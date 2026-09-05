using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SF6Access.Services;

/// <summary>
/// Plays mod-owned sound files and procedural tones through NAudio, with stereo panning.
///
/// The game's own Wwise system can only trigger events that already exist in SF6's
/// soundbanks, so any cue we author ourselves (radar sweeps, beacons) has to be mixed
/// by the mod. This service owns that mixer.
/// </summary>
public static class AudioService
{
    private static WaveOutEvent _waveOut;
    private static MixingSampleProvider _mixer;
    private static bool _initialized;
    private static string _soundsDir;

    // Cached sound file bytes (loaded once from disk)
    private static readonly Dictionary<string, byte[]> _soundCache = new();

    private const int SampleRate = 44100;

    /// <summary>Folder the build drops mod sounds into, next to the plugin DLL.</summary>
    private const string SoundsFolderName = "SF6Access.sounds";

    public static void Initialize()
    {
        if (_initialized) return;

        try
        {
            // Resolve sounds directory relative to the plugin DLL
            var pluginDir = Path.GetDirectoryName(typeof(AudioService).Assembly.Location);
            _soundsDir = Path.Combine(pluginDir!, SoundsFolderName);

            var waveFormat = WaveFormat.CreateIeeeFloatWaveFormat(SampleRate, 2);
            _mixer = new MixingSampleProvider(waveFormat) { ReadFully = true };
            _waveOut = new WaveOutEvent { DesiredLatency = 100 };
            _waveOut.Init(_mixer);
            _waveOut.Play();
            _initialized = true;
            REFrameworkNET.API.LogInfo($"[SF6Access] AudioService initialized, sounds dir: {_soundsDir}");
        }
        catch (Exception ex)
        {
            REFrameworkNET.API.LogError($"[SF6Access] AudioService init error: {ex.Message}");
        }
    }

    public static void Shutdown()
    {
        try
        {
            _waveOut?.Stop();
            _waveOut?.Dispose();
            _waveOut = null;
            _mixer = null;
            _initialized = false;
            _soundCache.Clear();
        }
        catch { }
    }

    // Playback-rate bounds. Two octaves either way: past that the resampler is
    // stretching a cue so far that it stops sounding like itself, which defeats
    // the whole point of a recognisable cue.
    private const float MinPlaybackRate = 0.25f;
    private const float MaxPlaybackRate = 4f;

    /// <summary>
    /// Play a sound file with stereo panning, optionally detuned.
    /// </summary>
    /// <param name="fileName">Sound file name (e.g. "impassable.mp3")</param>
    /// <param name="pan">Stereo pan: -1 = full left, 0 = center, 1 = full right</param>
    /// <param name="volume">Volume 0.0 to 1.0</param>
    /// <param name="rate">Playback rate: 1 = the file as recorded, 0.5 = one octave
    /// down, 2 = one octave up. Speed moves with pitch (0.5 also lasts twice as
    /// long) — a tape-style shift, which is what makes it cheap.</param>
    /// <returns>How long the queued sound will occupy, i.e. the file's own length
    /// divided by <paramref name="rate"/>, or <see cref="TimeSpan.Zero"/> if nothing
    /// was queued. A caller that repeats a cue spaces its repeats from this instead
    /// of hard-coding the file's duration.</returns>
    public static TimeSpan PlaySound(string fileName, float pan = 0f, float volume = 0.5f, float rate = 1f)
    {
        if (!_initialized || _mixer == null || _soundsDir == null) return TimeSpan.Zero;
        rate = Math.Clamp(rate, MinPlaybackRate, MaxPlaybackRate);

        try
        {
            // Cache file bytes to avoid repeated disk reads
            if (!_soundCache.TryGetValue(fileName, out var fileBytes))
            {
                var filePath = Path.Combine(_soundsDir, fileName);
                if (!File.Exists(filePath))
                {
                    REFrameworkNET.API.LogError($"[SF6Access] Sound file not found: {filePath}");
                    return TimeSpan.Zero;
                }
                fileBytes = File.ReadAllBytes(filePath);
                _soundCache[fileName] = fileBytes;
            }

            var stream = new MemoryStream(fileBytes);
            var reader = new Mp3FileReader(stream);
            var sampleProvider = reader.ToSampleProvider();

            // Convert to mono if stereo, so panning has something to place
            ISampleProvider mono = sampleProvider.WaveFormat.Channels == 2
                ? new StereoToMonoSampleProvider(sampleProvider)
                : sampleProvider;

            // Detune by RE-LABELLING the source's sample rate before the resampler
            // pulls it back up to the mixer's fixed rate: material recorded at
            // 44100 Hz but declared as 22050 Hz makes the resampler emit two output
            // samples for every input one, so it plays an octave down at half speed.
            // At rate 1 nothing is wrapped and the path is byte for byte what it was.
            if (rate != 1f) mono = new RateShiftSampleProvider(mono, rate);

            if (mono.WaveFormat.SampleRate != SampleRate)
                mono = new WdlResamplingSampleProvider(mono, SampleRate);

            var volumeProvider = new VolumeSampleProvider(mono) { Volume = volume };
            var panned = new PanningSampleProvider(volumeProvider) { Pan = Math.Clamp(pan, -1f, 1f) };
            _mixer.AddMixerInput(panned);
            return TimeSpan.FromSeconds(reader.TotalTime.TotalSeconds / rate);
        }
        catch (Exception ex)
        {
            REFrameworkNET.API.LogError($"[SF6Access] PlaySound error ({fileName}): {ex.Message}");
            return TimeSpan.Zero;
        }
    }

