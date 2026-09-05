using System.Runtime.InteropServices;
using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// Edge-detects an on-demand readout shortcut on both input devices: a keyboard
/// key (only while the game window is foreground, so it never fires while the
/// user types elsewhere) and a gamepad button (via.hid merged device — pad input
/// only reaches the game when it has focus). Callers poll <see cref="Pressed"/>
/// every frame (ReadInterval 1) — slower polls miss short presses.
/// </summary>
public sealed class ReadoutShortcut
{
    /// <summary>Keyboard G — the letter shortcut used by the stats readouts.</summary>
    public const int VK_G = 0x47;

    /// <summary>No gamepad button at all — a keyboard-only shortcut. Every pad
    /// button the testers tried in the World Tour field turned out to be a game
    /// action, and the ones that are free are already taken.</summary>
    public const uint PAD_NONE = 0;

    /// <summary>via.hid.GamePadButton flag for Start / Options (see
    /// InputNameResolver's PadButtonNames table). Chosen by the tester after
    /// R3/L3, Triangle/Y and Square/X all turned out to be game actions in the
    /// shop menus.</summary>
    public const uint PAD_START = 0x8000;

    private readonly int _vk;
    private readonly uint _padFlag;
    private bool _lastKey, _lastPad;

    public ReadoutShortcut(int vk = VK_G, uint padFlag = PAD_START)
    {
        _vk = vk;
        _padFlag = padFlag;
    }

    [DllImport("user32.dll")]
    private static extern short GetAsyncKeyState(int vKey);
    [DllImport("user32.dll")]
    private static extern System.IntPtr GetForegroundWindow();
    [DllImport("user32.dll")]
    private static extern uint GetWindowThreadProcessId(System.IntPtr hWnd, out uint processId);

    /// <summary>True exactly once per key/button press.</summary>
    public bool Pressed()
    {
        bool fired = false;

        bool key = (GetAsyncKeyState(_vk) & 0x8000) != 0;
        if (key && !_lastKey && IsGameForeground()) fired = true;
        _lastKey = key;

        // A zero mask means "keyboard only", and then the pad must not be read at
        // all: ReadPadButtons resolves a native singleton and makes two calls, and
        // this runs every frame for every shortcut that exists.
        if (_padFlag != 0)
        {
            bool pad = (ReadPadButtons() & _padFlag) != 0;
            if (pad && !_lastPad) fired = true;
            _lastPad = pad;
        }

        return fired;
    }

    /// <summary>Currently pressed buttons of the merged gamepad device
    /// (via.hid.GamePad) — also used by the key-config input test.</summary>
    public static uint ReadPadButtons()
    {
        try
        {
            var pad = API.GetNativeSingleton("via.hid.GamePad");
            var device = (pad as IObject)?.Call("get_MergedDevice");
            var button = (device as IObject)?.Call("get_Button");
            return button != null ? System.Convert.ToUInt32(button) : 0;
        }
        catch { return 0; }
    }

    /// <summary>Whether SF6 owns the foreground window. Public because any LETTER
    /// shortcut must ask it before acting — otherwise the mod reacts to the user
    /// typing in another application — and there must be exactly one copy of that
    /// test, not one per hook.</summary>
    public static bool IsGameForeground()
    {
        try
        {
            GetWindowThreadProcessId(GetForegroundWindow(), out uint pid);
            return pid == (uint)System.Environment.ProcessId;
        }
        catch { return false; }
    }
}
