#nullable enable
using System;

namespace SF6Access.Services.WorldTour;

/// <summary>Finds the most aligned known place, without allowing distance to hide a distant landmark.</summary>
internal sealed class CityPlaceFocus
{
    private readonly float _enterDegrees, _leaveDegrees, _switchMargin;
    private string? _best;
    private float _bestAngle, _bestDistance, _currentAngle;

    internal string? Key { get; private set; }
    internal string? SpokenKey { get; private set; }
    internal bool CanSound => Key != null && Key == SpokenKey;

    internal CityPlaceFocus(float enterDegrees, float leaveDegrees, float switchMargin)
    {
        _enterDegrees = enterDegrees; _leaveDegrees = leaveDegrees; _switchMargin = switchMargin;
    }

    internal void Begin()
    {
        _best = null;
        _bestAngle = _bestDistance = _currentAngle = float.PositiveInfinity;
    }

    internal void Offer(string key, float offDegrees, float meters)
    {
        if (string.IsNullOrEmpty(key) || !float.IsFinite(offDegrees) || offDegrees < 0
            || !float.IsFinite(meters) || meters < 0) return;
        if (key == Key && offDegrees <= _leaveDegrees) _currentAngle = offDegrees;
        if (offDegrees > _enterDegrees || offDegrees > _bestAngle
            || offDegrees == _bestAngle && meters >= _bestDistance) return;
        _best = key; _bestAngle = offDegrees; _bestDistance = meters;
    }

    internal void End()
    {
        if (float.IsFinite(_currentAngle) && (_best == null || _bestAngle + _switchMargin >= _currentAngle)) return;
        if (Key != _best) SpokenKey = null;
        Key = _best;
    }

    internal void Spoken(string key) { if (key == Key) SpokenKey = key; }
    internal void Clear() { Key = SpokenKey = null; }

    internal static bool Includes(Destination d) => d.Action == DestinationAction.Go && d.HasPosition
        && !d.Actor.IsValid && d.Category is DestinationCategory.Rest or DestinationCategory.Places
            or DestinationCategory.Travel or DestinationCategory.Shops or DestinationCategory.Jobs
            or DestinationCategory.Pins or DestinationCategory.Saved;
}
