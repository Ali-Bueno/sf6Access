using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// The phone's Map app — the city map with its icons and the fast-travel list
/// (<c>app.UIFlowWTDeviceMap.MapParam</c>).
///
/// <para><b>Evidence.</b> Fields confirmed in the F8 auto-dump
/// <c>sf6access_autodump_154237.txt</c> (lines 770-965): <c>FlowState</c> /
/// <c>PreviousFlowState</c> (<c>eFlowState</c>), <c>OperationMode</c>,
/// <c>EnableFreeCursor</c>, <c>SelectedTravelPoint</c> (null in MapView),
/// <c>TravelPoint</c> (the list behind the fast-travel list),
/// <c>mTravelPointList</c> (<c>UIPartsScrollList</c>), <c>mIconList</c>,
/// <c>mGroupTop</c>, <c>mCtrlPinInfoPanel</c>, <c>mTextCityName</c> /
/// <c>mTextAreaName</c>, <c>GuideMessage</c>, <c>DispCityId</c> /
/// <c>DispSectionId</c>. The same dump's on-screen texts (lines 809-837) show the
/// map's own labels arrive ALREADY LOCALIZED — "Beat Square", "Chun-Li",
/// "Style Lab Beauty Salon", mission names — so an icon's <c>TitleText</c> is
/// spoken verbatim and only the icon's KIND needs a mod-supplied word
/// (<see cref="DeviceMapText"/>).</para>
///
/// <para><b>Where each announcement comes from.</b>
/// <list type="bullet">
/// <item>Entry: the two location texts + <c>GuideMessage</c> (a plain
/// already-localized <c>System.String</c> field, not a message Guid).</item>
/// <item>The icon under the free cursor: <c>MapParam.GetCursorSelectedItem()</c>
/// returns a <c>UIPartsMouseOperable</c>; when that is a
/// <c>UIPartsMapIconPanel</c> its <c>Info</c> (<c>UIMapWindowBase.IconInfo</c>)
/// carries <c>TitleText</c> and <c>Type</c> (<c>ICON_TYPE</c>). The panel's own
/// <c>mTextTitle</c> is the fallback when <c>TitleText</c> is empty; the panel has
/// no distance or section field, so neither is spoken.</item>
/// <item>The fast-travel list: <c>mTravelPointList.SelectedIndex</c> indexes the
/// <c>TravelPoint</c> record list, whose <c>PointNameID.GUID</c> resolves through
/// <see cref="FlowHelper.ResolveGuidField"/>. Only read in the
/// <c>TravelSelect</c> state — in MapView the list is hidden and its index is
/// stale.</item>
/// <item>The pin info panel: <c>mCtrlPinInfoPanel</c> is a plain
/// <c>via.gui.Control</c>, read with the GUI scraper (visible text only, so a
/// hidden panel yields nothing).</item>
/// </list></para>
///
/// <para><b>Enum widths.</b> <c>eFlowState</c> and <c>ICON_TYPE</c> are declared
/// without an underlying type in the generated stubs, and that generator DOES
/// emit <c>: byte</c> where it applies (1027 of the 5620 enums in the dump carry
/// an explicit base), so both are 4-byte ints and <c>ReadIntField</c> is the
/// width-correct reader. Values are never compared against literals: every enum
/// is turned back into its member NAME via <c>FlowHelper.ResolveEnumName</c>.</para>
///
/// <para><b><c>eTopGroupFocus</c> could not be located.</b> The enum
/// (<c>{ TravelList, IconList }</c>) exists, but no field of that type appears on
/// <c>MapParam</c>, on <c>Flow_MapView</c>/<c>Flow_TravelSelect</c>, or anywhere
/// else in the decompiled stubs — the dump shows no such field either. The
/// closest live signal is <c>mGroupTop._FocusIndex</c> (<c>UIPartsGroup</c>),
/// which is read null-safely and LOGGED ONCE per screen entry so a follow-up dump
/// can confirm whether its indices line up with the enum. Nothing is announced
/// from it: the travel list and the map cursor each announce their own changes,
/// so moving between them is already audible, and a wrong mapping would have
/// silenced one of the two.</para>
///
/// <para><b>Change detection is per-instance, not <c>GameStateTracker</c>.</b>
/// That tracker expires a remembered value after 2.5 s, which is right for
/// announce-on-entry screens but would make a per-tick poll repeat the current
/// icon every 2.5 s while the player holds still. The sibling phone readers
/// (<c>DeviceIMHooks</c>, <c>MissionListHooks</c>) use last-value fields for the
/// same reason; they reset in <c>OnBind</c>, which is also when the game
/// recreates the Param.</para>
/// </summary>
public sealed class DeviceMapHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.UIFlowWTDeviceMap.MapParam";

    // The map is a free-cursor screen: the icon under the cursor changes while a
    // stick is held, so read a little faster than the 5-frame default.
    public DeviceMapHooks() { ReadInterval = 4; }

    /// <summary>Concrete type of a map icon panel, as returned by
    /// <c>GetCursorSelectedItem()</c> (also covers <c>UIPartsMapIconPanelBase</c>
    /// derivatives).</summary>
    private const string ICON_PANEL_TYPE = "MapIconPanel";

    private const int UNKNOWN = int.MinValue;

    /// <summary>The free cursor's GUI control (the game's own spelling). Checked only when the
    /// type declares it, so a renamed field cannot silence the cursor.</summary>
    private const string FREE_CURSOR_FIELD = "mCtrlFreeeCursor";

    /// <summary>How many polls the entry announcement waits for the location
    /// texts to be filled in before giving up (~2 s at ReadInterval 4).</summary>
    private const int ENTRY_RETRY_POLLS = 30;

    private int _lastState;
    private string _lastIcon;
    private string _lastTravel;
    private string _lastPin;
    private int _lastGroupFocus;
    private bool _groupFocusLogged;
    private int _entryPolls;
    private bool _entryDone;
    private bool _cursorField;

    protected override void OnBind()
    {
        _lastState = ReadFlowState();
        _lastIcon = null;
        _lastTravel = null;
        _lastPin = null;
        _lastGroupFocus = UNKNOWN;
        _groupFocusLogged = false;
        _entryPolls = 0;
        _entryDone = false;
        _cursorField = Param?.GetTypeDefinition()?.GetField(FREE_CURSOR_FIELD) != null;

        AnnounceEntry();
        API.LogInfo("[SF6Access] World Tour map active");
    }

    protected override void Poll()
    {
        if (!_entryDone) AnnounceEntry();

        AnnounceFlowState();
        if (MapIconSnapshot.Due && !Closing) MapIconSnapshot.Capture(Param);
        AnnounceCursorIcon();
        AnnounceTravelPoint();
        AnnouncePinPanel();
        TrackTopGroupFocus();
    }

    // ---------------------------------------------------------------- entry

    /// <summary>Where you are and what the screen is for, once. The location
    /// texts can still be empty on the frame the Param appears, so this retries
    /// until they fill in (or the retry budget runs out and it settles for what
    /// it has).</summary>
    private void AnnounceEntry()
    {
        string location = DeviceMapText.Location(GuiText("mTextAreaName"), GuiText("mTextCityName"));
        bool timedOut = ++_entryPolls > ENTRY_RETRY_POLLS;
        if (string.IsNullOrEmpty(location) && !timedOut) return;

        _entryDone = true;

        string state = DeviceMapText.FlowState(_lastState);
        string guide = Flatten(FlowHelper.ReadStringField(Param, "GuideMessage"));

        string spoken = Join(state, location, guide);
        if (!string.IsNullOrEmpty(spoken)) Speak(spoken);
    }

    // ------------------------------------------------------------ flow state

    private void AnnounceFlowState()
    {
        int state = ReadFlowState();
        if (state == UNKNOWN || state == _lastState) return;
        _lastState = state;

        // Entering the fast-travel list re-lays it, so let the new selection
        // speak for itself instead of repeating the previous point's name.
        _lastTravel = null;

        string word = DeviceMapText.FlowState(state);
        if (!string.IsNullOrEmpty(word)) Speak(word);
    }

    private int ReadFlowState() => FlowHelper.ReadIntField(Param, "FlowState", UNKNOWN);

    // ----------------------------------------------------------- map cursor

    /// <summary>While the map closes its cursor control goes first, and asking the map for
    /// icons then threw a NullReferenceException inside the game 11 times in 0.7 s (2026-09-26).</summary>
    private bool Closing => _cursorField && FlowHelper.GetObjectField(Param, FREE_CURSOR_FIELD) == null;

    /// <summary>The icon the free cursor is over: its own (already localized)
    /// name plus the mod's word for its kind.</summary>
    private void AnnounceCursorIcon()
    {
        if (Closing) return;
        var item = FlowHelper.Call(Param, "GetCursorSelectedItem") as ManagedObject;
        if (item == null)
        {
            // Cursor moved off every icon — forget it, so coming back onto the
            // same icon announces it again.
            _lastIcon = null;
            return;
        }

        string typeName = null;
        try { typeName = item.GetTypeDefinition()?.GetFullName(); } catch { }
        if (typeName == null || !typeName.Contains(ICON_PANEL_TYPE)) return;

        var info = FlowHelper.GetObjectField(item, "Info");
        string title = Flatten(FlowHelper.ReadStringField(info, "TitleText"));
        if (string.IsNullOrEmpty(title))
            title = Flatten(FlowHelper.ReadGuiText(FlowHelper.GetObjectField(item, "mTextTitle")));
        if (string.IsNullOrEmpty(title)) return;

        string kind = DeviceMapText.IconType(FlowHelper.ReadIntField(info, "Type", UNKNOWN));
        string spoken = DeviceMapText.Icon(title, kind);

        // Whatever the cursor is on becomes the point the beacon guides to and the
        // camera can be turned toward. Every kind of thing on the map arrives
        // through this one branch — missions, shops, masters, NPCs, fast-travel
        // points and the player's own pins — so this is the only capture the
        // feature needs. Remembered on every poll, not only when the announcement
        // changes: the spoken line dedupes, the selection must not.
        MapSelectionMemory.RememberIcon(info, spoken);

        if (spoken == _lastIcon) return;
        _lastIcon = spoken;
        Speak(spoken);
    }

    // ----------------------------------------------------- fast-travel list

    /// <summary>The highlighted fast-travel destination, with its position in the
    /// list. Skipped outside the TravelSelect state: the list is hidden there and
    /// its index still holds the previous visit's row.</summary>
    private void AnnounceTravelPoint()
    {
        if (!DeviceMapText.IsTravelSelect(_lastState)) return;

        var list = FlowHelper.GetObjectField(Param, "TravelPoint");
        int count = FlowHelper.GetListCount(list);
        int index = SelectedTravelIndex();

        // Prefer the record the list index points at; SelectedTravelPoint is the
        // fallback (it is null until something is picked — dump line 803).
        var record = index >= 0 && index < count
            ? FlowHelper.GetListItem(list, index)
            : FlowHelper.GetObjectField(Param, "SelectedTravelPoint");

        string name = Flatten(FlowHelper.ResolveGuidField(
            FlowHelper.GetObjectField(record, "PointNameID"), "GUID"));
        if (string.IsNullOrEmpty(name)) return;

        string spoken = DeviceMapText.TravelPoint(name, index, count);
        if (spoken == _lastTravel) return;
        _lastTravel = spoken;
        Speak(spoken);
    }

    private int SelectedTravelIndex()
    {
        var scroll = FlowHelper.GetObjectField(Param, "mTravelPointList");
        if (scroll == null) return -1;

        int index = FlowHelper.ReadIntField(scroll, "SelectedIndex", UNKNOWN);
        // UIPartsScrollList exposes SelectedIndex as a property; if the concrete
        // type keeps no backing field for it, ask the getter.
        if (index == UNKNOWN) index = FlowHelper.CallInt(scroll, "get_SelectedIndex", -1);
        return index;
    }

    // -------------------------------------------------------- pin info panel

    /// <summary>Whatever the marker info panel is showing, when it is showing.</summary>
    private void AnnouncePinPanel()
    {
        var panel = FlowHelper.GetObjectField(Param, "mCtrlPinInfoPanel");
        if (panel == null) return;

        string text = Flatten(GuiTextReader.ReadControlTextJoined(panel));
        if (string.IsNullOrEmpty(text))
        {
            _lastPin = null;
            return;
        }
        if (text == _lastPin) return;
        _lastPin = text;
        Speak(text);
    }

    // ------------------------------------------------------- top group focus

    /// <summary>Diagnostic only — see the class remarks on <c>eTopGroupFocus</c>.
    /// Logged once per screen entry so the follow-up dump has the index without
    /// costing a log line per frame.</summary>
    private void TrackTopGroupFocus()
    {
        if (_groupFocusLogged) return;

        var group = FlowHelper.GetObjectField(Param, "mGroupTop");
        if (group == null) return;

        int focus = FlowHelper.ReadIntField(group, "_FocusIndex", UNKNOWN);
        if (focus == UNKNOWN || focus == _lastGroupFocus) return;

        _lastGroupFocus = focus;
        _groupFocusLogged = true;
        API.LogInfo($"[SF6Access] Map top group focus index = {focus} " +
                    "(eTopGroupFocus: 0 TravelList, 1 IconList — unconfirmed)");
    }

    // ------------------------------------------------------------- plumbing

    private string GuiText(string field) =>
        Flatten(FlowHelper.ReadGuiText(FlowHelper.GetObjectField(Param, field)));

    /// <summary>The screen reader stops at an embedded newline, and the guide
    /// message and area names are laid out over several visual rows.</summary>
    private static string Flatten(string text) =>
        string.IsNullOrEmpty(text)
            ? null
            : System.Text.RegularExpressions.Regex.Replace(text, @"\s+", " ").Trim();

    private static string Join(params string[] parts)
    {
        var kept = new System.Collections.Generic.List<string>();
        foreach (string p in parts)
            if (!string.IsNullOrEmpty(p)) kept.Add(p);
        return string.Join(". ", kept);
    }
}
