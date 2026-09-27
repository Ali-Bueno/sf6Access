using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>
/// The destination picker: hold R3 (or V) anywhere in the field to browse every known
/// place by category, nearest first, and choose one; the mission beacon, route guidance
/// and R3 aiming then lead there. Opened by <see cref="MapAimHooks"/>; controls in
/// <see cref="PickerInput"/>. Design: <c>docs/wt-orientation.md</c>.
/// </summary>
public class DestinationPickerHooks
{
    private static readonly DestinationPicker _picker = new();
    private static readonly PickerInput _input = new();
    private static DestinationCategory _lastCategory = DestinationCategory.Places;

    /// <summary>True while the picker owns the field's controls and the speech.</summary>
    public static bool IsOpen { get; private set; }

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] DestinationPickerHooks initialized (hold R3 / V = destinations)");
    }

    /// <summary>Builds the list from where the player stands and speaks the first entry.</summary>
    public static void Open()
    {
        if (IsOpen || PadGuideHooks.Active) return;
        var me = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (!me.ok)
        {
            ScreenReaderService.Speak(DestinationText.Unavailable(), interrupt: true);
            return;
        }

        _picker.Load(DestinationCatalog.Build(me.x, me.z), _lastCategory);
        if (_picker.IsEmpty)
        {
            ScreenReaderService.Speak(DestinationText.Empty(), interrupt: true);
            return;
        }

        IsOpen = true;
        _input.Reset();
        API.LogInfo($"[SF6Access] Destination picker opened on {_picker.Category}");
        string opened = DestinationText.Opened() + ". " + CategoryAndItem();
        if (!_hintGiven && !LearnedFromMap())
        {
            _hintGiven = true;
            opened += ". " + DestinationText.LearnHint();
        }
        Speak(opened);
    }

    private static bool _hintGiven;

    /// <summary>Whether any category the phone map (or the game's registry) feeds is present.</summary>
    private static bool LearnedFromMap() =>
        _picker.Has(DestinationCategory.Shops) || _picker.Has(DestinationCategory.Masters) ||
        _picker.Has(DestinationCategory.Missions) || _picker.Has(DestinationCategory.Jobs);

    // After closing, the field stays masked until the closing button is released, so the
    // game never sees that press (A would otherwise talk to whoever stands in front).
    private static bool _maskUntilReleased;

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        if (!IsOpen)
        {
            if (!_maskUntilReleased) return;
            _maskUntilReleased = _input.AnyDown();
            if (_maskUntilReleased) FieldInputMask.Hold();
            return;
        }

        FieldPresenceService.Refresh();
        // Dialogue and the pad tutorial take the mic and the Decide button; so do menus and fights.
        if (!FieldPresenceService.CanSpeak || SF6Access.Hooks.SpTalkNovelHooks.DialogueActive || PadGuideHooks.Active)
        {
            Close();
            return;
        }
        FieldInputMask.Hold();

        switch (_input.Poll())
        {
            case PickerInput.Command.Next: _picker.MoveItem(1); Speak(ItemLine()); break;
            case PickerInput.Command.Previous: _picker.MoveItem(-1); Speak(ItemLine()); break;
            case PickerInput.Command.NextCategory: _picker.MoveCategory(1); Speak(CategoryAndItem()); break;
            case PickerInput.Command.PreviousCategory: _picker.MoveCategory(-1); Speak(CategoryAndItem()); break;
            case PickerInput.Command.Choose: Choose(); break;
            case PickerInput.Command.Delete: Delete(); break;
            case PickerInput.Command.Cancel:
                Close();
                Speak(DestinationText.Cancelled());
                break;
        }
    }

    private static void Choose()
    {
        var d = _picker.Current;
        Close();
        var me = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());

        switch (d.Action)
        {
            case DestinationAction.SaveHere:
                string name = SavedPlaces.SaveHere(ZoneNameService.Current().Name);
                // The new spot is where the player stands: known, not something to pass by.
                PlaceCache.Reset();
                FieldPassingHooks.Prime();
                Speak(name == null ? DestinationText.NotSaved() : DestinationText.Saved(name));
                return;
            case DestinationAction.FollowMission:
                MapSelectionMemory.Reset("back to the story objective");
                Speak(DestinationText.FollowMission());
                return;
        }

        MapSelectionMemory.Remember(d.Label, d.X, d.Y, d.Z);
        string where = me.ok ? DestinationText.Where(me.x, me.y, me.z, d.X, d.Y, d.Z) : null;
        Speak(where == null ? LocalizedText.TrackerFollowing(d.Label) : DestinationText.Chosen(d.Label, where));
    }

    /// <summary>Deletes the highlighted saved spot; other entries are the game's, not ours.</summary>
    private static void Delete()
    {
        var d = _picker.Current;
        if (d.Category != DestinationCategory.Saved || d.Action != DestinationAction.Go) return;
        if (!SavedPlaces.Delete(WorldTourStateService.CityId, d.Name)) return;
        Close();
        Speak(DestinationText.Deleted(d.Name));
    }

    private static void Close()
    {
        if (!IsOpen) return;
        IsOpen = false;
        _maskUntilReleased = true;
        if (!_picker.IsEmpty) _lastCategory = _picker.Category;
    }

    private static string CategoryAndItem() =>
        DestinationText.CategoryLine(_picker.Category, _picker.Count) + ". " + ItemLine();

    private static string ItemLine()
    {
        var d = _picker.Current;
        string where = null;
        if (d.HasPosition)
        {
            var me = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
            if (me.ok) where = DestinationText.Where(me.x, me.y, me.z, d.X, d.Y, d.Z);
        }
        return DestinationText.Item(d, where, _picker.Index, _picker.Count);
    }

    private static void Speak(string text) => ScreenReaderService.Speak(text, interrupt: true);
}