    /// <summary>Passes samples straight through but DECLARES a different sample
    /// rate, so the resampler downstream stretches the sound in time — the cheapest
    /// pitch shift there is, for cues where speed is allowed to move with pitch.</summary>
    private sealed class RateShiftSampleProvider : ISampleProvider
    {
        private readonly ISampleProvider _source;

        public WaveFormat WaveFormat { get; }

        public RateShiftSampleProvider(ISampleProvider source, float rate)
        {
            _source = source;
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(
                (int)Math.Round(source.WaveFormat.SampleRate * rate), source.WaveFormat.Channels);
        }

        public int Read(float[] buffer, int offset, int count) => _source.Read(buffer, offset, count);
    }

    // --- Procedural musical tones ---
    // Equal-temperament note frequencies derived from the A4 = 440 Hz concert-pitch
    // standard (each semitone = previous * 2^(1/12)); lets us vary pitch without
    // shipping an audio file per step.
    public const float NoteLa = 440.00f;      // A4
    public const float NoteMi = 659.25f;      // E5  (A4 * 2^(7/12))
    public const float NoteLaHigh = 880.00f;  // A5  (A4 octave up)

    private const float NoteDuration = 0.18f; // seconds per note (soft, short)

    /// <summary>
    /// Play one or more soft sine-based musical notes in sequence, panned in stereo.
    /// </summary>
    /// <param name="frequencies">Note frequencies (Hz), played back to back.</param>
    /// <param name="pan">Stereo pan: -1 = left, 0 = center, 1 = right.</param>
    /// <param name="volume">Volume 0.0 to 1.0.</param>
    public static void PlayTone(float[] frequencies, float pan = 0f, float volume = 0.4f)
    {
        if (!_initialized || _mixer == null || frequencies == null || frequencies.Length == 0) return;

        try
        {
            ISampleProvider tone = new ToneSampleProvider(SampleRate, frequencies, NoteDuration);
            var volumeProvider = new VolumeSampleProvider(tone) { Volume = volume };
            var panned = new PanningSampleProvider(volumeProvider) { Pan = Math.Clamp(pan, -1f, 1f) };
            _mixer.AddMixerInput(panned);
        }
        catch (Exception ex)
        {
            REFrameworkNET.API.LogError($"[SF6Access] PlayTone error: {ex.Message}");
        }
    }

    // Generates a sequence of soft sine notes (with a light 2nd harmonic for warmth)
    // each shaped by a short attack / long release envelope so they sound like gentle
    // musical tones rather than clicks. Mono output (panned downstream).
    private sealed class ToneSampleProvider : ISampleProvider
    {
        private readonly float[] _frequencies;
        private readonly int _samplesPerNote;
        private readonly int _totalSamples;
        private readonly int _attackSamples;
        private readonly int _releaseSamples;
        private int _pos;

        public WaveFormat WaveFormat { get; }

        public ToneSampleProvider(int sampleRate, float[] frequencies, float noteDuration)
        {
            WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(sampleRate, 1);
            _frequencies = frequencies;
            _samplesPerNote = (int)(sampleRate * noteDuration);
            _totalSamples = _samplesPerNote * frequencies.Length;
            _attackSamples = (int)(sampleRate * 0.012f); // 12 ms fade-in
            _releaseSamples = (int)(sampleRate * 0.10f); // 100 ms fade-out tail
        }

        public int Read(float[] buffer, int offset, int count)
        {
            int n = 0;
            int sr = WaveFormat.SampleRate;
            for (; n < count && _pos < _totalSamples; n++, _pos++)
            {
                int noteIndex = _pos / _samplesPerNote;
                int noteSample = _pos - noteIndex * _samplesPerNote;
                float freq = _frequencies[noteIndex];
                float t = noteSample / (float)sr;

                float sample = (float)(Math.Sin(2.0 * Math.PI * freq * t)
                                       + 0.15 * Math.Sin(2.0 * Math.PI * 2.0 * freq * t));

                // Per-note attack/release envelope
                float env;
                if (noteSample < _attackSamples)
                    env = noteSample / (float)_attackSamples;
                else
                {
                    int remaining = _samplesPerNote - noteSample;
                    env = remaining < _releaseSamples ? remaining / (float)_releaseSamples : 1f;
                }

                buffer[offset + n] = sample * env * 0.55f;
            }
            return n; // returning 0 tells the mixer this input is finished
        }
    }
}
