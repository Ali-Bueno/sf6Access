using REFrameworkNET;

namespace SF6Access.Services.Ui;

/// <summary>
/// Watches the use-item confirm popup — GUI widget UIWidget_ItemConfirmWindow,
/// which creates NO flow param of its own (the owning screen's param stays
/// active) — and announces its question + effect once per appearance. Shared by
/// the WTM pause Item tab and the device item app (ui50201).
/// </summary>
public sealed class ItemConfirmWatcher
{
    private const string GUI_OWNER = "UIWidget_ItemConfirmWindow";

    private bool _announced;

    /// <summary>True while the popup is on screen (as of the last Poll) — its
    /// Yes/No buttons only surface through the generic focus reader, so screens
    /// that suppress it lift the suppression while this is true.</summary>
    public bool IsOpen { get; private set; }

    public void Reset()
    {
        _announced = false;
        IsOpen = false;
    }

    public void Poll()
    {
        string question = null, effect = null, value = null;
        bool present = false;
        foreach (var t in GuiTextReader.ReadTextsByOwner(GUI_OWNER))
        {
            present = true;
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            string s = t.Text.Replace('\n', ' ').Trim();
            switch (t.Name)
            {
                case "e_text_detail": question ??= s; break;  // "Use Energy Drink S?"
                case "e_text_name": effect ??= s; break;      // "Recover Vitality"
                case "e_text_value": value ??= s; break;      // "+10000"
            }
        }
        IsOpen = present;
        if (!present)
        {
            _announced = false;
            return;
        }
        if (_announced || question == null) return;
        _announced = true;

        string msg = question;
        if (effect != null) msg += $". {effect}" + (value != null ? $" {value}" : "");
        API.LogInfo($"[SF6Access] Item confirm: {msg}");
        ScreenReaderService.Speak(msg, interrupt: true);
    }
}

/// <summary>
/// Reads the selected row of an item grid whose cells only carry owned-count
/// numbers (e_text_total) while the item's name/description render in a shared
/// GUI area: "name xN. description". Shared by the WTM pause Item tab
/// (WTMBattlePauseItem) and the device item app (ui50201).
/// </summary>
public static class ItemGridReader
{
    public static string ReadSelectedItem(ManagedObject grid, string guiOwner)
    {
        string name = null, detail = null;
        foreach (var t in GuiTextReader.ReadTextsByOwner(guiOwner))
        {
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            string s = t.Text.Replace('\n', ' ').Trim();
            if (t.Name == "e_text_name" && name == null) name = s;          // item title (selected)
            else if (t.Name == "e_text_detail" && detail == null) detail = s; // description
        }
        if (string.IsNullOrEmpty(name)) return null;

        // Owned count: the selected grid cell carries only its own number
        string count = null;
        var cell = FlowHelper.Call(grid, "get_SelectedItem") as ManagedObject;
        if (cell != null)
        {
            foreach (var t in GuiTextReader.ReadControlTexts(cell))
            {
                if (t.Name == "e_text_total" && !string.IsNullOrWhiteSpace(t.Text))
                {
                    count = t.Text.Trim();
                    break;
                }
            }
        }

        string msg = string.IsNullOrEmpty(count) ? name : $"{name} x{count}";
        if (!string.IsNullOrEmpty(detail)) msg += $". {detail}";
        return msg;
    }
}
