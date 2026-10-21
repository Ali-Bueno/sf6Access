using System;

namespace SF6Access.Services.WorldTour;

/// <summary>A deliberate camera turn speaks its settled compass point once, with boundary hysteresis.</summary>
public sealed class HeadingAnnouncement
{
    public const long SettleMs = 200;
    private const long MinimumGapMs = 600; // Space for one short compass word before another.
    private readonly int _sectors;
    private readonly float _sectorDegrees, _leaveDegrees;
    private int _current = -1, _spoken = -1;
    private long _since, _spokeAt;
    private bool _deliberate;

    public HeadingAnnouncement(int sectors)
    {
        _sectors = sectors;
        _sectorDegrees = 360f / sectors;
        _leaveDegrees = _sectorDegrees / 2f + _sectorDegrees / 8f;
    }

    public int Observe(float degrees, bool deliberate, long now)
    {
        if (!float.IsFinite(degrees)) { Reset(); return -1; }
        degrees = (degrees % 360f + 360f) % 360f;
        int sector = (int)MathF.Floor((degrees + _sectorDegrees / 2f) / _sectorDegrees) % _sectors;
        float delta = _current < 0 ? float.MaxValue
            : MathF.Abs((degrees - _current * _sectorDegrees + 540f) % 360f - 180f);
        if (_current < 0 || delta > _leaveDegrees)
        {
            _current = sector;
            _since = now;
            _deliberate = deliberate;
        }
        else _deliberate |= deliberate;
        return IsCurrent(_current) && now - _since >= SettleMs
            && (_spoken < 0 || now - _spokeAt >= MinimumGapMs) ? _current : -1;
    }

    public bool IsCurrent(int sector) => sector >= 0 && sector == _current && sector != _spoken && _deliberate;

    public void Spoke(int sector, long now)
    {
        if (!IsCurrent(sector)) return;
        _spoken = sector;
        _spokeAt = now;
    }

    public void Reset() { _current = _spoken = -1; _deliberate = false; _since = _spokeAt = 0; }
}
