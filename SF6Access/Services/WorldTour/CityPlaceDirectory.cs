using System;
using System.Collections.Generic;

namespace SF6Access.Services.WorldTour;

/// <summary>A stable named index of known fixed destinations, independent of player distance.</summary>
internal static class CityPlaceDirectory
{
    internal static List<Destination> ByName(IEnumerable<DestinationPicker.Group> groups)
    {
        var places = new List<Destination>();
        var seen = new HashSet<Destination>();
        foreach (var group in groups)
            foreach (var d in group.Items)
                if (CityPlaceFocus.Includes(d) && seen.Add(d)) places.Add(d);
        places.Sort((a, b) =>
        {
            int name = StringComparer.CurrentCultureIgnoreCase.Compare(a.Label, b.Label);
            if (name != 0) return name;
            int x = a.X.CompareTo(b.X);
            if (x != 0) return x;
            int z = a.Z.CompareTo(b.Z);
            return z != 0 ? z : a.Y.CompareTo(b.Y);
        });
        return places;
    }
}
