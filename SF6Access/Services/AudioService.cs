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

    // PCM is decoded once; no MP3 reader or unmanaged decoder survives a playback request.
    private static readonly Dictionary<string, CueAudio.Clip> _soundCache = new();

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
            _waveOut.Init(CueAudio.LimitMix(_mixer));
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

    /// <summary>
    /// Play a sound file with stereo panning, optionally detuned.
    /// </summary>
    /// <param name="fileName">Sound file name (e.g. "impassable.mp3")</param>
    /// <param name="pan">Stereo pan: -1 = full left, 0 = center, 1 = full right</param>
    /// <param name="volume">Volume 0.0 to 1.0</param>
    /// <param name="rate">Playback rate: 1 = the file as recorded, 0.5 = one octave
    /// down, 2 = one octave up. Speed moves with pitch (0.5 also lasts twice as
    /// long) — a tape-style shift, which is what makes it cheap.</param>
    /// <param name="maximumDuration">Optional final playback window, after pitch shifting.</param>
    /// <returns>The exact output duration after trimming, or zero when playback fails.</returns>
    /// <summary>The mixer's default cue level: half scale, loud enough over the
    /// game's street ambience, under the reader's voice.</summary>
    public const float DEFAULT_VOLUME = 0.5f;

    public static TimeSpan PlaySound(string fileName, float pan = 0f, float volume = DEFAULT_VOLUME, float rate = 1f,
        TimeSpan? maximumDuration = null)
    {
        if (!_initialized || _mixer == null || _soundsDir == null) return TimeSpan.Zero;
        try
        {
            if (!_soundCache.TryGetValue(fileName, out var clip))
            {
                var filePath = Path.Combine(_soundsDir, fileName);
                if (!File.Exists(filePath))
                {
                    REFrameworkNET.API.LogError($"[SF6Access] Sound file not found: {filePath}");
                    return TimeSpan.Zero;
                }
                clip = CueAudio.Clip.Decode(File.ReadAllBytes(filePath));
                _soundCache[fileName] = clip;
            }

            var playback = clip.Create(pan, volume, rate, maximumDuration);
            if (playback.Duration <= TimeSpan.Zero) return TimeSpan.Zero;
            _mixer.AddMixerInput(playback.Samples);
            return playback.Duration;
        }
        catch (Exception ex)
        {
            REFrameworkNET.API.LogError($"[SF6Access] PlaySound error ({fileName}): {ex.Message}");
            return TimeSpan.Zero;
        }
    }

    // --- Procedural musical tones ---
    // Equal-temperament note frequencies derived from the A4 = 440 Hz concert-pitch
    // standard (each semitone = previous * 2^(1/12)); lets us vary pitch without
    // shipping an audio file per step.
    public const float NoteLa = 440.00f;      // A4
    public const float NoteMi = 659.25f;      // E5  (A4 * 2^(7/12))
    public const float NoteLaHigh = 880.00f;  // A5  (A4 octave up)

    // A SECOND, non-overlapping register. The radar has two tone channels — the
    // approach ladder and the stairs motif — and when both drew on the A/E set above
    // they were built from the same three notes in opposite order, which a player
    // reported as literally the same sound. These share no frequency with them, so the
    // two channels cannot be confused whatever order they are played in.
    public const float NoteDoLow = 261.63f;   // C4  (A4 * 2^(-9/12))
    public const float NoteFaLow = 349.23f;   // F4  (A4 * 2^(-4/12))
    public const float NoteDo = 523.25f;      // C5  (A4 * 2^(3/12))

    private const float NoteDuration = 0.18f; // seconds per note (soft, short)

    /// <summary>
    /// Play one or more soft sine-based musical notes in sequence, panned in stereo.
    /// </summary>
    /// <param name="frequencies">Note frequencies (Hz), played back to back.</param>
    /// <param name="pan">Stereo pan: -1 = left, 0 = center, 1 = right.</param>
    /// <param name="volume">Volume 0.0 to 1.0.</param>
    public static TimeSpan ToneDuration(int notes) => TimeSpan.FromSeconds(Math.Max(0, notes) * NoteDuration);

    public static TimeSpan PlayWorldObject(WorldObjectKind kind, float pan, float volume, bool behind = false)
    {
        if (!_initialized || _mixer == null) return TimeSpan.Zero;
        try
        {
            var playback = WorldObjectSounds.Create(kind, pan, volume, behind);
            _mixer.AddMixerInput(playback.Samples);
            return playback.Duration;
        }
        catch (Exception ex)
        {
            REFrameworkNET.API.LogError($"[SF6Access] World object sound error ({kind}): {ex.Message}");
            return TimeSpan.Zero;
        }
    }

    public static TimeSpan PlayTone(float[] frequencies, float pan = 0f, float volume = 0.4f,
        TimeSpan? maximumDuration = null)
    {
        if (!_initialized || _mixer == null || frequencies == null || frequencies.Length == 0) return TimeSpan.Zero;

        try
        {
            double seconds = NoteDuration;
            if (maximumDuration.HasValue)
                seconds = Math.Min(seconds, maximumDuration.Value.TotalSeconds / frequencies.Length);
            if (seconds <= 0) return TimeSpan.Zero;
            var playback = CueAudio.Tone(frequencies, pan, volume, seconds);
            _mixer.AddMixerInput(playback.Samples);
            return playback.Duration;
        }
        catch (Exception ex)
        {
            REFrameworkNET.API.LogError($"[SF6Access] PlayTone error: {ex.Message}");
            return TimeSpan.Zero;
        }
    }

}
