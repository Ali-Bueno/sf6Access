#nullable enable
using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>The destination picker's cursor: categories of places, each sorted nearest-first.</summary>
public sealed class DestinationPicker
{
    public readonly record struct Group(DestinationCategory Category, IReadOnlyList<Destination> Items);

    private List<Group> _groups = new();
    private readonly Dictionary<DestinationCategory, Destination> _remembered = new();
    private int _group, _item;

    public bool IsEmpty => _groups.Count == 0;

    public bool Has(DestinationCategory category) => _groups.Exists(g => g.Category == category);
    public DestinationCategory Category => _groups[_group].Category;
    public Destination Current => _groups[_group].Items[_item];
    public int Index => _item;
    public int Count => _groups[_group].Items.Count;

    /// <summary>Groups the entries by category (in enum order), drops entries that repeat
    /// one already kept within <paramref name="mergeM"/>, and sorts each group
    /// nearest-first with its position-less actions on top. Earlier entries win a merge,
    /// so callers list live sources before remembered ones.</summary>
    public static List<Group> Build(IEnumerable<Destination> entries, float px, float pz, float mergeM)
    {
        var byCategory = new SortedDictionary<DestinationCategory, List<Destination>>();
        foreach (var e in entries)
        {
            // A place whose position failed to read (exact origin) cannot be guided to.
            if (string.IsNullOrWhiteSpace(e.Name) || e.Action == DestinationAction.Go && !e.HasPosition) continue;
            if (!byCategory.TryGetValue(e.Category, out var list))
                byCategory[e.Category] = list = new List<Destination>();
            if (!IsRepeat(list, e, mergeM)) list.Add(e);
        }

        var groups = new List<Group>();
        foreach (var (category, list) in byCategory)
        {
            list.Sort((a, b) => SortKey(a, px, pz).CompareTo(SortKey(b, px, pz)));
            groups.Add(new Group(category, list));
        }
        return groups;
    }

    private static bool IsRepeat(List<Destination> kept, Destination e, float mergeM)
    {
        foreach (var k in kept)
            if (k.Name == e.Name && k.Action == e.Action &&
                (!k.Actor.IsValid || !e.Actor.IsValid || k.Actor.City == e.Actor.City && k.Actor.Owner == e.Actor.Owner) &&
                (!e.HasPosition || SamePosition(k, e, mergeM)))
                return true;
        return false;
    }

    private static bool SamePosition(Destination a, Destination b, float mergeM)
    {
        float dx = a.X - b.X, dy = a.Y - b.Y, dz = a.Z - b.Z;
        return dx * dx + dy * dy + dz * dz <= mergeM * mergeM;
    }

    private static float SortKey(Destination d, float px, float pz) =>
        d.Action == DestinationAction.FollowMission ? float.NegativeInfinity
            : d.HasPosition ? d.FlatDistanceTo(px, pz) : -1f;

    /// <summary>Starts on <paramref name="preferred"/> when it has entries, else on the first group.</summary>
    public void Load(List<Group> groups, DestinationCategory preferred)
    {
        RememberItem();
        _groups = groups ?? new List<Group>();
        _group = System.Math.Max(0, _groups.FindIndex(g => g.Category == preferred));
        RestoreItem();
    }

    public void MoveItem(int step)
    {
        if (IsEmpty) return;
        _item = Wrap(_item + step, Count);
    }

    public void MoveCategory(int step)
    {
        if (IsEmpty) return;
        RememberItem();
        _group = Wrap(_group + step, _groups.Count);
        RestoreItem();
    }

    /// <summary>City changes discard cursor memories whose positions belong to the previous map.</summary>
    public void Reset()
    {
        _groups = new List<Group>();
        _remembered.Clear();
        _group = _item = 0;
    }

    private void RememberItem()
    {
        if (!IsEmpty) _remembered[Category] = Current;
    }

    private void RestoreItem()
    {
        _item = 0;
        if (IsEmpty || !_remembered.TryGetValue(Category, out var previous)) return;
        float nearest = float.MaxValue;
        for (int i = 0; i < Count; i++)
        {
            var candidate = _groups[_group].Items[i];
            if (candidate.Action != previous.Action) continue;
            if (candidate.Action != DestinationAction.Go) { _item = i; return; }
            if (candidate.Name != previous.Name || candidate.Kind != previous.Kind) continue;
            if (previous.Actor.IsValid && (!candidate.Actor.IsValid
                || candidate.Actor.City != previous.Actor.City || candidate.Actor.Owner != previous.Actor.Owner)) continue;
            float dx = candidate.X - previous.X, dy = candidate.Y - previous.Y, dz = candidate.Z - previous.Z;
            float distance = dx * dx + dy * dy + dz * dz;
            if (distance < nearest) { nearest = distance; _item = i; }
        }
    }

    private static int Wrap(int value, int count) => ((value % count) + count) % count;
}
