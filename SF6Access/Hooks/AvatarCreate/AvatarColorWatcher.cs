using System;
using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces avatar color changes by part while the character creator is open.
/// The authoritative colors live in the live edit data
/// (UIFlowUI61000.Param → MyEditPresetParam → EditParam, an
/// app.CharaEdit.charaEditParam) as raw via.Color RGBA — the model applies
/// them live while the user moves swatch grids or the HLS popup sliders, so
/// watching these fields gives real color feedback with no scale guessing.
/// Names come from ColorNamer (the game has no color-name table).
/// </summary>
internal sealed class AvatarColorWatcher
{
    // (part label lang key, nested owner field on charaEditParam or null, via.Color field)
    private static readonly (string LabelKey, string LabelFallback, string Owner, string Field)[] Entries =
    {
        ("avpart.skin",       "Skin",            null,    "FaceColor"),
        ("avpart.paint",      "Paint",           null,    "PaintColor"),
        ("avpart.hair",       "Hair",            null,    "HairColor"),
        ("avpart.hair2",      "Hair secondary",  null,    "HairColor2"),
        ("avpart.chest_hair", "Chest hair",      null,    "ChestHairColor"),
        ("avpart.back_hair",  "Back hair",       null,    "BackHairColor"),
        ("avpart.arm_hair",   "Arm hair",        null,    "ArmHairColor"),
        ("avpart.leg_hair",   "Leg hair",        null,    "LegHairColor"),
        ("avpart.eye_r",      "Right eye",       "eye_r", "iris_col"),
        ("avpart.eye_l",      "Left eye",        "eye_l", "iris_col"),
        ("avpart.sclera_r",   "Right sclera",    "eye_r", "sclera_col"),
        ("avpart.sclera_l",   "Left sclera",     "eye_l", "sclera_col"),
        ("avpart.brow_l",     "Left eyebrow",    "brows", "colL"),
        ("avpart.brow_r",     "Right eyebrow",   "brows", "colR"),
        ("avpart.lash_up",    "Upper eyelashes", "lash",  "colorup"),
        ("avpart.lash_down",  "Lower eyelashes", "lash",  "colordown"),
    };

    private const long MIN_ANNOUNCE_INTERVAL_MS = 300; // slider drags change colors every frame

    private ManagedObject _rootParam;
    private readonly uint?[] _lastRgba = new uint?[Entries.Length];
    private readonly string[] _lastName = new string[Entries.Length];
    private bool _seeded;
    private long _lastAnnounceMs;

    public void Bind(ManagedObject rootParam)
    {
        _rootParam = rootParam;
        _seeded = false;
        Array.Clear(_lastRgba, 0, _lastRgba.Length);
        Array.Clear(_lastName, 0, _lastName.Length);
    }

    public void Reset() => Bind(null);

    public void Poll()
    {
        var edit = ResolveCharaEditParam();
        if (edit == null) return;

        var announcements = new List<string>();

        for (int i = 0; i < Entries.Length; i++)
        {
            var e = Entries[i];
            uint? rgba = ReadEntryColor(edit, e.Owner, e.Field);
            if (rgba == null) continue;

            if (!_seeded || _lastRgba[i] == null)
            {
                _lastRgba[i] = rgba;
                _lastName[i] = ColorNamer.NameRgba(rgba.Value);
                continue;
            }

            if (rgba == _lastRgba[i]) continue;
            _lastRgba[i] = rgba;

            string name = ColorNamer.NameRgba(rgba.Value);
            API.LogInfo($"[SF6Access] Avatar color {e.Field} = #{rgba.Value:X8} -> {name}");
            if (name == _lastName[i]) continue; // same spoken name, skip chatter
            _lastName[i] = name;

            string label = LangFile.Get(e.LabelKey, e.LabelFallback);
            announcements.Add($"{label}: {name}");
        }

        _seeded = true;
        if (announcements.Count == 0) return;

        long now = Environment.TickCount64;
        if (now - _lastAnnounceMs < MIN_ANNOUNCE_INTERVAL_MS) return;
        _lastAnnounceMs = now;

        ScreenReaderService.Speak(string.Join(". ", announcements), interrupt: true);
    }

    /// <summary>
    /// Color of one entry. Nested owners (eye_r/eye_l/brows/lash) are INLINE
    /// STRUCTS on charaEditParam (boxed as ValueType, not ManagedObject —
    /// confirmed by the 2026-07-07 dump), so their colors are read via the
    /// container-aware path; plain-object owners still work if the game ever
    /// changes them to classes.
    /// </summary>
    internal static uint? ReadEntryColor(ManagedObject edit, string owner, string field)
    {
        if (owner == null) return FlowHelper.ReadColorField(edit, field);

        // Resolve the owner via the TDB field (silent) — ManagedObject.GetField
        // logs "Member not found" spam for struct-typed fields every poll
        try
        {
            var td = edit.GetTypeDefinition();
            var ownerField = td?.GetField(owner) ?? td?.GetField($"<{owner}>k__BackingField");
            if (ownerField == null) return null;

            var boxed = ownerField.GetDataBoxed(typeof(object), edit.GetAddress(), false);
            if (boxed is ManagedObject mo)
                return FlowHelper.ReadColorField(mo, field);
            if (boxed is REFrameworkNET.ValueType vt)
                return FlowHelper.ReadColorFieldIn(ownerField.Type, vt.GetAddress(), true, field);
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Re-resolve every poll: undo/redo and preset loads can swap the edit
    /// data instance under us. MyEditPresetParam is a computed getter;
    /// EditParam is a plain field.
    /// </summary>
    private ManagedObject ResolveCharaEditParam()
    {
        if (_rootParam == null) return null;
        var presetData = FlowHelper.Call(_rootParam, "get_MyEditPresetParam") as ManagedObject
                         ?? FlowHelper.GetObjectField(_rootParam, "MyEditPresetParam");
        if (presetData == null) return null;
        return FlowHelper.GetObjectField(presetData, "EditParam")
               ?? FlowHelper.Call(presetData, "get_EditParam") as ManagedObject;
    }
}
