using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// The phone's Messages app — contacts down one side, that contact's
/// conversations down the other
/// (<c>app.worldtour.UIFlowWTDeviceIM.DeviceIMParam</c>).
///
/// <para><b>Read the item arrays, not the scroll list.</b> <c>HolderListParts</c>
/// and <c>SubjectListParts</c> are <c>UIPartsScrollList</c>, which carries no
/// <c>_Children</c> to walk. The rows are reachable instead as
/// <c>HolderPartsArray</c> / <c>SubjectPartsArray</c>, whose <c>ItemText</c> is a
/// plain field-backed <c>via.gui.Text</c> — read with <c>get_Message</c>, not an
/// interface getter.</para>
///
/// <para>Which pane has focus is not announced separately: the selected contact
/// and the selected conversation each announce themselves when they change, and
/// moving between panes changes one of them.</para>
/// </summary>
public sealed class DeviceIMHooks : SingleParamScreenAdapter
{
    protected override string ParamType => "app.worldtour.UIFlowWTDeviceIM.DeviceIMParam";

    // "No contact selected". The game's own not-set value for these id fields is
    // uint.MaxValue, not 0 — the same sentinel that caught us out in the sound
    // system's language-id fields.
    private const uint NO_HOLDER = uint.MaxValue;

    private uint _lastHolder = NO_HOLDER;
    private int _lastSubject = int.MinValue;
    private string _lastSpoken;

    protected override void OnBind()
    {
        _lastHolder = NO_HOLDER;
        _lastSubject = int.MinValue;
        _lastSpoken = null;
        API.LogInfo("[SF6Access] Messages app active");
    }

    protected override void Poll()
    {
        // Contact first: changing contact re-lays the conversation list, so
        // announcing the new contact before its threads keeps the order sane.
        // The contact id is a uint and stays one: narrowing it to int is how the
        // "not set" sentinel (uint.MaxValue) turns into a wrong number.
        uint holder = FlowHelper.ReadUIntField(Param, "SelectedHolderID", NO_HOLDER);
        if (holder != _lastHolder && holder != NO_HOLDER)
        {
            bool first = _lastHolder == NO_HOLDER;
            _lastHolder = holder;
            _lastSubject = int.MinValue;   // the thread list belongs to the old contact
            if (!first)
            {
                string name = SelectedText("HolderPartsArray");
                if (!string.IsNullOrEmpty(name)) { Say(name); return; }
            }
        }

        int subject = FlowHelper.ReadIntField(Param, "SelectedSubjectIndex", int.MinValue);
        if (subject == _lastSubject) return;
        bool firstSubject = _lastSubject == int.MinValue;
        _lastSubject = subject;
        if (firstSubject && _lastSpoken != null) return;

        string title = TextAt("SubjectPartsArray", subject);
        if (!string.IsNullOrEmpty(title)) Say(title);
    }

    private void Say(string text)
    {
        if (text == _lastSpoken) return;
        _lastSpoken = text;
        Speak(text);
    }

    /// <summary>Text of whichever row in an item array reports itself selected.
    /// Used for the contact list, whose selection is an id rather than an index —
    /// so the row cannot simply be indexed.</summary>
    private string SelectedText(string arrayField)
    {
        var array = FlowHelper.GetObjectField(Param, arrayField);
        int n = FlowHelper.GetListCount(array);
        for (int i = 0; i < n; i++)
        {
            var item = FlowHelper.GetListItem(array, i);
            var ctrl = FlowHelper.GetObjectField(item, "ItemCtrl");
            if (ctrl != null && FlowHelper.Call(ctrl, "get_IsSelect") is bool sel && !sel) continue;
            string text = ItemText(item);
            if (!string.IsNullOrEmpty(text)) return text;
        }
        return null;
    }

    private string TextAt(string arrayField, int index)
    {
        if (index < 0) return null;
        var array = FlowHelper.GetObjectField(Param, arrayField);
        if (index >= FlowHelper.GetListCount(array)) return null;
        return ItemText(FlowHelper.GetListItem(array, index));
    }

    private static string ItemText(ManagedObject item)
    {
        var text = FlowHelper.GetObjectField(item, "ItemText");
        return FlowHelper.CleanTags(FlowHelper.Call(text, "get_Message") as string)?.Trim();
    }
}
