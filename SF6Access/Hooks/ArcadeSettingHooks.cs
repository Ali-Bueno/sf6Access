using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Announces value edits in the Arcade/Story game-settings menu
/// (app.UIFlowUI11101.Param: Difficulty, Rounds, Stages, Bonus Stages...).
/// Row navigation (up/down) is already read by the generic focus path; only
/// left/right value edits went silent because they don't move focus. Each row
/// keeps its current option in mSpinIndexCount[row], and the option label is
/// mMessData[row].TextList[index] — poll the array and announce the row whose
/// index changed. Migrated to ScreenAdapter (IsActive kept for FocusValueHooks).
/// </summary>
public sealed class ArcadeSettingHooks : SingleParamScreenAdapter
{
    private static ArcadeSettingHooks _self;

    /// <summary>True while the arcade settings menu owns value announcements,
    /// so the generic FocusValueHooks watcher doesn't double-read.</summary>
    public static bool IsActive => _self != null && _self.Active;

    protected override string ParamType => "app.UIFlowUI11101.Param";

    public ArcadeSettingHooks()
    {
        _self = this;
        SearchInterval = 30;
        ReadInterval = 5;
    }

    // Current spin index per row, to detect left/right edits
    private readonly Dictionary<int, int> _lastSpin = new();

    protected override void OnBind()
    {
        _lastSpin.Clear(); // re-baseline on (re-)entry
        API.LogInfo("[SF6Access] Arcade settings active");
    }

    protected override void OnExit()
    {
        _lastSpin.Clear();
    }

    protected override void Poll()
    {
        try
        {
            var spinArr = FlowHelper.GetObjectField(Param, "mSpinIndexCount");
            var messArr = FlowHelper.GetObjectField(Param, "mMessData");
            if (spinArr == null || messArr == null) return;

            int count = FlowHelper.GetListCount(spinArr);
            for (int row = 0; row < count; row++)
            {
                int spin = ReadArrayInt(spinArr, row);
                if (spin == int.MinValue) continue;

                bool known = _lastSpin.TryGetValue(row, out int last);
                _lastSpin[row] = spin;

                // First read = baseline (no announce); unchanged = skip
                if (!known || spin == last) continue;

                string value = ResolveSpinValue(messArr, row, spin);
                if (string.IsNullOrEmpty(value)) continue;

                API.LogInfo($"[SF6Access] Arcade setting [{row}] = {value}");
                ScreenReaderService.Speak(value);
            }
        }
        catch { }
    }

    /// <summary>Localized option label from mMessData[row].TextList[index].</summary>
    private static string ResolveSpinValue(ManagedObject messArr, int row, int index)
    {
        try
        {
            var messEntry = FlowHelper.GetListItem(messArr, row);
            if (messEntry == null) return null;

            var textList = FlowHelper.Call(messEntry, "get_TextList") as ManagedObject
                ?? FlowHelper.GetObjectField(messEntry, "TextList");
            if (textList == null) return null;
            if (index < 0 || index >= FlowHelper.GetListCount(textList)) return null;

            var guidObj = FlowHelper.Call(textList, "get_Item", index);
            if (guidObj is REFrameworkNET.ValueType vt)
                return FlowHelper.CleanTags(FlowHelper.ResolveGuid(vt));
        }
        catch { }
        return null;
    }

    private static int ReadArrayInt(ManagedObject arr, int index)
    {
        try
        {
            var val = FlowHelper.Call(arr, "Get", index);
            if (val != null) return System.Convert.ToInt32(val);
        }
        catch { }
        return int.MinValue;
    }
}
