using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

namespace SF6Access.Hooks.WorldTour;

/// <summary>Announces stable interaction targets; N or Start reads nearby people on request.</summary>
public class FieldAwarenessHooks
{
    private const int TargetStablePolls = 3;
    private const int MaxNearbySpoken = 8;
    private const long PollMs = 250;
    private const int VkN = 0x4E;
    private static readonly ReadoutShortcut NearbyKey = new(VkN, ReadoutShortcut.PAD_START);
    private static string _pendingTarget, _announcedTarget;
    private static int _pendingPolls;
    private static long _nextPoll;
    private readonly record struct Interactable(string Name, int ContactType, float Distance, bool IncludesKind);

    [PluginEntryPoint]
    public static void Initialize() => API.LogInfo("[SF6Access] FieldAwarenessHooks initialized (interaction changes and N / Start readout)");

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void Tick()
    {
        bool wantsNearby = NearbyKey.Pressed();
        FieldPresenceService.Refresh();
        if (!FieldFeedback.CanOutput)
        {
            _pendingTarget = _announcedTarget = null;
            _pendingPolls = 0;
            FieldFeedback.Forget("contact");
            FieldFeedback.Forget("contact-left");
            return;
        }
        if (wantsNearby) AnnounceNearby();
        long now = System.Environment.TickCount64;
        if (now < _nextPoll) return;
        _nextPoll = now + PollMs;
        AnnounceCurrentTargetChange();
    }

    private static void AnnounceCurrentTargetChange()
    {
        string description = NearestDescription();
        if (string.IsNullOrEmpty(description))
        {
            FieldFeedback.Forget("contact");
            _pendingTarget = null;
            _pendingPolls = 0;
            if (_announcedTarget != null && FieldFeedback.CanExplore)
                FieldFeedback.Say("contact-left", LocalizedText.LeavingTarget(_announcedTarget),
                    FieldSpeechPriority.Ambient, stillValid: () => _pendingTarget == null);
            _announcedTarget = null;
            return;
        }
        FieldFeedback.Forget("contact-left");
        string spoken = ExplorationText.Interact(description);
        if (description != _pendingTarget)
        {
            FieldFeedback.Forget("contact");
            _pendingTarget = description;
            _pendingPolls = 1;
            return;
        }
        if (_pendingPolls < TargetStablePolls) { _pendingPolls++; return; }
        if (description == _announcedTarget) return;
        FieldFeedback.Say("contact", spoken, FieldSpeechPriority.Interaction,
            onSpoken: () => _announcedTarget = description, stillValid: () => _pendingTarget == description);
    }

    /// <summary>The nearest actual contact for the Z readout; proximity to a landmark never counts.</summary>
    internal static string CurrentInteraction()
    {
        if (!FieldFeedback.CanOutput) return null;
        string description = NearestDescription();
        return string.IsNullOrEmpty(description) ? null : ExplorationText.Interact(description);
    }

    private static string NearestDescription()
    {
        var list = ReadInteractables();
        if (list.Count == 0) return null;
        var nearest = list[0];
        foreach (var item in list)
            if (item.Distance < nearest.Distance) nearest = item;
        return Describe(nearest);
    }

    private static void AnnounceNearby()
    {
        var list = ReadInteractables();
        if (list.Count == 0)
        {
            if (!AnnounceNearbyFromAvatarList()) ScreenReaderService.Speak(LocalizedText.NothingNearby());
            return;
        }
        list.Sort((a, b) => a.Distance.CompareTo(b.Distance));
        var parts = new List<string>(list.Count);
        foreach (var item in list)
        {
            string description = Describe(item);
            if (!string.IsNullOrEmpty(description)) parts.Add(description);
        }
        if (parts.Count > 0)
            ScreenReaderService.Speak($"{LocalizedText.NearbyCount(parts.Count)}: {Join(parts)}");
    }

    private static List<Interactable> ReadInteractables()
    {
        var result = new List<Interactable>();
        var mgr = WorldTourStateService.GetAvatarManager();
        if (mgr == null) return result;
        var list = AvatarFieldReader.GetAccessInfoList(mgr);
        int count = FlowHelper.GetListCount(list);
        for (int i = 0; i < count; i++)
        {
            var access = FlowHelper.GetListItem(list, i);
            var info = AvatarFieldReader.GetProp(access, "TargetInfo");
            var target = AvatarFieldReader.GetProp(info, "Target");
            string name = FlowHelper.CleanTags(FlowHelper.Call(target, "GetDispName") as string)?.Trim();
            bool includesKind = RestContact.TryDescribe(target, name, out string contactLabel)
                || FastTravelContact.TryDescribe(target, name, out contactLabel);
            if (includesKind) name = contactLabel;
            else if (string.IsNullOrEmpty(name) && target != null)
            {
                var player = FieldPresenceService.PlayerPos;
                foreach (var entry in FieldAccessCatalog.Nearby(player.X, player.Y, player.Z, System.Environment.TickCount64))
                    if (entry.Target == target.GetAddress()) { name = entry.Text; break; }
            }
            if (string.IsNullOrEmpty(name)) continue;
            int contactType = target != null ? AvatarFieldReader.ReadContactType(target) : -1;
            float distance = FlowHelper.ReadFloatField(info, "Distance", 0f);
            result.Add(new(name, contactType, distance, includesKind));
        }
        return result;
    }

    private static bool AnnounceNearbyFromAvatarList()
    {
        var others = AvatarFieldReader.ReadOthers(WorldTourStateService.GetAvatarManager());
        if (others.Count == 0) return false;
        var forward = FieldDirectionService.GetCameraForward();
        var parts = new List<string>(others.Count);
        foreach (var other in others)
        {
            int meters = (int)System.Math.Round(other.Dist);
            int hour = FieldDirectionService.ClockHour(forward, other.Dx, other.Dz);
            string what = AvatarFieldReader.DescribeAvatar(other.Avatar) ?? LocalizedText.ContactPerson();
            parts.Add(hour > 0 ? LocalizedText.AtClockMeters(what, hour, meters) : LocalizedText.AtMeters(what, meters));
        }
        ScreenReaderService.Speak($"{LocalizedText.NearbyCount(parts.Count)}: {Join(parts)}");
        return true;
    }

    private static string Join(List<string> parts)
    {
        if (parts.Count <= MaxNearbySpoken) return string.Join(", ", parts);
        return $"{string.Join(", ", parts.GetRange(0, MaxNearbySpoken))}, {LocalizedText.AndMore(parts.Count - MaxNearbySpoken)}";
    }

    private static string Describe(Interactable item)
    {
        string kind = item.IncludesKind ? null : AvatarFieldReader.KindWord(item.ContactType);
        return string.IsNullOrEmpty(kind) ? item.Name : $"{item.Name}, {kind}";
    }
}
