using System;
using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>Whether the game offers a ladder right now: its access target is in the avatar's contact list
/// (<c>AvatarManager.CurrentAccessInfoList</c>), the list the game's own action prompt is built from.</summary>
internal static class LadderAccess
{
    private const long DiagnoseMs = 3000; // One contact line every few seconds while standing at a ladder.
    private static long _nextDiagnosis;

    internal static bool Offered(ulong access) => FieldActionReader.InContact(access);

    /// <summary>At a planned ladder: what the contact list holds, so a ladder the player could not take can be explained.</summary>
    internal static void Diagnose(ulong access, float bottomRungM, float px, float py, float pz)
    {
        long now = Environment.TickCount64;
        if (now < _nextDiagnosis) return;
        _nextDiagnosis = now + DiagnoseMs;
        var parts = new List<string>();
        try
        {
            var list = AvatarFieldReader.GetAccessInfoList(WorldTourStateService.GetAvatarManager());
            int count = FlowHelper.GetListCount(list);
            for (int i = 0; i < count; i++)
            {
                var item = FlowHelper.GetListItem(list, i);
                var target = TargetOf(item);
                float distance = FieldProbeService.ToFloat(
                    MemberAccess.ReadFieldsFirst(MemberAccess.ReadFieldsFirst(item, "TargetInfo"), "Distance", typeof(float)));
                string type = target?.GetTypeDefinition()?.GetFullName() ?? "?";
                bool mine = access != 0 && target?.GetAddress() == access;
                parts.Add(FormattableString.Invariant($"{type}{(mine ? " (this ladder)" : "")} at {distance:F2} m"));
            }
        }
        catch (Exception ex) { parts.Add("read failed: " + ex.GetType().Name); }
        string contacts = parts.Count == 0 ? "none" : string.Join("; ", parts);
        API.LogInfo(FormattableString.Invariant(
            $"[SF6Access] Ladder contact: player ({px:F1}, {py:F1}, {pz:F1}), access target {(access != 0 ? "known" : "unknown")}, lowest rung {bottomRungM:+0.0;-0.0} m; contacts: {contacts}"));
    }

    private static ManagedObject TargetOf(ManagedObject item)
        => MemberAccess.ReadFieldsFirst(MemberAccess.ReadFieldsFirst(item, "TargetInfo"), "Target") as ManagedObject;
}
