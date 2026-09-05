using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// Makes a World Tour NPC produce one of its OWN sounds, at its own position.
///
/// <para>The point is that the sound belongs to the game: an NPC's footstep or
/// its "hey!" reads as the city being alive, where a synthetic beep reads as a
/// mod talking over it. Positioning is free — the sound plays through the NPC's
/// own emitter, so the engine gives real 3D panning, distance attenuation and
/// occlusion, and it obeys the player's own volume sliders.</para>
///
/// <para>Runtime-confirmed 2026-08-03 — see <c>docs/sf6-architecture.md</c>
/// § Game audio (Wwise) for the full reference (emitter type, bank map, the
/// sentinel in the language-id fields, and the stop APIs).</para>
/// </summary>
public static class NpcBeaconService
{
    // The emitter is matched by INHERITANCE: a WT NPC carries
    // app.sound.SoundDynamicContainerApp, and "SoundContainer" is not a
    // substring of "SoundDynamicContainerApp", so a name test misses it. It also
    // correctly rejects app.sound.SoundRequestReferenceTableContainer, which
    // merely ends in "Container" and has no trigger().
    private const string SOUND_CONTAINER = "soundlib.SoundContainer";
    private const string TRIGGER_BY_ID = "trigger(System.UInt32)";

    // Soundbank holding an NPC's small idle noises. A SHARED bank (measured at
    // 53 triggers on two different NPCs), unlike the voice banks whose names
    // carry the actor id — so this one can safely be matched by name.
    private const string BEACON_BANK = "foot_steps_es";
    // Identified by ear in game: the first entries of that bank are the noises
    // an NPC makes while standing still; later ones are walking steps.
    private const int IDLE_FIRST = 0;
    private const int IDLE_COUNT = 5;

    // Voice lines are matched by the game's own IsLanguage flag, never by name.
    // The tutorial bank is excluded: those lines are instructional, not ambient.
    private const string TUTORIAL_MARK = "tutorial";
    private const int VOICE_FIRST = 0;
    // Covers the short interjections ("hey!", "hum", "ha?") the tester picked
    // out, which sat past the first handful of entries.
    private const int VOICE_COUNT = 16;

    private static Method _trigger;
    private static readonly System.Random Rng = new System.Random();

    /// <summary>
    /// Sound one NPC. <paramref name="allowVoice"/> lets the caller keep quiet
    /// where a spoken line would collide with the game's own dialogue — an NPC
    /// with no voice bank (most filler NPCs) always falls back to its noises, so
    /// nobody is ever unlocatable. Returns false when the NPC could not be
    /// sounded at all.
    /// </summary>
    public static bool Ping(ManagedObject avatar, bool allowVoice)
        => PingObject(FlowHelper.Call(avatar, "get_GameObject") as ManagedObject, allowVoice);

