using System;
using System.Collections.Generic;
using System.IO;
using NAudio.Wave;
using NAudio.Wave.SampleProviders;

namespace SF6Access.Services;

/// <summary>Owned PCM clips and the production playback chain, also renderable without an audio device.</summary>
public static class CueAudio
{
    public const int SampleRate = 44100;
    // Preserve recognizable timbre within two octaves of the recorded cue.
    private const float MinPlaybackRate = .25f, MaxPlaybackRate = 4f;
    // Short fades preserve the cue's attack and remove clicks at a trimmed tail.
    private const double AttackSeconds = .004, ReleaseSeconds = .025;
    // Remove MP3 priming/leading quiet only for short cues, retaining a small pre-attack margin.
    private const float OnsetFraction = .01f;

    public readonly record struct Playback(ISampleProvider Samples, TimeSpan Duration);

    public sealed class Clip
    {
        private readonly float[] _samples;
        private readonly int _onset;
        public TimeSpan Duration => TimeSpan.FromSeconds(_samples.Length / (double)SampleRate);

        private Clip(float[] samples)
        {
            _samples = samples;
            float peak = 0f;
            foreach (float sample in samples) peak = Math.Max(peak, Math.Abs(sample));
            int start = 0;
            while (start < samples.Length && Math.Abs(samples[start]) <= peak * OnsetFraction) start++;
            _onset = Math.Max(0, start - (int)(AttackSeconds * SampleRate));
        }

        public static Clip Decode(byte[] bytes) => Decode(new MemoryStream(bytes, writable: false));

        /// <summary>Consumes and disposes the input, including when decoding fails.</summary>
        public static Clip Decode(Stream stream)
        {
            using var ownedStream = stream;
            using var reader = new Mp3FileReader(stream);
            ISampleProvider mono = reader.ToSampleProvider();
            if (mono.WaveFormat.Channels == 2) mono = new StereoToMonoSampleProvider(mono);
            if (mono.WaveFormat.Channels != 1) throw new InvalidDataException("Cue must contain mono or stereo audio.");
            if (mono.WaveFormat.SampleRate != SampleRate) mono = new WdlResamplingSampleProvider(mono, SampleRate);
            var samples = new List<float>();
            var buffer = new float[SampleRate];
            int count;
            while ((count = mono.Read(buffer, 0, buffer.Length)) > 0)
                for (int i = 0; i < count; i++) samples.Add(buffer[i]);
            return new Clip(samples.ToArray());
        }

        public Playback Create(float pan, float volume, float rate = 1f, TimeSpan? maximumDuration = null)
        {
            rate = float.IsFinite(rate) ? Math.Clamp(rate, MinPlaybackRate, MaxPlaybackRate) : 1f;
            int start = maximumDuration.HasValue ? _onset : 0;
            int shiftedRate = (int)Math.Round(SampleRate * rate);
            double seconds = (_samples.Length - start) / (double)shiftedRate;
            if (maximumDuration.HasValue) seconds = Math.Min(seconds, Math.Max(0, maximumDuration.Value.TotalSeconds));
            int frames = (int)Math.Floor(seconds * SampleRate);
            ISampleProvider source = new ArraySource(_samples, start, shiftedRate);
            if (shiftedRate != SampleRate) source = new WdlResamplingSampleProvider(source, SampleRate);
            source = new Window(source, frames);
            source = Pan(source, pan, volume);
            return new Playback(source, TimeSpan.FromSeconds(frames / (double)SampleRate));
        }
    }

    public static ISampleProvider LimitMix(ISampleProvider mixer) => new PeakCeiling(mixer);

    /// <summary>Play an immutable mono pattern whose attack and tail are already shaped.</summary>
    public static Playback Pcm(float[] samples, float pan, float volume) =>
        new(Pan(new ArraySource(samples, 0, SampleRate), pan, volume), TimeSpan.FromSeconds(samples.Length / (double)SampleRate));

    public static Playback Tone(float[] notes, float pan, float volume, double noteSeconds)
    {
        int noteFrames = Math.Max(1, (int)(SampleRate * noteSeconds));
        var samples = new float[noteFrames * notes.Length];
        int attack = (int)(AttackSeconds * SampleRate);
        int release = Math.Min(noteFrames, (int)(.10 * SampleRate));
        for (int n = 0; n < notes.Length; n++)
            for (int i = 0; i < noteFrames; i++)
            {
                double phase = 2 * Math.PI * notes[n] * i / SampleRate;
                double envelope = Math.Min(1, Math.Min(i / (double)attack, (noteFrames - i - 1) / (double)release));
                samples[n * noteFrames + i] = (float)((Math.Sin(phase) + .15 * Math.Sin(2 * phase)) * envelope * .55);
            }
        return new Playback(Pan(new ArraySource(samples, 0, SampleRate), pan, volume),
            TimeSpan.FromSeconds(samples.Length / (double)SampleRate));
    }

    private static ISampleProvider Pan(ISampleProvider mono, float pan, float volume)
    {
        var gain = new VolumeSampleProvider(mono) { Volume = float.IsFinite(volume) ? Math.Clamp(volume, 0, 1) : 0 };
        return new PanningSampleProvider(gain) { Pan = float.IsFinite(pan) ? Math.Clamp(pan, -1, 1) : 0 };
    }

    private sealed class ArraySource : ISampleProvider
    {
        private readonly float[] _samples;
        private int _position;
        public WaveFormat WaveFormat { get; }
        public ArraySource(float[] samples, int start, int rate)
        { _samples = samples; _position = start; WaveFormat = WaveFormat.CreateIeeeFloatWaveFormat(rate, 1); }
        public int Read(float[] buffer, int offset, int count)
        {
            int available = Math.Min(count, _samples.Length - _position);
            Array.Copy(_samples, _position, buffer, offset, available);
            _position += available;
            return available;
        }
    }

    private sealed class Window : ISampleProvider
    {
        private readonly ISampleProvider _source;
        private readonly int _frames;
        private int _position;
        public WaveFormat WaveFormat => _source.WaveFormat;
        public Window(ISampleProvider source, int frames) { _source = source; _frames = frames; }
        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, Math.Min(count, _frames - _position));
            int attack = (int)(SampleRate * AttackSeconds), release = (int)(SampleRate * ReleaseSeconds);
            for (int i = 0; i < read; i++, _position++)
            {
                float envelope = Math.Min(1f, Math.Min(_position / (float)attack, (_frames - _position - 1) / (float)release));
                buffer[offset + i] *= Math.Max(0, envelope);
            }
            return read;
        }
    }

    private sealed class PeakCeiling : ISampleProvider
    {
        // Preserve normal levels. Only coincident peaks above 85% enter the soft knee.
        private const float Knee = .85f;
        private readonly ISampleProvider _source;
        public PeakCeiling(ISampleProvider source) => _source = source;
        public WaveFormat WaveFormat => _source.WaveFormat;
        public int Read(float[] buffer, int offset, int count)
        {
            int read = _source.Read(buffer, offset, count);
            for (int i = offset; i < offset + read; i++)
            {
                float sample = buffer[i], magnitude = Math.Abs(sample);
                if (!float.IsFinite(sample)) buffer[i] = 0;
                else if (magnitude > Knee)
                    buffer[i] = MathF.CopySign(Knee + (1 - Knee) * MathF.Tanh((magnitude - Knee) / (1 - Knee)), sample);
            }
            return read;
        }
    }
}
