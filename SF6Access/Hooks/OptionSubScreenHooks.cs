using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the options sub-screens that open from the audio tab:
/// - app.UIFlowCharacterVoiceLanguage.Param: per-character voice selection
///   (UIPartsFighterSelectSimple grid, mSelectIndex + selected tile text)
/// - app.UIFlowCharaBgmSetting.UIParam: character music settings — character
///   list (FocusCharaId) + assigned BGM grid
/// - app.UIFlowCharaBgmSelect.UIParam: BGM track picker (mBgmList grid whose
///   rows resolve to mDispBgmDataList[i].TitleMessage.GUID)
/// </summary>
public class OptionSubScreenHooks
{
    private const string VOICE_PARAM = "app.UIFlowCharacterVoiceLanguage.Param";
    private const string BGM_SETTING_PARAM = "app.UIFlowCharaBgmSetting.UIParam";
    private const string BGM_SELECT_PARAM = "app.UIFlowCharaBgmSelect.UIParam";
    private static readonly string[] WatchedTypes = { VOICE_PARAM, BGM_SETTING_PARAM, BGM_SELECT_PARAM };

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 30;
    private const int POLL_READ_INTERVAL = 5;

    private static bool _isActive;
    private static ManagedObject _voiceParam;
    private static ManagedObject _bgmSettingParam;
    private static ManagedObject _bgmSelectParam;

    // Voice screen state
    private static int _lastVoiceIndex = -2;
    private static string _lastVoiceText;

    // BGM setting screen state
    private static int _lastBgmSettingChara = -2;
    private static int _lastBgmSettingRow = -2;
    private static string _lastBgmSettingText;

    // BGM select screen state
    private static int _lastBgmSelectRow = -2;

    private static Method _getFighterNameMethod;

    [PluginEntryPoint]
    public static void Initialize()
    {
        _getFighterNameMethod = TDB.Get().FindType("app.IDScriptExtensions")
            ?.GetMethod("GetFighterNameText(app.CHARA_ID)");
        API.LogInfo("[SF6Access] OptionSubScreenHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var found = FlowHelper.FindFlowParams(WatchedTypes);
            found.TryGetValue(VOICE_PARAM, out _voiceParam);
            found.TryGetValue(BGM_SETTING_PARAM, out _bgmSettingParam);
            found.TryGetValue(BGM_SELECT_PARAM, out _bgmSelectParam);

            bool active = _voiceParam != null || _bgmSettingParam != null || _bgmSelectParam != null;
            if (active && !_isActive)
            {
                _isActive = true;
                ResetState();
                API.LogInfo($"[SF6Access] Option sub-screen active (voice={_voiceParam != null}, " +
                    $"bgmSetting={_bgmSettingParam != null}, bgmSelect={_bgmSelectParam != null})");
            }
            else if (!active && _isActive)
            {
                _isActive = false;
                ResetState();
                API.LogInfo("[SF6Access] Option sub-screen ended");
            }
        }

        if (!_isActive || _pollCounter % POLL_READ_INTERVAL != 0) return;

        // The track picker overlays the BGM settings screen: prefer it while open
        if (_bgmSelectParam != null) { PollBgmSelect(); return; }
        if (_bgmSettingParam != null) { PollBgmSetting(); return; }
        if (_voiceParam != null) PollVoiceScreen();
    }

