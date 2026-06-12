using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for World Tour character creation screen.
/// Uses polling via UIFlowManager handles + direct field reads.
/// </summary>
public class AvatarCreateHooks
{
    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);

    private const int VK_F11 = 0x7A;
    private static bool _lastF11State;
    private static bool _isDumping;

    private static Method _msgGetMethod;

    // ---- Param polling ----
    private static ManagedObject _avatarParam;
    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;

    // ---- Cached TDB fields ----
    private static Field _handlesField;
    private static Field _guideDescField;
    private static Field _currentMainCatField;
    private static Field _currentMiddleCatField;
    private static bool _tdbCached;
    private static bool _paramFieldsCached;

    // ---- State tracking ----
    private static int _lastMainCategory = -1;
    private static int _lastMiddleCategory = -1;
    private static string _lastGuideDesc = "";

    // ---- Child flow tracking ----
    private static ManagedObject _childFlowParam;
    private static string _lastChildFlowType = "";

    // ---- Slider tracking ----
    private static ManagedObject[] _sliders;
    private static string[] _sliderNames;
    private static float[] _sliderValues;
    private static bool _slidersDiscovered;

    // ---- Guid cache ----
    private static readonly Dictionary<string, string> _guidCache = new();

    private static string DumpPath =>
        Path.Combine(Services.ObjectDumper.DumpDir, "sf6access_avatar_dump.txt");

    private static readonly string[] MainCategoryNames =
    {
        "Type", "Preset", "Body", "Face", "Body Paint",
        "Face Paint", "Color", "Voice", "Recipe"
    };

    private static readonly Dictionary<int, string[]> SubCategoryNames = new()
    {
        [0] = new[] { "Body Type", "Gender Identity" },
        [1] = new[] { "Face Preset", "Body Preset", "Random", "Blend" },
        [2] = new[] { "Height", "Upper Body", "Lower Body", "Build", "Skin Color", "Body Hair" },
        [3] = new[] { "Face Shape", "Hair", "Eyes", "Pupils", "Eyelashes", "Eyebrows", "Nose", "Mouth", "Ears", "Beard", "Age", "Contour", "Expression" },
        [4] = new[] { "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5" },
        [5] = new[] { "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5" },
        [6] = new[] { "Skin", "Body Hair", "Hair", "Pupils", "Eyelashes", "Eyebrows", "Beard", "Body Paint", "Face Paint" },
        [7] = new[] { "Voice" },
        [8] = new[] { "Recipe" },
    };

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        HandleF11Dump();
        PollAvatarParam();

        if (!_isActive) return;

        CacheParamFields();
        PollMainCategory();
        PollMiddleCategory();
        PollGuideDescription();
        PollChildFlow();
        PollContentList();
    }

    #region Param Discovery

    private static void PollAvatarParam()
    {
        _pollCounter++;
        if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;

        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) return;

            if (!_tdbCached)
            {
                _tdbCached = true;
                _handlesField = TDB.Get().FindType("app.UIFlowManager")?.GetField("_Handles");
            }
            if (_handlesField == null) return;

            ulong mgrAddr = flowMgr.GetAddress();
            if (mgrAddr == 0) return;
            var handles = _handlesField.GetDataBoxed(typeof(ManagedObject), mgrAddr, false) as ManagedObject;
            if (handles == null) return;

            var countObj = (handles as IObject)?.Call("get_Count");
            if (countObj == null) return;
            int count = Convert.ToInt32(countObj);

            bool foundMain = false;
            ManagedObject firstChildParam = null;
            string firstChildType = "";

            for (int i = 0; i < count; i++)
            {
                ManagedObject handle;
                try { handle = (handles as IObject)?.Call("get_Item", i) as ManagedObject; }
                catch { continue; }
                if (handle == null) continue;

                try { if ((handle as IObject)?.Call("get_IsEnd") is true) continue; }
                catch { }

                ManagedObject param;
                try { param = (handle as IObject)?.Call("GetParam") as ManagedObject; }
                catch { continue; }
                if (param == null) continue;

                string typeName = param.GetTypeDefinition()?.FullName ?? "";

                if (typeName.Contains("UIFlowUI61000.Param"))
                {
                    foundMain = true;
                    // Re-bind when the instance changed (recreated on re-entry)
                    if (FlowHelper.AddressOf(param) != FlowHelper.AddressOf(_avatarParam))
                    {
                        _avatarParam = param;
                        _isActive = true;
                        API.LogInfo($"[SF6Access] Avatar Param found: {typeName}");
                    }
                }
                // Track the first non-61000 worldtour flow as potential child flow
                else if (typeName.Contains("app.worldtour.UIFlow") && typeName.Contains(".Param")
                         && !typeName.Contains("UIFlowUI61000") && firstChildParam == null)
                {
                    firstChildParam = param;
                    firstChildType = typeName;
                }
            }

            // Update child flow — compare the INSTANCE too: re-entering the
            // same sub-menu (hair, face...) recreates a param of the same type
            // and the old one would keep being read (silent menu)
            if (foundMain)
            {
                if (firstChildType != _lastChildFlowType ||
                    FlowHelper.AddressOf(firstChildParam) != FlowHelper.AddressOf(_childFlowParam))
                {
                    _childFlowParam = firstChildParam;
                    _lastChildFlowType = firstChildType;
                    ResetSliders();
                    if (!string.IsNullOrEmpty(firstChildType))
                        API.LogInfo($"[SF6Access] Child flow: {firstChildType}");
                }
            }

            if (!foundMain && _isActive)
            {
                _avatarParam = null;
                _childFlowParam = null;
                _isActive = false;
                _lastChildFlowType = "";
                ResetState();
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] PollAvatarParam error: {ex.Message}");
        }
    }

    private static void CacheParamFields()
    {
        if (_paramFieldsCached || _avatarParam == null) return;
        _paramFieldsCached = true;

        try
        {
            var td = _avatarParam.GetTypeDefinition();
            if (td == null) return;

            var fields = td.GetFields();
            if (fields == null) return;

            foreach (var f in fields)
            {
                string name = f.Name ?? "";
                if (name == "GuideDescriptionString") _guideDescField = f;
                else if (name == "CurrentMainCategory") _currentMainCatField = f;
                else if (name == "CurrentMiddleCategory") _currentMiddleCatField = f;
            }

            API.LogInfo($"[SF6Access] Param fields cached: guide={_guideDescField != null}, mainCat={_currentMainCatField != null}, midCat={_currentMiddleCatField != null}");
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] CacheParamFields error: {ex.Message}");
        }
    }

    private static void ResetState()
    {
        _lastMainCategory = -1;
        _lastMiddleCategory = -1;
        _lastGuideDesc = "";
        _paramFieldsCached = false;
        _guideDescField = null;
        _currentMainCatField = null;
        _currentMiddleCatField = null;
        ResetSliders();
    }

    private static void ResetSliders()
    {
        _sliders = null;
        _sliderNames = null;
        _sliderValues = null;
        _slidersDiscovered = false;
        _contentList = null;
        _lastContentIndex = -1;
        _lastContentMax = -1;
        _isGroupMode = false;
    }

    #endregion

    #region State Polling

    private static void PollMainCategory()
    {
        try
        {
            int cat = -1;

            // Try field read first (most reliable)
            if (_currentMainCatField != null)
            {
                ulong addr = _avatarParam.GetAddress();
                var val = _currentMainCatField.GetDataBoxed(typeof(int), addr, false);
                if (val != null) cat = Convert.ToInt32(val);
            }

            // Fallback to UI component
            if (cat < 0)
            {
                var mainList = (_avatarParam as IObject)?.Call("get_PartsSimpleListMainCategory") as ManagedObject;
                if (mainList == null) return;
                var idxObj = (mainList as IObject)?.Call("get_SelectedIndex");
                if (idxObj == null) return;
                cat = Convert.ToInt32(idxObj);
            }

            if (cat == _lastMainCategory) return;
            _lastMainCategory = cat;
            _lastMiddleCategory = -1;

            string name = GetMainCategoryName(cat);
            API.LogInfo($"[SF6Access] Main category: {cat} = {name}");
            ScreenReaderService.Speak(name);
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] PollMainCategory error: {ex.Message}");
        }
    }

    private static void PollMiddleCategory()
    {
        try
        {
            int mid = -1;

            if (_currentMiddleCatField != null)
            {
                ulong addr = _avatarParam.GetAddress();
                var val = _currentMiddleCatField.GetDataBoxed(typeof(int), addr, false);
                if (val != null) mid = Convert.ToInt32(val);
            }

            if (mid < 0)
            {
                var scrollList = (_avatarParam as IObject)?.Call("get_PartsScrollListMiddleItem") as ManagedObject;
                if (scrollList == null) return;
                var idxObj = (scrollList as IObject)?.Call("get_SelectedIndex");
                if (idxObj == null) return;
                mid = Convert.ToInt32(idxObj);
            }

            if (mid == _lastMiddleCategory) return;
            _lastMiddleCategory = mid;

            string name = GetSubCategoryName(_lastMainCategory, mid);
            API.LogInfo($"[SF6Access] Sub-category: {mid} = {name}");
            ScreenReaderService.Speak(name);
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] PollMiddleCategory error: {ex.Message}");
        }
    }

    /// <summary>
    /// Poll GuideDescriptionString field - announces when guide text changes
    /// (e.g. entering a sub-menu shows description of what you're editing)
    /// </summary>
    private static void PollGuideDescription()
    {
        if (_guideDescField == null) return;

        try
        {
            ulong addr = _avatarParam.GetAddress();
            var val = _guideDescField.GetDataBoxed(typeof(string), addr, false);
            string desc = val as string ?? "";

            if (desc == _lastGuideDesc || string.IsNullOrEmpty(desc)) return;
            _lastGuideDesc = desc;

            string cleaned = CleanTags(desc);
            if (!string.IsNullOrEmpty(cleaned))
            {
                API.LogInfo($"[SF6Access] Guide: {cleaned}");
                ScreenReaderService.Speak(cleaned, false); // Don't interrupt category announcements
            }
        }
        catch { }
    }

    /// <summary>
    /// Poll child flow's PartsSliderAry for value changes.
    /// Uses getValue() for absolute values (real height, etc.) and getRate() as fallback.
    /// Names come from the *Buffer fields on the child flow.
    /// </summary>
    private static void PollChildFlow()
    {
        if (_childFlowParam == null) return;

        try
        {
            if (!_slidersDiscovered)
            {
                _slidersDiscovered = true;
                DiscoverSliders();
            }

            if (_sliders == null || _sliders.Length == 0) return;

            for (int i = 0; i < _sliders.Length; i++)
            {
                if (_sliders[i] == null) continue;

                var valObj = (_sliders[i] as IObject)?.Call("getValue");
                if (valObj == null) continue;
                float value = Convert.ToSingle(valObj);

                if (MathF.Abs(value - _sliderValues[i]) < 0.001f) continue;
                _sliderValues[i] = value;

                string name = (i < _sliderNames.Length && !string.IsNullOrEmpty(_sliderNames[i]))
                    ? _sliderNames[i] : null;
                string formatted = FormatSliderValue(name, value);

                string announcement = name != null ? $"{name}: {formatted}" : formatted;
                API.LogInfo($"[SF6Access] Slider [{i}]: {announcement} (raw={value:F4})");
                ScreenReaderService.Speak(announcement);
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] PollChildFlow error: {ex.Message}");
        }
    }

    /// <summary>
    /// Format slider value with context-aware display.
    /// Height slider (0-100) maps to real height in cm using community-researched data.
    /// </summary>
    private static string FormatSliderValue(string name, float rawValue)
    {
        int val = (int)MathF.Round(rawValue);

        if (name == "Height" || name == "Sitting Height")
        {
            int cm = SliderToCm(val);
            return $"{val}: {cm} cm";
        }

        return $"{val}";
    }

    // Exact height lookup table (slider 0-100 → cm), from community research.
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

    private static int SliderToCm(int sliderValue)
    {
        int clamped = Math.Clamp(sliderValue, 0, 100);
        return HeightTable[clamped];
    }

    // ---- Scroll grid/list tracking (for non-slider content like hair, presets) ----
    private static ManagedObject _contentList;
    private static int _lastContentIndex = -1;
    private static int _lastContentMax = -1;
    private static bool _isGroupMode;

    private static void PollContentList()
    {
        if (_contentList == null) return;

        try
        {
            int idx = -1;
            // Try all index methods until one works
            foreach (var m in IndexMethods)
            {
                try
                {
                    var val = (_contentList as IObject)?.Call(m);
                    if (val != null) { idx = Convert.ToInt32(val); break; }
                }
                catch { }
            }

            if (idx < 0 || idx == _lastContentIndex) return;
            _lastContentIndex = idx;

            string announcement = $"{idx + 1} of {_lastContentMax}";
            API.LogInfo($"[SF6Access] Content: {announcement}");
            ScreenReaderService.Speak(announcement);
        }
        catch { }
    }

    private static void DiscoverSliders()
    {
        try
        {
            // Get slider names from *Buffer fields
            var td = _childFlowParam.GetTypeDefinition();
            var fields = td?.GetFields();
            var names = new List<string>();

            if (fields != null)
            {
                foreach (var f in fields)
                {
                    string fname = f.Name ?? "";
                    if (f.Type?.FullName == "System.Single" && fname.EndsWith("Buffer"))
                    {
                        string displayName = fname.Replace("Buffer", "");
                        displayName = Regex.Replace(displayName, @"(?<=[a-z])(?=[A-Z])", " ");
                        names.Add(displayName);
                    }
                }
            }

            // Get PartsSliderAry
            ManagedObject sliderArray = null;
            try { sliderArray = (_childFlowParam as IObject)?.Call("get_PartsSliderAry") as ManagedObject; }
            catch { }
            if (sliderArray == null)
            {
                // No sliders - try to find scroll grid or list for preset selection
                API.LogInfo("[SF6Access] No sliders, searching for content list...");
                DiscoverContentList();
                return;
            }

            var lenObj = (sliderArray as IObject)?.Call("get_Length");
            if (lenObj == null) return;
            int len = Convert.ToInt32(lenObj);

            _sliders = new ManagedObject[len];
            _sliderValues = new float[len];
            _sliderNames = names.Count >= len ? names.ToArray() : names.ToArray();

            for (int i = 0; i < len; i++)
            {
                try
                {
                    _sliders[i] = (sliderArray as IObject)?.Call("Get", i) as ManagedObject;
                    if (_sliders[i] != null)
                    {
                        var valObj = (_sliders[i] as IObject)?.Call("getValue");
                        _sliderValues[i] = valObj != null ? Convert.ToSingle(valObj) : 0f;
                    }
                }
                catch { }
            }

            // Log what we found
            var sliderInfo = new List<string>();
            for (int i = 0; i < len; i++)
            {
                string n = i < _sliderNames.Length ? _sliderNames[i] : $"#{i}";
                sliderInfo.Add($"{n}={_sliderValues[i]:F2}");
            }
            API.LogInfo($"[SF6Access] Found {len} sliders: {string.Join(", ", sliderInfo)}");
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] DiscoverSliders error: {ex.Message}");
        }
    }

    /// <summary>
    /// For child flows without sliders (hair, presets, type selection etc.),
    /// find a scroll grid, list, or group component to track selection.
    /// Searches both methods and fields on the child flow.
    /// </summary>
    private static void DiscoverContentList()
    {
        try
        {
            // Strategy 1: Search child flow methods for UI components
            if (_childFlowParam != null)
            {
                API.LogInfo("[SF6Access] Strategy 1: searching methods...");
                var found = TryFindListOnObject(_childFlowParam, "child flow");
                if (found) return;

                // Strategy 2: Search child flow FIELDS for UI components
                API.LogInfo("[SF6Access] Strategy 2: searching fields...");
                var td = _childFlowParam.GetTypeDefinition();
                var fields = td?.GetFields();
                if (fields != null)
                {
                    ulong addr = _childFlowParam.GetAddress();
                    foreach (var f in fields)
                    {
                        string ftype = f.Type?.FullName ?? "";
                        if (!ftype.Contains("UIParts") && !ftype.Contains("ScrollGrid") &&
                            !ftype.Contains("ScrollList") && !ftype.Contains("SimpleList"))
                            continue;

                        API.LogInfo($"[SF6Access] Trying field: {ftype} {f.Name}");
                        try
                        {
                            var component = f.GetDataBoxed(typeof(ManagedObject), addr, false) as ManagedObject;
                            if (component == null) { API.LogInfo("[SF6Access]   -> null"); continue; }

                            API.LogInfo($"[SF6Access]   -> got object, trying read...");
                            if (TryReadListComponent(component, $"field {f.Name}"))
                                return;
                        }
                        catch (Exception ex) { API.LogInfo($"[SF6Access]   -> error: {ex.Message}"); }
                    }
                }
            }

            // Strategy 3: PartsMainCtrl on root param (FocusIndex for group navigation)
            if (_avatarParam != null)
            {
                try
                {
                    var mainCtrl = (_avatarParam as IObject)?.Call("get_PartsMainCtrl") as ManagedObject;
                    if (mainCtrl != null && TryReadGroupComponent(mainCtrl, "PartsMainCtrl"))
                        return;
                }
                catch { }
            }

            API.LogInfo("[SF6Access] No content list found on child flow");
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] DiscoverContentList error: {ex.Message}");
        }
    }

    private static bool TryFindListOnObject(ManagedObject obj, string label)
    {
        var td = obj.GetTypeDefinition();
        var methods = td?.GetMethods();
        if (methods == null) return false;

        foreach (var m in methods)
        {
            string mName = m.Name ?? "";
            if (!mName.StartsWith("get_")) continue;

            string returnType = m.ReturnType?.FullName ?? "";
            if (!returnType.Contains("ScrollGrid") && !returnType.Contains("ScrollList") &&
                !returnType.Contains("SimpleList") && !returnType.Contains("PresetScrollGrid"))
                continue;

            try
            {
                var component = (obj as IObject)?.Call(mName) as ManagedObject;
                if (component == null) continue;
                if (TryReadListComponent(component, $"{label}.{mName}"))
                    return true;
            }
            catch { }
        }
        return false;
    }

    // Method names to try for reading selection index
    private static readonly string[] IndexMethods = {
        "get_SelectedIndex", "GetFocusIndex", "get_FocusIndex"
    };
    // Method names to try for reading item count
    private static readonly string[] CountMethods = {
        "get_ItemMax", "get_ItemCount", "GetChildCount", "get_Count"
    };

    private static bool TryReadListComponent(ManagedObject component, string label)
    {
        // Try direct read first
        if (TryReadIndexAndCount(component, label))
            return true;

        // If direct read fails, try sub-components (e.g. PartsWorker on PresetScrollGrid)
        string[] subComponents = { "get_PartsWorker", "get__List", "get__Grid" };
        foreach (var sub in subComponents)
        {
            try
            {
                var inner = (component as IObject)?.Call(sub) as ManagedObject;
                if (inner != null && TryReadIndexAndCount(inner, $"{label}.{sub}"))
                    return true;
            }
            catch { }
        }

        return false;
    }

    private static bool TryReadIndexAndCount(ManagedObject component, string label)
    {
        int idx = -1, max = -1;
        string idxMethod = null;

        foreach (var m in IndexMethods)
        {
            try
            {
                var val = (component as IObject)?.Call(m);
                API.LogInfo($"[SF6Access]   {label}.{m} = {val}");
                if (val != null) { idx = Convert.ToInt32(val); idxMethod = m; break; }
            }
            catch (Exception ex) { API.LogInfo($"[SF6Access]   {label}.{m} FAIL: {ex.Message}"); }
        }

        foreach (var m in CountMethods)
        {
            try
            {
                var val = (component as IObject)?.Call(m);
                API.LogInfo($"[SF6Access]   {label}.{m} = {val}");
                if (val != null && Convert.ToInt32(val) > 0) { max = Convert.ToInt32(val); break; }
            }
            catch (Exception ex) { API.LogInfo($"[SF6Access]   {label}.{m} FAIL: {ex.Message}"); }
        }

        if (idx < 0 || max <= 0) return false;

        _contentList = component;
        _lastContentIndex = idx;
        _lastContentMax = max;
        _isGroupMode = idxMethod != "get_SelectedIndex";
        API.LogInfo($"[SF6Access] Content via {label}: idx={idx}, max={max} ({idxMethod})");
        return true;
    }

    private static bool TryReadGroupComponent(ManagedObject group, string label)
    {
        return TryReadListComponent(group, label);
    }

    #endregion

    #region Name Resolution

    private static string GetMainCategoryName(int index)
    {
        if (index >= 0 && index < MainCategoryNames.Length)
            return MainCategoryNames[index];
        return $"Category {index}";
    }

    private static string GetSubCategoryName(int mainIndex, int subIndex)
    {
        if (mainIndex >= 0 && SubCategoryNames.TryGetValue(mainIndex, out var subs))
        {
            if (subIndex >= 0 && subIndex < subs.Length)
                return subs[subIndex];
        }
        return $"Item {subIndex + 1}";
    }

    private static string ResolveGuid(REFrameworkNET.ValueType vt, string cacheKey)
    {
        if (_guidCache.TryGetValue(cacheKey, out var cached))
            return cached;

        ulong vtAddr = vt.GetAddress();
        if (vtAddr == 0) return null;

        bool allZero = true;
        for (int i = 0; i < 16; i++)
        {
            if (Marshal.ReadByte((IntPtr)(long)(vtAddr + (ulong)i)) != 0)
            { allZero = false; break; }
        }
        if (allZero) return null;

        _msgGetMethod ??= TDB.Get().FindType("via.gui.message")?.GetMethod("get(System.Guid)");
        if (_msgGetMethod == null) return null;

        try
        {
            var task = Task.Run(() =>
            {
                try { return _msgGetMethod.InvokeBoxed(typeof(string), null, new object[] { vt }) as string; }
                catch { return null; }
            });

            if (task.Wait(TimeSpan.FromMilliseconds(200)))
            {
                string text = CleanTags(task.Result);
                if (!string.IsNullOrEmpty(text))
                    _guidCache[cacheKey] = text;
                return text;
            }
        }
        catch { }

        return null;
    }

    private static string CleanTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(Regex.Replace(text, @"<[^>]+>", "").Trim(), @"\s+", " ").Trim();
    }

    #endregion

    #region F11 Dump

    private static void HandleF11Dump()
    {
        bool f11Down = (GetAsyncKeyState(VK_F11) & 0x8000) != 0;
        if (f11Down && !_lastF11State && !_isDumping)
        {
            _isDumping = true;
            try
            {
                DumpAvatarState();
                ScreenReaderService.Speak("Avatar dump complete");
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] Avatar dump failed: {ex.Message}");
            }
            finally { _isDumping = false; }
        }
        _lastF11State = f11Down;
    }

    private static void DumpAvatarState()
    {
        var sb = new StringBuilder();
        sb.AppendLine($"=== SF6 Avatar State Dump - {DateTime.Now:yyyy-MM-dd HH:mm:ss} ===");
        sb.AppendLine($"AvatarParam: {(_avatarParam != null ? "found" : "null")}");
        sb.AppendLine($"IsActive: {_isActive}");
        sb.AppendLine($"LastMainCategory: {_lastMainCategory}");
        sb.AppendLine($"LastMiddleCategory: {_lastMiddleCategory}");
        sb.AppendLine($"ChildFlowType: {_lastChildFlowType}");
        sb.AppendLine();

        DumpFlowHandles(sb);

        if (_avatarParam != null)
            DumpAvatarParamState(sb);

        if (_childFlowParam != null)
            DumpChildFlowState(sb);

        Directory.CreateDirectory(Path.GetDirectoryName(DumpPath)!);
        File.WriteAllText(DumpPath, sb.ToString());
        API.LogInfo($"[SF6Access] Avatar state dump saved to {DumpPath}");
    }

    private static void DumpAvatarParamState(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine("=== Avatar Param State ===");

        // Read fields directly
        try
        {
            var td = _avatarParam.GetTypeDefinition();
            ulong addr = _avatarParam.GetAddress();
            var fields = td?.GetFields();
            if (fields != null)
            {
                sb.AppendLine("  Key Fields:");
                foreach (var f in fields)
                {
                    string fname = f.Name ?? "";
                    if (fname.Contains("Category") || fname.Contains("Middle") ||
                        fname.Contains("Select") || fname.Contains("Activate") ||
                        fname.Contains("Guide") || fname.Contains("Description") ||
                        fname.Contains("Paint") || fname.Contains("Mouse"))
                    {
                        try
                        {
                            var val = f.GetDataBoxed(typeof(object), addr, false);
                            sb.AppendLine($"    {fname} = {val}");
                        }
                        catch (Exception ex) { sb.AppendLine($"    {fname} ERROR: {ex.Message}"); }
                    }
                }
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Fields error: {ex.Message}"); }
    }

    private static void DumpChildFlowState(StringBuilder sb)
    {
        sb.AppendLine();
        sb.AppendLine($"=== Child Flow: {_lastChildFlowType} ===");

        try
        {
            var td = _childFlowParam.GetTypeDefinition();
            ulong addr = _childFlowParam.GetAddress();

            // Dump ALL fields on child flow
            var fields = td?.GetFields();
            if (fields != null)
            {
                sb.AppendLine($"  Fields ({fields.Count}):");
                foreach (var f in fields)
                {
                    string fname = f.Name ?? "";
                    try
                    {
                        var val = f.GetDataBoxed(typeof(object), addr, false);
                        string valStr = val?.ToString() ?? "null";
                        // Truncate long values
                        if (valStr.Length > 100) valStr = valStr[..100] + "...";
                        sb.AppendLine($"    {f.Type?.FullName ?? "?"} {fname} = {valStr}");
                    }
                    catch (Exception ex) { sb.AppendLine($"    {fname} ERROR: {ex.Message}"); }
                }
            }

            // Dump methods
            var methods = td?.GetMethods();
            if (methods != null)
            {
                sb.AppendLine($"  Methods ({methods.Count}):");
                foreach (var m in methods)
                {
                    string name = m.Name ?? "?";
                    if (name.StartsWith("get_") || name.Contains("Select") ||
                        name.Contains("Group") || name.Contains("Item") ||
                        name.Contains("Slider") || name.Contains("update") ||
                        name.Contains("Scroll") || name.Contains("Focus"))
                    {
                        sb.AppendLine($"    {m.ReturnType?.FullName ?? "void"} {name}");
                    }
                }
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }
    }

    private static void DumpFlowHandles(StringBuilder sb)
    {
        sb.AppendLine("=== UIFlowManager Handles (worldtour only) ===");
        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) { sb.AppendLine("  null"); return; }

            if (!_tdbCached)
            {
                _tdbCached = true;
                _handlesField = TDB.Get().FindType("app.UIFlowManager")?.GetField("_Handles");
            }
            if (_handlesField == null) return;

            ulong mgrAddr = flowMgr.GetAddress();
            var handles = _handlesField.GetDataBoxed(typeof(ManagedObject), mgrAddr, false) as ManagedObject;
            if (handles == null) return;

            var countObj = (handles as IObject)?.Call("get_Count");
            int count = countObj != null ? Convert.ToInt32(countObj) : 0;

            for (int i = 0; i < count; i++)
            {
                try
                {
                    var handle = (handles as IObject)?.Call("get_Item", i) as ManagedObject;
                    if (handle == null) continue;

                    var param = (handle as IObject)?.Call("GetParam") as ManagedObject;
                    string paramType = param?.GetTypeDefinition()?.FullName ?? "null";

                    // Only show worldtour flows to keep dump readable
                    if (!paramType.Contains("worldtour")) continue;

                    var isEnd = (handle as IObject)?.Call("get_IsEnd");
                    sb.AppendLine($"  [{i}] {paramType} (IsEnd={isEnd})");
                }
                catch { }
            }
        }
        catch (Exception ex) { sb.AppendLine($"  Error: {ex.Message}"); }
    }

    private static void DumpCall(StringBuilder sb, ManagedObject obj, string methodName, string prefix = "  ")
    {
        try
        {
            var result = (obj as IObject)?.Call(methodName);
            sb.AppendLine($"{prefix}{methodName} = {result}");
        }
        catch (Exception ex)
        {
            sb.AppendLine($"{prefix}{methodName} ERROR: {ex.Message}");
        }
    }

    #endregion
}
