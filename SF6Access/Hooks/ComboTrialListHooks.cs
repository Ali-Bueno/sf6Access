using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Combo-trial selection list (app.esports.UI11414.Param). The rows themselves
/// are read by the generic GroupFocus reader; this adapter adds the per-trial
/// CLEAR status (shown only as a visual check mark): the focused row index in
/// PartsScrollListItem maps to CurrentItemDataInfoList, whose ItemDataInfo →
/// BattleComboTrialData.UniqueID keys the save data —
/// SystemSaveManager.Data.ComboTrialSaveData.IsClear(uniqueId). The status is
/// announced a few frames after the row so it queues behind the generic
/// announcement instead of being cut by it.
/// </summary>
public sealed class ComboTrialListHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.esports.UI11414.Param";

    // Queue the status behind the row name spoken by GroupFocusHooks.
    private const int STATUS_DELAY_FRAMES = 12;

    private int _frame;
    private int _lastIndex = int.MinValue;
    private string _pendingStatus;
    private int _pendingStatusFrame = -1;

    protected override void OnBind()
    {
        _lastIndex = int.MinValue;
        _pendingStatus = null;
        API.LogInfo("[SF6Access] Combo trial list active");
    }

    protected override void OnExit()
    {
        _lastIndex = int.MinValue;
        _pendingStatus = null;
    }

    protected override void Poll()
    {
        _frame++;

        var list = FlowHelper.GetObjectField(Param, "PartsScrollListItem");
        int idx = FlowHelper.CallInt(list, "get_SelectedIndex");
        if (idx >= 0 && idx != _lastIndex)
        {
            bool first = _lastIndex == int.MinValue;
            _lastIndex = idx;
            string status = ReadClearStatus(idx);
            if (status != null)
            {
                // On entry the generic reader may not announce the row — say the
                // status right away; on navigation defer it behind the row name.
                _pendingStatus = status;
                _pendingStatusFrame = first ? _frame : _frame + STATUS_DELAY_FRAMES;
            }
        }

        if (_pendingStatus != null && _frame >= _pendingStatusFrame)
        {
            string status = _pendingStatus;
            _pendingStatus = null;
            API.LogInfo($"[SF6Access] Combo trial [{_lastIndex}]: {status}");
            ScreenReaderService.Speak(status, interrupt: false);
        }
    }

    /// <summary>Clear flag of the trial at the given list index, worded for
    /// announcement — null when the data can't be resolved.</summary>
    private string ReadClearStatus(int index)
    {
        try
        {
            var infoList = FlowHelper.GetObjectField(Param, "CurrentItemDataInfoList");
            var info = FlowHelper.GetListItem(infoList, index);
            var trial = FlowHelper.GetObjectField(info, "BattleFGComboTrialData");
            int uniqueId = FlowHelper.ReadIntField(trial, "UniqueID", 0);
            if (uniqueId == 0) uniqueId = FlowHelper.CallInt(trial, "get_UniqueID", 0);
            if (uniqueId <= 0) return null;

            var saveMgr = API.GetManagedSingleton("app.SystemSaveManager") as ManagedObject;
            var data = FlowHelper.GetObjectField(saveMgr, "Data")
                ?? FlowHelper.Call(saveMgr, "get_Data") as ManagedObject;
            var trialSave = FlowHelper.GetObjectField(data, "ComboTrialSaveData")
                ?? FlowHelper.Call(data, "get_ComboTrialSaveData") as ManagedObject;
            if (trialSave == null) return null;

            var result = FlowHelper.Call(trialSave, "IsClear", (uint)uniqueId);
            if (result == null) return null;
            bool clear = System.Convert.ToBoolean(result);
            return ClearWord(clear);
        }
        catch { return null; }
    }

    // The on-screen check mark is a texture — no game text to reuse.
    private static string ClearWord(bool clear) => LocalizedText.Completed(clear);
}