    /// <summary>Character grid of the voice selection screen.</summary>
    private static void PollVoiceScreen()
    {
        var grid = FlowHelper.GetObjectField(_voiceParam, "FighterSelectSimple");
        if (grid == null) return;

        int idx = FlowHelper.ReadIntField(grid, "mSelectIndex", int.MinValue);
        if (idx == int.MinValue) return;

        // The tile text holds the fighter name and its current voice value
        string text = FlowHelper.ReadSelectedItemText(grid)
            ?? FlowHelper.ReadSelectedItemText(FlowHelper.GetObjectField(grid, "mScrollGrid"));

        bool first = _lastVoiceIndex == -2;
        bool indexChanged = idx != _lastVoiceIndex;
        bool textChanged = !string.IsNullOrEmpty(text) && text != _lastVoiceText;
        string previous = _lastVoiceText;

        _lastVoiceIndex = idx;
        if (!string.IsNullOrEmpty(text)) _lastVoiceText = text;

        if (first || string.IsNullOrEmpty(text)) return;
        if (!indexChanged && !textChanged) return;

        // Same tile, voice value flipped: announce only what changed
        string announcement = indexChanged ? text : FlowHelper.DiffSegments(previous, text);
        API.LogInfo($"[SF6Access] Voice screen [{idx}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>Character music settings: character list + assigned BGM grid.</summary>
    private static void PollBgmSetting()
    {
        // Focused character (data field, not UI) — resolve the localized name
        int charaId = FlowHelper.ReadIntField(_bgmSettingParam, "FocusCharaId", int.MinValue);
        if (charaId != int.MinValue && charaId != _lastBgmSettingChara)
        {
            bool first = _lastBgmSettingChara == -2;
            _lastBgmSettingChara = charaId;
            if (!first)
            {
                string name = ResolveFighterName(charaId);
                if (!string.IsNullOrEmpty(name))
                {
                    API.LogInfo($"[SF6Access] BGM setting chara: {name} (id={charaId})");
                    ScreenReaderService.Speak(name);
                    return;
                }
            }
        }

        // Assigned-track grid on the right side
        var bgmList = FlowHelper.GetObjectField(_bgmSettingParam, "mBgmList");
        if (bgmList == null) return;

        int row = FlowHelper.CallInt(bgmList, "get_SelectedIndex");
        if (row < 0) return;

        string text = FlowHelper.ReadSelectedItemText(bgmList);

        bool firstRow = _lastBgmSettingRow == -2;
        bool rowChanged = row != _lastBgmSettingRow;
        bool textChanged = !string.IsNullOrEmpty(text) && text != _lastBgmSettingText;
        string previousText = _lastBgmSettingText;

        _lastBgmSettingRow = row;
        if (!string.IsNullOrEmpty(text)) _lastBgmSettingText = text;

        if (firstRow || string.IsNullOrEmpty(text)) return;
        if (!rowChanged && !textChanged) return;

        string announcement = rowChanged ? text : FlowHelper.DiffSegments(previousText, text);
        API.LogInfo($"[SF6Access] BGM setting row [{row}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    /// <summary>BGM track picker grid.</summary>
    private static void PollBgmSelect()
    {
        var bgmList = FlowHelper.GetObjectField(_bgmSelectParam, "mBgmList");
        if (bgmList == null) return;

        int row = FlowHelper.CallInt(bgmList, "get_SelectedIndex");
        if (row < 0 || row == _lastBgmSelectRow) return;

        bool first = _lastBgmSelectRow == -2;
        _lastBgmSelectRow = row;
        if (first) return;

        // Track title from the data record (localized Guid), grid text as fallback
        string title = ReadBgmTitle(row) ?? FlowHelper.ReadSelectedItemText(bgmList);
        if (string.IsNullOrEmpty(title)) return;

        API.LogInfo($"[SF6Access] BGM select [{row}]: {title}");
        ScreenReaderService.Speak(title);
    }

    private static string ReadBgmTitle(int row)
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

    private static string ResolveFighterName(int charaId)
    {
        if (_getFighterNameMethod == null || charaId <= 0) return null;
        try
        {
            return _getFighterNameMethod.InvokeBoxed(
                typeof(string), null, new object[] { (byte)charaId }) as string;
        }
        catch { return null; }
    }

    private static void ResetState()
    {
        _lastVoiceIndex = -2;
        _lastVoiceText = null;
        _lastBgmSettingChara = -2;
        _lastBgmSettingRow = -2;
        _lastBgmSettingText = null;
        _lastBgmSelectRow = -2;
    }
}
