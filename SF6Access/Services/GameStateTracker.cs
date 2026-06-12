using System.Collections.Generic;

namespace SF6Access.Services;

public static class GameStateTracker
{
    private static readonly Dictionary<string, (string Value, long Tick)> _trackedStates = new();

    // A remembered value goes stale after this long: reopening a menu whose
    // only option matches the last announced text must read it again
    // (single-option menus were silent on every visit after the first)
    private const long EXPIRY_MS = 2500;

    public static bool HasChanged(string key, string currentValue)
    {
        long now = System.Environment.TickCount64;

        if (_trackedStates.TryGetValue(key, out var previous) &&
            previous.Value == currentValue &&
            now - previous.Tick < EXPIRY_MS)
        {
            return false;
        }

        _trackedStates[key] = (currentValue, now);
        return true;
    }

    public static void Clear()
    {
        _trackedStates.Clear();
    }

    public static void Remove(string key)
    {
        _trackedStates.Remove(key);
    }
}
