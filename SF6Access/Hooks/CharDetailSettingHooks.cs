using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the generic character setting screens used by combo
/// trials, tutorials and other modes (training has its own FighterSettingHooks):
/// - app.UIFlowGenericCharacterSetting.Param: character grid (mFighterSelectSimple)
/// - app.UIFlowCharacterDetailSetting.Param: costume / color / control type /
///   preset overlay (MatchingFighterSetting widget — mGroup focus + mArrSpin rows,
///   each row's label and value are visible texts under the spin's control)
///
/// ScreenAdapter (multi-Param): the detail overlay sits on top of the grid and
/// is preferred while open. Registered in ScreenRegistry.
/// </summary>
public sealed class CharDetailSettingHooks : ScreenAdapter
{
    private const string GRID_TYPE = "app.UIFlowGenericCharacterSetting.Param";
    private const string DETAIL_TYPE = "app.UIFlowCharacterDetailSetting.Param";
    private static readonly string[] Types = { GRID_TYPE, DETAIL_TYPE };

    public override string[] OwnedTypes => Types;

    public CharDetailSettingHooks()
    {
        SearchInterval = 60;
        ReadInterval = 5;
    }

    private ManagedObject _gridParam;
    private ManagedObject _detailParam;

    private string _lastFighterName;
    private int _lastDetailFocus = -2;
    private string _lastDetailRowText;

    protected override bool Locate()
    {
        var found = FlowHelper.FindFlowParams(Types);
        found.TryGetValue(GRID_TYPE, out _gridParam);
        found.TryGetValue(DETAIL_TYPE, out _detailParam);
        return _gridParam != null || _detailParam != null;
    }

    protected override void OnActivate()
    {
        _lastFighterName = null;
        _lastDetailFocus = -2;
        _lastDetailRowText = null;
        API.LogInfo($"[SF6Access] CharDetailSetting active (grid={_gridParam != null}, detail={_detailParam != null})");
    }

    protected override void OnDeactivate()
    {
        _gridParam = null;
        _detailParam = null;
        API.LogInfo("[SF6Access] CharDetailSetting ended");
    }

    protected override void OnPoll()
    {
        // The detail overlay sits on top of the grid: prefer it while open
        if (_detailParam != null && PollDetailSetting()) return;
        PollFighterGrid();
    }

    /// <summary>Costume/color/control/preset rows of the detail overlay.</summary>
    private bool PollDetailSetting()
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
        Speak(announcement);
        return true;
    }

    /// <summary>Character grid: announce the focused fighter's name.</summary>
    private void PollFighterGrid()
    {
        if (_gridParam == null) return;

        var grid = FlowHelper.GetObjectField(_gridParam, "mFighterSelectSimple");
        string name = FlowHelper.ReadSelectedItemText(grid);
        if (string.IsNullOrEmpty(name) || name == _lastFighterName) return;

        bool first = _lastFighterName == null;
        _lastFighterName = name;
        if (first) return;

        API.LogInfo($"[SF6Access] CharDetail fighter: {name}");
        Speak(name);
    }
}
