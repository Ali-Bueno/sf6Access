using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// What the player last picked on the World Tour phone map, remembered as plain
/// numbers so it outlives the map being closed.
///
/// <para><b>Why the phone map and not the minimap.</b> The HUD minimap was tried
/// first and could not be reached at all: neither <c>app.UIMiniMapWindow</c> nor
/// its owner <c>app.UICityHud_CityMiniMap</c> turns up in the scene
/// (confirmed in game 2026-09-09, twice). The phone map, by contrast, has worked
/// since 2026-09-07 — <see cref="SF6Access.Hooks.WorldTour.DeviceMapHooks"/> already
/// reads the icon under the free cursor and speaks it. So the map the player
/// actually browses IS the selection UI; nothing new had to be built for them to
/// choose with, which is also why this replaced a whole modal radar.</para>
///
/// <para><b>One capture point covers everything.</b> The cursor path yields an
/// <c>app.UIMapWindowBase.IconInfo</c>, and every kind of thing on the map arrives
/// through it — missions, shops, masters, NPCs, fast-travel points, and the
/// player's OWN pins: the game builds a pin's icon with
/// <c>MakePinIconInfo(PinInfo, int)</c> (<c>UIMapWindowBase.cs:1038</c>), so a
/// placed marker's <c>GlobalPos</c> is already the pin's own position.</para>
///
/// <para><b>Plain floats, never a wrapper.</b> The icon buffer belongs to a UI that
/// is about to be torn down, and an engine wrapper outliving its object is the
/// finalizer-on-the-GC-thread crash this mod has paid for twice. The three
/// coordinates and a string are copied out and the engine object is let go.</para>
/// </summary>
public static class MapSelectionMemory
{
    /// <summary>A remembered map point. <see cref="Ok"/> is false when the player
    /// has not picked anything since entering the city.</summary>
    public readonly struct Selection
    {
        public readonly bool Ok;

        /// <summary>Already localized by the game and worded by the map reader —
        /// "Beat Square, shop". Spoken verbatim.</summary>
        public readonly string Label;

        public readonly float X, Y, Z;

        public Selection(string label, float x, float y, float z)
        {
            Ok = true; Label = label; X = x; Y = y; Z = z;
        }
    }

    private static Selection _current;

    /// <summary>What the player last picked, or a default whose <c>Ok</c> is false.</summary>
    public static Selection Current => _current;

    /// <summary>Remember a point the player put the map cursor on. Ignores an
    /// unreadable position: an exact origin means the read failed rather than that
    /// something sits at the world origin, the same rule the field readers already
    /// apply to gimmick positions.</summary>
    public static void Remember(string label, float x, float y, float z)
    {
        if (string.IsNullOrWhiteSpace(label)) return;
        if (x == 0f && y == 0f && z == 0f) return;

        bool changed = !_current.Ok || _current.Label != label
                       || _current.X != x || _current.Y != y || _current.Z != z;
        _current = new Selection(label, x, y, z);
        if (changed)
            API.LogInfo($"[SF6Access] Map selection: '{label}' at ({x:0.0}, {y:0.0}, {z:0.0})");
    }

    /// <summary>Read <c>IconInfo.GlobalPos</c> off a map icon and remember it.
    /// <paramref name="info"/> is the icon record the map reader already has in
    /// hand; nothing is retained from it.
    ///
    /// <para><c>GlobalPos</c> is a <c>via.vec3</c> STRUCT member, so it is read
    /// untyped through <see cref="FieldProbeService.Member"/> and unpacked with
    /// <see cref="FlowHelper.ReadVecComponent"/> — naming <c>via.vec3</c> as the
    /// target type yields a dispatch proxy that reads as all zeros, and
    /// <c>GetObjectField</c> only ever returns a <c>ManagedObject</c>, which a
    /// value type is not.</para></summary>
    public static void RememberIcon(object info, string label)
    {
        if (info == null) return;
        object pos = FieldProbeService.Member(info, "GlobalPos");
        if (pos == null) return;
        Remember(label,
                 FlowHelper.ReadVecComponent(pos, "x"),
                 FlowHelper.ReadVecComponent(pos, "y"),
                 FlowHelper.ReadVecComponent(pos, "z"));
    }

    /// <summary>Forget the selection. Called when the field unloads: another city's
    /// coordinates are not this one's, and a point left over from the last city
    /// would aim the player at nothing.</summary>
    public static void Reset(string reason)
    {
        if (_current.Ok) API.LogInfo($"[SF6Access] Map selection cleared ({reason})");
        _current = default;
    }
}
