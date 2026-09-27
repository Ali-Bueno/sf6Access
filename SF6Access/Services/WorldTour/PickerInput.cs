using REFrameworkNET;
using SF6Access.Services;

namespace SF6Access.Services.WorldTour;

/// <summary>
/// The destination picker's controls, pad and keyboard, as edge-detected commands. Pad bits
/// are resolved by member name from <c>via.hid.GamePadButton</c>; the platform's own
/// Decide/Cancel bits are preferred once the device is seen to report them.
/// </summary>
public sealed class PickerInput
{
    public enum Command { None, Next, Previous, NextCategory, PreviousCategory, Choose, Cancel, Delete }

    private const string BUTTON_ENUM = "via.hid.GamePadButton";

    // Keyboard virtual-key codes (winuser.h).
    private const int VK_BACK = 0x08, VK_RETURN = 0x0D, VK_PRIOR = 0x21, VK_NEXT = 0x22;
    private const int VK_LEFT = 0x25, VK_UP = 0x26, VK_RIGHT = 0x27, VK_DOWN = 0x28;
    private const int VK_DELETE = 0x2E, VK_V = 0x56;

    private static readonly (int Vk, Command Command)[] KEYS =
    {
        (VK_DOWN, Command.Next), (VK_UP, Command.Previous),
        (VK_RIGHT, Command.NextCategory), (VK_NEXT, Command.NextCategory),
        (VK_LEFT, Command.PreviousCategory), (VK_PRIOR, Command.PreviousCategory),
        (VK_RETURN, Command.Choose), (VK_BACK, Command.Cancel), (VK_V, Command.Cancel),
        (VK_DELETE, Command.Delete),
    };

    private (uint Bit, Command Command)[] _pad;
    private uint _decide, _cancel, _south, _east, _allPad;
    private bool _platformButtons;
    private uint _lastPad;
    private readonly bool[] _lastKeys = new bool[KEYS.Length];

    /// <summary>Takes the current state as already seen, so a button still held from
    /// opening the picker is not read as a command.</summary>
    public void Reset()
    {
        Resolve();
        _lastPad = ReadoutShortcut.ReadPadButtons();
        for (int i = 0; i < KEYS.Length; i++) _lastKeys[i] = ReadoutShortcut.KeyDown(KEYS[i].Vk);
    }

    /// <summary>Whether any picker button or key is still held.</summary>
    public bool AnyDown()
    {
        Resolve();
        if ((ReadoutShortcut.ReadPadButtons() & _allPad) != 0) return true;
        foreach (var (vk, _) in KEYS)
            if (ReadoutShortcut.KeyDown(vk)) return true;
        return false;
    }

    /// <summary>The first new command this frame, or <see cref="Command.None"/>.</summary>
    public Command Poll()
    {
        Resolve();
        var command = Command.None;

        uint pad = ReadoutShortcut.ReadPadButtons();
        uint pressed = pad & ~_lastPad;
        _lastPad = pad;
        if ((pad & (_decide | _cancel)) != 0) _platformButtons = true;
        uint choose = _platformButtons ? _decide : _south;
        uint cancel = _platformButtons ? _cancel : _east;
        if ((pressed & choose) != 0) command = Command.Choose;
        else if ((pressed & cancel) != 0) command = Command.Cancel;
        else
            foreach (var (bit, c) in _pad)
                if ((pressed & bit) != 0) { command = c; break; }

        bool foreground = ReadoutShortcut.IsGameForeground();
        for (int i = 0; i < KEYS.Length; i++)
        {
            bool down = ReadoutShortcut.KeyDown(KEYS[i].Vk);
            if (down && !_lastKeys[i] && foreground && command == Command.None) command = KEYS[i].Command;
            _lastKeys[i] = down;
        }
        return command;
    }

    private void Resolve()
    {
        if (_pad != null) return;
        _decide = Bit("Decide");
        _cancel = Bit("Cancel");
        _south = Bit("RDown");
        _east = Bit("RRight");
        _pad = new[]
        {
            (Bit("LDown"), Command.Next), (Bit("LUp"), Command.Previous),
            (Bit("LRight"), Command.NextCategory), (Bit("RTrigTop"), Command.NextCategory),
            (Bit("LLeft"), Command.PreviousCategory), (Bit("LTrigTop"), Command.PreviousCategory),
            (Bit("RLeft"), Command.Delete), (Bit("RStickPush"), Command.Cancel),
        };
        _allPad = _decide | _cancel | _south | _east;
        foreach (var (bit, _) in _pad) _allPad |= bit;
        API.LogInfo($"[SF6Access] Destination picker pad: decide={_decide:X} cancel={_cancel:X} " +
                    $"south={_south:X} east={_east:X}");
    }

    private static uint Bit(string member)
    {
        int value = FieldProbeService.EnumValue(BUTTON_ENUM, member);
        return value > 0 ? (uint)value : 0u;
    }
}
