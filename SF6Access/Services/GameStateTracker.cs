using System.Collections.Generic;

namespace SF6Access.Services;

public static class GameStateTracker
{
    private static readonly Dictionary<string, string> _trackedStates = new();

    public static bool HasChanged(string key, string currentValue)
    {
        if (_trackedStates.TryGetValue(key, out var previousValue) && previousValue == currentValue)
            return false;

        _trackedStates[key] = currentValue;
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
