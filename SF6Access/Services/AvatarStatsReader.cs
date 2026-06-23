using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// Reads avatar combat stats for the Status menu. Equipped totals come from the
/// authoritative player data (WTPlayerData.Status.CalcStatus) so they stay
/// correct as gear changes — the on-screen panel text lags behind / shows the
/// grid preview. Per-item stats come from each item's own WTPlayerStatusData
/// (WTItemParam.GetEquipStatus), which lists only the stats that item grants.
/// </summary>
public static class AvatarStatsReader
{
    /// <summary>A single stat row: localized label + its value text.</summary>
    public readonly struct Stat
    {
        public readonly int Type;     // WTStatusDefine.WTPlayerStatusType
        public readonly string Label;
        public readonly string Value;
        public Stat(int type, string label, string value) { Type = type; Label = label; Value = value; }
    }

    // The combat stats shown on the panel, in display order.
    private static readonly int[] CombatStatTypes = { 1, 6, 7, 8, 9, 10 };

    private static Method _getStatValue;
    private static bool _getStatValueCached;

    /// <summary>The avatar's current equipped totals, read from WTPlayerData.Status.CalcStatus.</summary>
    public static List<Stat> ReadEquippedStats(ManagedObject statusParam)
    {
        var result = new List<Stat>();
        var calc = GetCalcStatus(statusParam);
        if (calc == null) return result;

        foreach (int type in CombatStatTypes)
        {
            float v = GetStatValue(calc, type);
            if (float.IsNaN(v)) continue;
            result.Add(new Stat(type, StatLabel(type), ((int)v).ToString()));
        }
        return result;
    }

    /// <summary>
    /// The stats a single gear item grants (only the ones it actually affects),
    /// from WTItemParam.GetEquipStatus().DataList. The focused item is located in
    /// the slot's ItemParamList by its announced name.
    /// </summary>
    public static List<Stat> ReadItemStats(ManagedObject equipParam, string itemName)
    {
        var result = new List<Stat>();
        var itemParam = FindItemParam(equipParam, itemName);
        if (itemParam == null) return result;

        var statusData = FlowHelper.Call(itemParam, "GetEquipStatus", false) as ManagedObject;
        var dataList = FlowHelper.GetObjectField(statusData, "DataList");
        int count = FlowHelper.GetListCount(dataList);
        for (int i = 0; i < count; i++)
        {
            var data = FlowHelper.GetListItem(dataList, i);
            if (data == null) continue;

            int type = FlowHelper.ReadIntField(data, "Type");
            float value = FlowHelper.ReadFloatField(data, "Value", 0f);
            if (value == 0f) continue;   // the item doesn't have this stat — skip it

            result.Add(new Stat(type, StatLabel(type), ((int)value).ToString()));
        }
        return result;
    }

    /// <summary>Format stat rows as "Vitality 5658, Punch 128, ...".</summary>
    public static string FormatStats(List<Stat> stats)
    {
        if (stats == null || stats.Count == 0) return null;
        var parts = new List<string>();
        foreach (var s in stats) parts.Add($"{s.Label} {s.Value}");
        return string.Join(", ", parts);
    }

    /// <summary>Equipped perk names from the perk list widget (mPerkStatus).</summary>
    public static List<string> ReadPerks(ManagedObject equipParam)
    {
        var names = new List<string>();
        var widget = FlowHelper.GetObjectField(equipParam, "mPerkStatus");
        if (widget == null) return names;

        var control = GetWidgetControl(widget);
        if (control == null) return names;

        foreach (var t in GuiTextReader.ReadControlTexts(control, visibleOnly: true))
        {
            if (t.Name != "e_text_name" || string.IsNullOrWhiteSpace(t.Text)) continue;
            string name = t.Text.Trim();
            if (!names.Contains(name)) names.Add(name);
        }
        return names;
    }

    /// <summary>Full spoken summary of the equipped avatar: stats + perks.</summary>
    public static string ReadSummary(ManagedObject statusParam, ManagedObject equipParam)
    {
        var stats = ReadEquippedStats(statusParam);
        if (stats.Count == 0) return null;

        var parts = new List<string> { FormatStats(stats) };

        var perks = ReadPerks(equipParam);
        if (perks.Count > 0)
            parts.Add($"{PerksLabel()}: {string.Join(", ", perks)}");

        return string.Join(". ", parts);
    }

    /// <summary>WTPlayerData.Status.CalcStatus (the computed equipped-total array).</summary>
    private static ManagedObject GetCalcStatus(ManagedObject statusParam)
    {
        var playerData = FlowHelper.GetObjectField(statusParam, "PlayerData");
        var status = FlowHelper.GetObjectField(playerData, "Status");
        return FlowHelper.GetObjectField(status, "CalcStatus");
    }

