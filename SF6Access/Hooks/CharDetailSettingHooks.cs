using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the generic character setting screens used by combo
/// trials, tutorials and other modes (training has its own FighterSettingHooks):
/// - app.UIFlowGenericCharacterSetting.Param: character grid (mFighterSelectSimple)
/// - app.UIFlowCharacterDetailSetting.Param: costume / color / control type /
///   preset overlay (MatchingFighterSetting widget — mGroup focus + mArrSpin rows,
///   each row's label and value are visible texts under the spin's control)
/// </summary>
public class CharDetailSettingHooks
{
    private const string GRID_TYPE = "app.UIFlowGenericCharacterSetting.Param";
    private const string DETAIL_TYPE = "app.UIFlowCharacterDetailSetting.Param";
    private static readonly string[] WatchedTypes = { GRID_TYPE, DETAIL_TYPE };

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    private static bool _isActive;
    private static ManagedObject _gridParam;
    private static ManagedObject _detailParam;

    private static string _lastFighterName;
    private static int _lastDetailFocus = -2;
    private static string _lastDetailRowText;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] CharDetailSettingHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            var found = FlowHelper.FindFlowParams(WatchedTypes);
            found.TryGetValue(GRID_TYPE, out _gridParam);
            found.TryGetValue(DETAIL_TYPE, out _detailParam);

            bool active = _gridParam != null || _detailParam != null;
            if (active && !_isActive)
            {
                _isActive = true;
                _lastFighterName = null;
                _lastDetailFocus = -2;
                _lastDetailRowText = null;
                API.LogInfo($"[SF6Access] CharDetailSetting active (grid={_gridParam != null}, detail={_detailParam != null})");
            }
            else if (!active && _isActive)
            {
                _isActive = false;
                _gridParam = null;
                _detailParam = null;
                API.LogInfo("[SF6Access] CharDetailSetting ended");
            }
        }

        if (!_isActive || _pollCounter % POLL_READ_INTERVAL != 0) return;

        // The detail overlay sits on top of the grid: prefer it while open
        if (_detailParam != null && PollDetailSetting()) return;
        PollFighterGrid();
    }

    /// <summary>Costume/color/control/preset rows of the detail overlay.</summary>
    private static bool PollDetailSetting()
    {
        var widget = FlowHelper.GetObjectField(_detailParam, "MatchingFighterSetting");
        if (widget == null) return false;

        int focus = FlowHelper.CallInt(widget, "get_FocusListIndex");
        if (focus < 0)
        {
            var group = FlowHelper.GetObjectField(widget, "mGroup");
            focus = FlowHelper.ReadIntField(group, "_FocusIndex");
        }
        if (focus < 0) return false;

        var spins = FlowHelper.GetObjectField(widget, "mArrSpin");
        var spin = FlowHelper.GetListItem(spins, focus);
        var control = FlowHelper.GetObjectField(spin, "Control")
            ?? FlowHelper.Call(spin, "get_Control") as ManagedObject;

        string text = GuiTextReader.ReadControlTextJoined(control);
        if (string.IsNullOrEmpty(text)) return true; // overlay open but row unreadable

        string previous = _lastDetailRowText;
        bool focusChanged = focus != _lastDetailFocus;
        bool textChanged = text != previous;
        bool first = _lastDetailFocus == -2;

        _lastDetailFocus = focus;
        _lastDetailRowText = text;

        if (first || (!focusChanged && !textChanged)) return true;

        // Same row, value edited with left/right: announce only the new value
        string announcement = focusChanged ? text : FlowHelper.DiffSegments(previous, text);

        API.LogInfo($"[SF6Access] CharDetail [{focus}]: {announcement}");
        ScreenReaderService.Speak(announcement);
        return true;
    }

    /// <summary>Character grid: announce the focused fighter's name.</summary>
    private static void PollFighterGrid()
    {
        if (_gridParam == null) return;

        var grid = FlowHelper.GetObjectField(_gridParam, "mFighterSelectSimple");
        string name = FlowHelper.ReadSelectedItemText(grid);
        if (string.IsNullOrEmpty(name) || name == _lastFighterName) return;

        bool first = _lastFighterName == null;
        _lastFighterName = name;
        if (first) return;

        API.LogInfo($"[SF6Access] CharDetail fighter: {name}");
        ScreenReaderService.Speak(name);
    }
}
