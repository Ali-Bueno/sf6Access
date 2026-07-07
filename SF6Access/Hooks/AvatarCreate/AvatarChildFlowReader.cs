using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Generic reader for ONE avatar-creator child flow (UIFlowUI61xxx.Param or
/// the color popup UIFlowWTAvatarCreateColorPopUp.Param). Discovers the
/// param's UI parts by field type at bind time and polls them for changes:
/// - UIPartsAvatarCreatePresetScrollGrid → focused preset name
///   (CurrentSelectPresetData.PresetDataMessageInfo) + position
/// - plain UIPartsScrollGrid → color swatch index ("Color N of M"; the real
///   color is announced by AvatarColorWatcher when it applies to the model)
/// - UIPartsSlider / PartsSliderAry → value changes (names from the flow's
///   float *Buffer fields when present, else the field name via LangFile)
/// - UIPartsSpin → number + its *TextList entry when present
/// - UIPartsTriangleBar → cursor position (muscle/slim/fat triangle)
/// - UIPartsGroup / scroll lists → delegated to a GroupFocusPoller
/// All member names come from the decompiled game code; unverified screens
/// log what they find so silent menus can be diagnosed from the log.
/// </summary>
internal sealed class AvatarChildFlowReader
{
    private ManagedObject _param;
    private string _typeName = "";

    private readonly List<GridTracker> _presetGrids = new();
    private readonly List<SwatchTracker> _swatchGrids = new();
    private readonly List<SliderTracker> _sliders = new();
    private readonly List<SpinTracker> _spins = new();
    private readonly List<TriangleTracker> _triangles = new();
    private GroupFocusPoller _groups;

    public void Bind(ManagedObject param, string typeName)
    {
        _param = param;
        _typeName = typeName ?? "";
        _presetGrids.Clear();
        _swatchGrids.Clear();
        _sliders.Clear();
        _spins.Clear();
        _triangles.Clear();
        _groups = null;
        if (param == null) return;

        try { Discover(); }
        catch (Exception ex) { API.LogError($"[SF6Access] AvatarChildFlowReader.Bind: {ex.Message}"); }
    }

    public void Reset() => Bind(null, null);

    public void Poll()
    {
        if (_param == null) return;
        foreach (var g in _presetGrids) PollPresetGrid(g);
        foreach (var s in _swatchGrids) PollSwatchGrid(s);
        PollSliderArray();
        foreach (var s in _sliders) PollSlider(s);
        foreach (var s in _spins) PollSpin(s);
        foreach (var t in _triangles) PollTriangle(t);
        _groups?.Poll(_param);
    }

    #region Discovery

    private string _sliderArrayField;
    private string[] _sliderArrayNames = Array.Empty<string>();
    private float[] _sliderArrayValues = Array.Empty<float>();
    private bool _sliderArraySeeded;

    private void Discover()
    {
        var td = _param.GetTypeDefinition();
        var fields = td?.GetFields();
        if (fields == null) return;

        var bufferNames = new List<string>();
        var groupSources = new List<GroupFocusPoller.Source>();
        var found = new List<string>();

        foreach (var f in fields)
        {
            string name = f.Name ?? "";
            string type = f.Type?.FullName ?? "";

            if (type.Contains("UIPartsAvatarCreatePresetScrollGrid"))
            {
                _presetGrids.Add(new GridTracker { Field = name });
                found.Add($"presetGrid:{name}");
            }
            else if (type.EndsWith("UIPartsScrollGrid"))
            {
                _swatchGrids.Add(new SwatchTracker { Field = name });
                found.Add($"swatchGrid:{name}");
            }
            else if (type.Contains("UIPartsSlider[]"))
            {
                _sliderArrayField = name;
                found.Add($"sliderArray:{name}");
            }
            else if (type.EndsWith("UIPartsSlider"))
            {
                _sliders.Add(new SliderTracker { Field = name, Label = SliderLabel(name) });
                found.Add($"slider:{name}");
            }
            else if (type.Contains("UIPartsTriangleBar"))
            {
                _triangles.Add(new TriangleTracker { Field = name });
                found.Add($"triangle:{name}");
            }
            else if (type.Contains("UIPartsSpin"))
            {
                _spins.Add(new SpinTracker { Field = name });
                found.Add($"spin:{name}");
            }
            else if (type.EndsWith("UIPartsGroup"))
            {
                groupSources.Add(new GroupFocusPoller.Source(null, name));
                found.Add($"group:{name}");
            }
            else if (type.Contains("UIPartsScrollList") || type.Contains("UIPartsSimpleList"))
            {
                groupSources.Add(new GroupFocusPoller.Source(null, name, isList: true));
                found.Add($"list:{name}");
            }
            else if (type == "System.Single" && name.EndsWith("Buffer"))
            {
                bufferNames.Add(SplitCamel(name.Substring(0, name.Length - "Buffer".Length)));
            }
        }

        _sliderArrayNames = bufferNames.ToArray();
        _sliderArrayValues = Array.Empty<float>();
        _sliderArraySeeded = false;

        if (groupSources.Count > 0)
            _groups = new GroupFocusPoller("avatar-child", announceFirst: true, groupSources.ToArray());

        API.LogInfo($"[SF6Access] Avatar child {_typeName}: {(found.Count > 0 ? string.Join(", ", found) : "no known parts")}");
    }

    #endregion

    #region Preset grids (named items)

    private sealed class GridTracker
    {
        public string Field;
        public int LastIndex = int.MinValue;
        public int LastPage = int.MinValue;
    }

    private void PollPresetGrid(GridTracker t)
    {
        try
        {
            var grid = FlowHelper.GetObjectField(_param, t.Field);
            if (grid == null) return;

            var worker = FlowHelper.GetObjectField(grid, "PartsWorker")
                         ?? FlowHelper.Call(grid, "get_PartsWorker") as ManagedObject;
            if (worker == null) return;

            int idx = FlowHelper.CallInt(worker, "get_SelectedIndex");
            if (idx < 0) return;
            int max = FlowHelper.CallInt(worker, "get_ItemMax");
            int page = FlowHelper.ReadIntField(grid, "CurrentPageNum", -1);
            if (page < 0) page = FlowHelper.CallInt(grid, "get_CurrentPageNum");

            bool first = t.LastIndex == int.MinValue;
            if (idx == t.LastIndex && page == t.LastPage) return;
            t.LastIndex = idx;
            t.LastPage = page;
            if (first) return; // seed silently; announcements on navigation only

            string name = ReadPresetName(grid);
            string pos = max > 0
                ? string.Format(LangFile.Get("n_of_m", "{0} of {1}"), idx + 1, max)
                : (idx + 1).ToString();

            // Two-grid screens (pupils/eyebrows) mark the second grid "Left"
            string side = t.Field.EndsWith("Left") ? LangFile.Get("left", "Left") + ". " : "";

            string text = string.IsNullOrEmpty(name) ? $"{side}{pos}" : $"{side}{name}. {pos}";
            API.LogInfo($"[SF6Access] Avatar preset {t.Field}[page {page}, {idx}]: {text}");
            ScreenReaderService.Speak(text, interrupt: true);
        }
        catch { }
    }

    private static string ReadPresetName(ManagedObject grid)
    {
        try
        {
            var data = FlowHelper.Call(grid, "get_CurrentSelectPresetData") as ManagedObject;
            if (data == null) return null;
            string raw = FlowHelper.Call(data, "get_PresetDataMessageInfo") as string;
            return string.IsNullOrWhiteSpace(raw) ? null : FlowHelper.CleanTags(raw);
        }
        catch { return null; }
    }

    #endregion

    #region Color swatch grids (index-only)

    private sealed class SwatchTracker
    {
        public string Field;
        public int LastIndex = int.MinValue;
    }

    private void PollSwatchGrid(SwatchTracker t)
    {
        try
        {
            var grid = FlowHelper.GetObjectField(_param, t.Field);
            if (grid == null) return;

            int idx = FlowHelper.CallInt(grid, "get_SelectedIndex");
            if (idx < 0) return;
            int max = FlowHelper.CallInt(grid, "get_ItemMax");

            bool first = t.LastIndex == int.MinValue;
            if (idx == t.LastIndex) return;
            t.LastIndex = idx;
            if (first) return;

            string pos = max > 0
                ? string.Format(LangFile.Get("n_of_m", "{0} of {1}"), idx + 1, max)
                : (idx + 1).ToString();
            string text = $"{LangFile.Get("color_swatch", "Color")} {pos}";
            API.LogInfo($"[SF6Access] Avatar swatch {t.Field}[{idx}]: {text}");
            ScreenReaderService.Speak(text, interrupt: true);
        }
        catch { }
    }

    #endregion

    #region Sliders

    private sealed class SliderTracker
    {
        public string Field;
        public string Label;
        public float LastValue = float.NaN;
    }

    private void PollSlider(SliderTracker t)
    {
        try
        {
            var slider = FlowHelper.GetObjectField(_param, t.Field);
            if (slider == null) return;

            var valObj = FlowHelper.Call(slider, "getValue");
            if (valObj == null) return;
            float value = Convert.ToSingle(valObj);

            bool first = float.IsNaN(t.LastValue);
            if (!first && MathF.Abs(value - t.LastValue) < 0.001f) return;
            t.LastValue = value;
            if (first) return;

            string text = $"{t.Label}: {FormatSliderValue(t.Label, value)}";
            API.LogInfo($"[SF6Access] Avatar slider {t.Field} = {value:F3} -> {text}");
            ScreenReaderService.Speak(text, interrupt: true);
        }
        catch { }
    }

    private void PollSliderArray()
    {
        if (_sliderArrayField == null) return;
        try
        {
            var arr = FlowHelper.GetObjectField(_param, _sliderArrayField);
            if (arr == null) return;

            var lenObj = FlowHelper.Call(arr, "get_Length");
            if (lenObj == null) return;
            int len = Convert.ToInt32(lenObj);
            if (len <= 0) return;

            if (_sliderArrayValues.Length != len)
            {
                _sliderArrayValues = new float[len];
                _sliderArraySeeded = false;
            }

            for (int i = 0; i < len; i++)
            {
                var slider = FlowHelper.Call(arr, "Get", i) as ManagedObject;
                if (slider == null) continue;

                var valObj = FlowHelper.Call(slider, "getValue");
                if (valObj == null) continue;
                float value = Convert.ToSingle(valObj);

                if (!_sliderArraySeeded)
                {
                    _sliderArrayValues[i] = value;
                    continue;
                }
                if (MathF.Abs(value - _sliderArrayValues[i]) < 0.001f) continue;
                _sliderArrayValues[i] = value;

                string name = i < _sliderArrayNames.Length ? _sliderArrayNames[i] : null;
                string formatted = FormatSliderValue(name, value);
                string text = name != null ? $"{name}: {formatted}" : formatted;
                API.LogInfo($"[SF6Access] Avatar slider [{i}] = {value:F3} -> {text}");
                ScreenReaderService.Speak(text, interrupt: true);
            }
            _sliderArraySeeded = true;
        }
        catch { }
    }

    /// <summary>Spoken name for an individual slider field, translatable per
    /// field name (avslider.* keys); falls back to the camel-split field name.</summary>
    private static string SliderLabel(string fieldName) =>
        LangFile.Get("avslider." + fieldName, SplitCamel(fieldName));

    private static string SplitCamel(string name) =>
        Regex.Replace(name, @"(?<=[a-z])(?=[A-Z])", " ");

    /// <summary>
    /// Height sliders speak the real height in cm (community-measured table —
    /// the game renders the cm value as a texture, unreadable as text).
    /// </summary>
    private static string FormatSliderValue(string name, float rawValue)
    {
        int val = (int)MathF.Round(rawValue);
        if (name == "Height" || name == "Sitting Height")
            return $"{val}: {SliderToCm(val)} cm";
        // Near-integer values speak as integers, fine-grained ones keep a decimal
        if (MathF.Abs(rawValue - val) < 0.01f) return val.ToString();
        return rawValue.ToString("F1");
    }

    // Slider 0-100 → cm, from community research (see class doc).
    private static readonly int[] HeightTable =
    {
        109, 110, 112, 113, 115, 116, 117, 119, 120, 121, // 0-9
        122, 123, 125, 126, 127, 128, 130, 131, 132, 133, // 10-19
        135, 136, 137, 139, 140, 141, 142, 144, 145, 146, // 20-29
        147, 149, 150, 151, 153, 154, 155, 157, 158, 159, // 30-39
        160, 161, 163, 164, 165, 167, 168, 169, 170, 171, // 40-49
        173, 174, 175, 176, 178, 179, 180, 182, 183, 184, // 50-59
        185, 186, 188, 189, 190, 191, 193, 194, 195, 196, // 60-69
        198, 199, 200, 201, 203, 204, 205, 207, 208, 209, // 70-79
        210, 212, 213, 214, 216, 217, 218, 220, 221, 222, // 80-89
        223, 225, 226, 227, 229, 230, 231, 232, 234, 235, // 90-99
        236                                                 // 100
    };

    private static int SliderToCm(int sliderValue) =>
        HeightTable[Math.Clamp(sliderValue, 0, 100)];

    #endregion

    #region Spins (eyelash upper/lower type selectors)

    private sealed class SpinTracker
    {
        public string Field;
        public int LastNum = int.MinValue;
    }

    private void PollSpin(SpinTracker t)
    {
        try
        {
            var spin = FlowHelper.GetObjectField(_param, t.Field);
            if (spin == null) return;

            int num = FlowHelper.CallInt(spin, "get_Num");
            if (num < 0) num = FlowHelper.ReadIntField(spin, "_Num", -1);
            if (num < 0) return;

            bool first = t.LastNum == int.MinValue;
            if (num == t.LastNum) return;
            t.LastNum = num;
            if (first) return;

            // 61404 pairs each spin with a "<field>TextList" of via.gui.Text
            string text = null;
            var textList = FlowHelper.GetObjectField(_param, t.Field + "TextList");
            if (textList != null)
            {
                var item = FlowHelper.GetListItem(textList, num);
                text = FlowHelper.ReadGuiText(item);
            }
            if (string.IsNullOrEmpty(text)) text = (num + 1).ToString();

            API.LogInfo($"[SF6Access] Avatar spin {t.Field} = {num}: {text}");
            ScreenReaderService.Speak(text, interrupt: true);
        }
        catch { }
    }

    #endregion

    #region Triangle bar (muscle / slim / fat physique blend)

    private sealed class TriangleTracker
    {
        public string Field;
        public float LastX = float.NaN, LastY = float.NaN;
    }

    private void PollTriangle(TriangleTracker t)
    {
        try
        {
            var bar = FlowHelper.GetObjectField(_param, t.Field);
            if (bar == null) return;

            var pos = FlowHelper.ReadVec2Field(bar, "_CurrentPos");
            if (pos == null) return;
            (float x, float y) = pos.Value;

            bool first = float.IsNaN(t.LastX);
            if (!first && MathF.Abs(x - t.LastX) < 0.005f && MathF.Abs(y - t.LastY) < 0.005f) return;
            t.LastX = x;
            t.LastY = y;
            if (first) return;

            // Raw range unverified (likely 0..1) — scale defensively and log raw
            float sx = MathF.Abs(x) <= 1.05f ? x * 100f : x;
            float sy = MathF.Abs(y) <= 1.05f ? y * 100f : y;
            string text = string.Format(
                LangFile.Get("figure_pos", "Figure {0}, {1}"),
                MathF.Round(sx), MathF.Round(sy));
            API.LogInfo($"[SF6Access] Avatar triangle {t.Field} = ({x:F3}, {y:F3}) -> {text}");
            ScreenReaderService.Speak(text, interrupt: true);
        }
        catch { }
    }

    #endregion
}