    /// <summary>WTPlayerStatusArray.GetValue(type) — NaN on failure.</summary>
    private static float GetStatValue(ManagedObject calcStatus, int type)
    {
        try
        {
            if (!_getStatValueCached)
            {
                _getStatValueCached = true;
                _getStatValue = TDB.Get().FindType("app.worldtour.WTPlayerStatusArray")
                    ?.GetMethod("GetValue(app.worldtour.WTStatusDefine.WTPlayerStatusType)");
            }
            if (_getStatValue == null) return float.NaN;

            var r = _getStatValue.InvokeBoxed(typeof(float), calcStatus, new object[] { type });
            return r == null ? float.NaN : System.Convert.ToSingle(r);
        }
        catch { return float.NaN; }
    }

    /// <summary>
    /// Locate the focused grid item's WTItemParam by matching the announced name
    /// in the slot's ItemParamList. Matching by name (not index) avoids the grid's
    /// unequip/sort offset; when no row matches, return null rather than risk
    /// reading the wrong item's stats.
    /// </summary>
    private static ManagedObject FindItemParam(ManagedObject equipParam, string itemName)
    {
        if (string.IsNullOrWhiteSpace(itemName)) return null;

        var list = FlowHelper.GetObjectField(equipParam, "ItemParamList");
        int count = FlowHelper.GetListCount(list);
        if (count == 0) return null;

        string target = itemName.Trim();
        for (int i = 0; i < count; i++)
        {
            var item = FlowHelper.GetListItem(list, i);
            string name = FlowHelper.CleanTags(FlowHelper.Call(item, "GetNameWithLevel") as string)?.Trim();
            if (string.IsNullOrEmpty(name)) continue;

            // Lenient: the displayed label may omit/append the level suffix
            if (name == target ||
                name.Contains(target, System.StringComparison.OrdinalIgnoreCase) ||
                target.Contains(name, System.StringComparison.OrdinalIgnoreCase))
                return item;
        }
        return null;
    }

    /// <summary>Localized stat label for a WTPlayerStatusType value.</summary>
    private static string StatLabel(int type)
    {
        var lang = FlowHelper.GetDisplayLang();
        // WTStatusDefine.WTPlayerStatusType: 1=VitalMax, 6=PunchPower, 7=KickPower,
        // 8=ThrowPower, 9=SpecialPower, 10=Defense.
        switch (type)
        {
            case 1: return lang switch { FlowHelper.UiLang.Es => "Vitalidad", FlowHelper.UiLang.Pt => "Vitalidade", _ => "Vitality" };
            case 6: return lang switch { FlowHelper.UiLang.Es => "Puño", FlowHelper.UiLang.Pt => "Soco", _ => "Punch" };
            case 7: return lang switch { FlowHelper.UiLang.Es => "Patada", FlowHelper.UiLang.Pt => "Chute", _ => "Kick" };
            case 8: return lang switch { FlowHelper.UiLang.Es => "Agarre", FlowHelper.UiLang.Pt => "Agarre", _ => "Throw" };
            case 9: return lang switch { FlowHelper.UiLang.Es => "Ataque único", FlowHelper.UiLang.Pt => "Ataque único", _ => "Unique Attack" };
            case 10: return lang switch { FlowHelper.UiLang.Es => "Defensa", FlowHelper.UiLang.Pt => "Defesa", _ => "Defense" };
            default:
                // Unknown type: fall back to the enum constant name so nothing is mislabeled
                string enumName = FlowHelper.ResolveEnumName("app.worldtour.WTStatusDefine.WTPlayerStatusType", type);
                return string.IsNullOrEmpty(enumName) ? $"Stat {type}" : enumName;
        }
    }

    private static string PerksLabel()
        => FlowHelper.GetDisplayLang() switch
        {
            FlowHelper.UiLang.Es => "Ventajas",
            FlowHelper.UiLang.Pt => "Vantagens",
            _ => "Perks",
        };

    /// <summary>Try the common accessors a UIParts widget exposes for its root via.gui control.</summary>
    private static ManagedObject GetWidgetControl(ManagedObject widget)
    {
        foreach (var getter in new[] { "get_Control", "get_GUI", "get_View", "get_Panel" })
        {
            var c = FlowHelper.Call(widget, getter) as ManagedObject;
            if (c != null) return c;
        }
        foreach (var field in new[] { "Control", "_control", "mControl", "GUI", "_gui" })
        {
            var c = FlowHelper.GetObjectField(widget, field);
            if (c != null) return c;
        }
        return null;
    }
}
