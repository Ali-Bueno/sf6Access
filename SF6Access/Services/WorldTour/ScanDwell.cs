#nullable enable
namespace SF6Access.Services.WorldTour;

/// <summary>A brief look must settle before it speaks; looking away rearms the same target.</summary>
public sealed class ScanDwell
{
    // Three 100 ms scan samples reject names flashed past during a fast sweep.
    public const long DwellMs = 300;
    private string? _key, _spoken;
    private long _since;
    private bool _deliberate;
    public string? PendingKey => _key;

    public bool Observe(string? key, long now, bool deliberate)
    {
        if (key != _key)
        {
            _key = key; _since = now; _spoken = null; _deliberate = deliberate;
        }
        else _deliberate |= deliberate;
        return key != null && key != _spoken && _deliberate && now - _since >= DwellMs;
    }

    public void Spoken(string key) { if (_key == key) _spoken = key; }
    public void Clear() { _key = _spoken = null; _deliberate = false; }
}
