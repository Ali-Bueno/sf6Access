using System.Collections.Generic;
using System.IO;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>Spots the player saved from the destination picker, kept on disk per city.</summary>
public static class SavedPlaces
{
    private const string FILE_NAME = "sf6access_saved_places.txt";

    private static List<PlaceFile.Entry> _all;

    public static IEnumerable<PlaceFile.Entry> For(uint city)
    {
        Load();
        foreach (var e in _all)
            if (e.City == city) yield return e;
    }

    /// <summary>Saves the player's current position under a name built from where they
    /// are ("Beat Square, spot 3"); returns that name, or null when it could not be saved.</summary>
    public static string SaveHere(string zoneName)
    {
        uint city = WorldTourStateService.CityId;
        var me = AvatarFieldReader.ReadPlayerPos(WorldTourStateService.GetAvatarManager());
        if (city == 0 || !me.ok) return null;

        Load();
        int number = _all.FindAll(e => e.City == city).Count + 1;
        string name = DestinationText.SavedName(zoneName, number);
        while (_all.Exists(e => e.City == city && e.Name == name))
            name = DestinationText.SavedName(zoneName, ++number);
        _all.Add(new PlaceFile.Entry(city, 0, 0, name, me.x, me.y, me.z));
        bool ok = PlaceFile.Save(FilePath, _all);
        API.LogInfo($"[SF6Access] Saved place '{name}' at ({me.x:0.0}, {me.y:0.0}, {me.z:0.0})" +
                    (ok ? "" : " - could not write the file"));
        return name;
    }

    public static bool Delete(uint city, string name)
    {
        Load();
        int removed = _all.RemoveAll(e => e.City == city && e.Name == name);
        if (removed == 0) return false;
        PlaceFile.Save(FilePath, _all);
        API.LogInfo($"[SF6Access] Deleted saved place '{name}'");
        return true;
    }

    private static void Load() => _all ??= PlaceFile.Load(FilePath);

    private static string FilePath => Path.Combine(ObjectDumper.DumpDir, FILE_NAME);
}