    /// <summary>Same, for anything that is not an avatar — a mission objective
    /// may be a prop or a place rather than a person. Returns false when the
    /// object carries no emitter of its own, which is the caller's cue to fall
    /// back to speech.</summary>
    public static bool PingObject(ManagedObject go, bool allowVoice, bool sameEveryTime = false)
    {
        var container = ContainerOn(go);
        if (container == null) return Fail("no sound container on the NPC");

        var ids = allowVoice ? PickTriggers(container, true) : new List<uint>();
        if (ids.Count == 0) ids = PickTriggers(container, false);
        if (ids.Count == 0) return Fail($"no trigger ids; banks present: {BankList(container)}");

        _trigger ??= TDB.Get().FindType(SOUND_CONTAINER)?.GetMethod(TRIGGER_BY_ID);
        if (_trigger == null) return Fail($"{SOUND_CONTAINER}.{TRIGGER_BY_ID} not found");

        try
        {
            // trigger() has three 1-argument overloads, so the method is resolved
            // by full signature rather than dispatched by name.
            //
            // Ambient pings vary, so the city does not sound mechanical. A BEACON
            // does the opposite: it has to be recognisable as the same cue every
            // time, or it is one more sound to interpret rather than a signal.
            uint id = sameEveryTime ? ids[0] : ids[Rng.Next(ids.Count)];

            // WHAT was fired, once per distinct emitter type. "played" only means
            // the call did not throw; it says nothing about whether a human heard
            // anything, and that gap has already cost two test rounds on the
            // mission beacon. Naming the container and the bank makes an
            // inaudible cue diagnosable from the log instead of by ear.
            string containerType = container.GetTypeDefinition()?.GetFullName() ?? "?";
            if (!Reported.Contains(containerType))
            {
                Reported.Add(containerType);
                API.LogInfo($"[SF6Access] Beacon emitter {containerType}: {ids.Count} ids " +
                            $"(voice={allowVoice}), banks: {BankList(container)}, firing id={id}");
            }

            _trigger.InvokeBoxed(typeof(object), container, new object[] { id });
            return true;
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] Beacon trigger failed: {ex.Message}");
            return false;
        }
    }

    /// <summary>Trigger ids from one bank of this NPC's container: its spoken
    /// lines, or its idle noises. Read from the group's own TriggerInfoList so
    /// the positions are the game's real ones.</summary>
    private static List<uint> PickTriggers(ManagedObject container, bool voice)
    {
        var result = new List<uint>();
        var lists = AvatarFieldReader.GetProp(container, "AllTriggerInfoListData");
        int outer = FlowHelper.GetListCount(lists);

        for (int i = 0; i < outer; i++)
        {
            var group = FlowHelper.GetListItem(lists, i);
            string bank = BankName(group);
            bool match = voice
                ? IsLanguageGroup(group) && bank.IndexOf(TUTORIAL_MARK, System.StringComparison.OrdinalIgnoreCase) < 0
                : bank == BEACON_BANK;
            if (!match) continue;

            var infos = AvatarFieldReader.GetProp(group, "TriggerInfoList");
            int inner = FlowHelper.GetListCount(infos);
            int first = voice ? VOICE_FIRST : IDLE_FIRST;
            int count = voice ? VOICE_COUNT : IDLE_COUNT;
            for (int j = first; j < first + count && j < inner; j++)
            {
                // ReadUIntField, NOT ReadIntField: trigger ids are hashes and
                // routinely exceed int.MaxValue, where the int path throws
                // inside Convert.ToInt32 and silently yields the fallback.
                uint id = FlowHelper.ReadUIntField(FlowHelper.GetListItem(infos, j), "TriggerId");
                if (id != 0 && !result.Contains(id)) result.Add(id);
            }
            if (result.Count > 0) return result;
        }
        return result;
    }

    // One-shot failure reporting. A beacon that does not sound is otherwise
    // indistinguishable from one the player did not notice, and guessing between
    // those two cost a full test round.
    private static readonly List<string> Reported = new List<string>();

    private static bool Fail(string why)
    {
        if (!Reported.Contains(why))
        {
            Reported.Add(why);
            API.LogWarning($"[SF6Access] Beacon cannot play: {why}");
        }
        return false;
    }

    /// <summary>Bank names on a container, for the failure message — the bank is
    /// how a beacon sound is selected, so its absence is the answer.</summary>
    private static string BankList(ManagedObject container)
    {
        var lists = AvatarFieldReader.GetProp(container, "AllTriggerInfoListData");
        int n = FlowHelper.GetListCount(lists);
        if (n == 0) return "(AllTriggerInfoListData unreadable)";
        var names = new List<string>();
        for (int i = 0; i < n; i++)
        {
            var group = FlowHelper.GetListItem(lists, i);
            names.Add($"{BankName(group)}[{FlowHelper.GetListCount(AvatarFieldReader.GetProp(group, "TriggerInfoList"))}]");
        }
        return string.Join(", ", names);
    }

    /// <summary>The game's own "this bank is voice data" flag on a
    /// <c>soundlib.SoundTriggerInfoListData</c>.</summary>
    private static bool IsLanguageGroup(ManagedObject group)
    {
        if (group == null) return false;
        if (FlowHelper.ReadBoolField(group, "IsLanguage")) return true;
        return FlowHelper.Call(group, "get_IsLanguage") is bool b && b;
    }

    /// <summary>Short name of a group's soundbank, from the inherited
    /// <c>via.ResourceHolder.ResourcePath</c> — the only human-readable label
    /// these hashed trigger ids have.</summary>
    private static string BankName(ManagedObject group)
    {
        try
        {
            var bank = AvatarFieldReader.GetProp(group, "Bank");
            string path = FlowHelper.Call(bank, "get_ResourcePath") as string;
            if (string.IsNullOrEmpty(path)) return "";
            int slash = path.LastIndexOfAny(new[] { '/', '\\' });
            if (slash >= 0) path = path.Substring(slash + 1);
            int dot = path.IndexOf('.');
            return dot > 0 ? path.Substring(0, dot) : path;
        }
        catch { return ""; }
    }

    /// <summary>The NPC's sound emitter: a sibling component on its GameObject,
    /// matched by inheritance (see the note on SOUND_CONTAINER).</summary>
    private static ManagedObject ContainerOn(ManagedObject go)
    {
        var comps = FlowHelper.Call(go, "get_Components") as ManagedObject;
        int n = FlowHelper.GetListCount(comps);
        for (int i = 0; i < n; i++)
        {
            var c = FlowHelper.GetListItem(comps, i);
            var td = c?.GetTypeDefinition();
            if (td == null) continue;
            try { if (td.IsDerivedFrom(SOUND_CONTAINER)) return c; } catch { }
        }
        return null;
    }
}
