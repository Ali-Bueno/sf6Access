using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Options sub-screens opened from the audio tab:
/// - app.UIFlowCharacterVoiceLanguage.Param: per-character voice selection
///   (UIPartsFighterSelectSimple grid, mSelectIndex + selected tile text)
/// - app.UIFlowCharaBgmSetting.UIParam: character music settings — character
///   list (FocusCharaId) + assigned BGM grid
/// - app.UIFlowCharaBgmSelect.UIParam: BGM track picker (mBgmList grid whose
///   rows resolve to mDispBgmDataList[i].TitleMessage.GUID)
///
/// Built on the ScreenAdapter foundation (multi-Param variant): the base owns the
/// poll lifecycle, this class resolves whichever of the three sub-screens is open
/// and reads its focused row via ChangeGate. Registered in ScreenRegistry.
/// </summary>
public sealed class OptionSubScreenHooks : ScreenAdapter
{
    private const string VOICE_PARAM = "app.UIFlowCharacterVoiceLanguage.Param";
    private const string BGM_SETTING_PARAM = "app.UIFlowCharaBgmSetting.UIParam";
    private const string BGM_SELECT_PARAM = "app.UIFlowCharaBgmSelect.UIParam";
    private static readonly string[] Types = { VOICE_PARAM, BGM_SETTING_PARAM, BGM_SELECT_PARAM };

    public override string[] OwnedTypes => Types;

    public OptionSubScreenHooks()
    {
        SearchInterval = 30;
        ReadInterval = 5;
    }

    private ManagedObject _voiceParam;
    private ManagedObject _bgmSettingParam;
    private ManagedObject _bgmSelectParam;

    private readonly ChangeGate _voice = new();
    private readonly ChangeGate _bgmSetting = new();
    private int _lastBgmSettingChara = -2;
    private int _lastBgmSelectRow = -2;

    protected override bool Locate()
    {
        var found = FlowHelper.FindFlowParams(Types);
        found.TryGetValue(VOICE_PARAM, out _voiceParam);
        found.TryGetValue(BGM_SETTING_PARAM, out _bgmSettingParam);
        found.TryGetValue(BGM_SELECT_PARAM, out _bgmSelectParam);
        return _voiceParam != null || _bgmSettingParam != null || _bgmSelectParam != null;
    }

    protected override void OnActivate()
    {
        ResetState();
        API.LogInfo($"[SF6Access] Option sub-screen active (voice={_voiceParam != null}, " +
            $"bgmSetting={_bgmSettingParam != null}, bgmSelect={_bgmSelectParam != null})");
    }

    protected override void OnDeactivate()
    {
        _voiceParam = null;
        _bgmSettingParam = null;
        _bgmSelectParam = null;
        ResetState();
        API.LogInfo("[SF6Access] Option sub-screen ended");
    }

    protected override void OnPoll()
    {
        // The track picker overlays the BGM settings screen: prefer it while open.
        if (_bgmSelectParam != null) { PollBgmSelect(); return; }
        if (_bgmSettingParam != null) { PollBgmSetting(); return; }
        if (_voiceParam != null) PollVoiceScreen();
    }

    /// <summary>Character grid of the voice selection screen.</summary>
    private void PollVoiceScreen()
    {
        var grid = FlowHelper.GetObjectField(_voiceParam, "FighterSelectSimple");
        if (grid == null) return;

        int idx = FlowHelper.ReadIntField(grid, "mSelectIndex", int.MinValue);
        if (idx == int.MinValue) return;

        // The tile text holds the fighter name and its current voice value.
        string text = FlowHelper.ReadSelectedItemText(grid)
            ?? FlowHelper.ReadSelectedItemText(FlowHelper.GetObjectField(grid, "mScrollGrid"));

        string announcement = _voice.Evaluate(idx, text);
        if (announcement == null) return;
        API.LogInfo($"[SF6Access] Voice screen [{idx}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>Character music settings: character list + assigned BGM grid.</summary>
    private void PollBgmSetting()
    {
        // Focused character (data field, not UI) — resolve the localized name.
        int charaId = FlowHelper.ReadIntField(_bgmSettingParam, "FocusCharaId", int.MinValue);
        if (charaId != int.MinValue && charaId != _lastBgmSettingChara)
        {
            bool first = _lastBgmSettingChara == -2;
            _lastBgmSettingChara = charaId;
            if (!first)
            {
                string name = FlowHelper.ResolveFighterName(charaId);
                if (!string.IsNullOrEmpty(name))
                {
                    API.LogInfo($"[SF6Access] BGM setting chara: {name} (id={charaId})");
                    ScreenReaderService.Speak(name);
                    return;
                }
            }
        }

        // Assigned-track grid on the right side.
        var bgmList = FlowHelper.GetObjectField(_bgmSettingParam, "mBgmList");
        if (bgmList == null) return;

        int row = FlowHelper.CallInt(bgmList, "get_SelectedIndex");
        if (row < 0) return;

        string text = FlowHelper.ReadSelectedItemText(bgmList);
        string announcement = _bgmSetting.Evaluate(row, text);
        if (announcement == null) return;
        API.LogInfo($"[SF6Access] BGM setting row [{row}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>BGM track picker grid.</summary>
    private void PollBgmSelect()
    {
        var bgmList = FlowHelper.GetObjectField(_bgmSelectParam, "mBgmList");
        if (bgmList == null) return;

        int row = FlowHelper.CallInt(bgmList, "get_SelectedIndex");
        if (row < 0 || row == _lastBgmSelectRow) return;

        bool first = _lastBgmSelectRow == -2;
        _lastBgmSelectRow = row;
        if (first) return;

        // Track title from the data record (localized Guid), grid text as fallback.
        string title = ReadBgmTitle(row) ?? FlowHelper.ReadSelectedItemText(bgmList);
        if (string.IsNullOrEmpty(title)) return;

        API.LogInfo($"[SF6Access] BGM select [{row}]: {title}");
        ScreenReaderService.Speak(title);
    }

    private string ReadBgmTitle(int row)
    {
        try
        {
            var list = FlowHelper.GetObjectField(_bgmSelectParam, "mDispBgmDataList");
            var record = FlowHelper.GetListItem(list, row);
            var titleMsg = FlowHelper.GetObjectField(record, "TitleMessage");
            return FlowHelper.ResolveGuidField(titleMsg, "GUID");
        }
        catch { return null; }
    }

    private void ResetState()
    {
        _voice.Reset();
        _bgmSetting.Reset();
        _lastBgmSettingChara = -2;
        _lastBgmSelectRow = -2;
    }
}
