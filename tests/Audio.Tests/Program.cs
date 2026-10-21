using NAudio.Wave;
using NAudio.Wave.SampleProviders;
using SF6Access.Services;
using SF6Access.Services.WorldTour;

string root = Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "../../../../../"));
string sounds = Path.Combine(root, "SF6Access", "sounds");
string output = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.Combine(Path.GetTempPath(), "sf6access-audio-20261001");
Directory.CreateDirectory(output);
int checks = 0;
void Check(bool value, string message)
{
    if (!value) throw new InvalidOperationException(message);
    checks++;
}

var clips = new Dictionary<string, CueAudio.Clip>();
foreach (string file in Directory.GetFiles(sounds, "*.mp3"))
{
    var stream = new TrackedStream(File.ReadAllBytes(file));
    var clip = CueAudio.Clip.Decode(stream);
    Check(stream.Disposed, "Decoder must release its input after success: " + file);
    clips[Path.GetFileName(file)] = clip;
}
var broken = new TrackedStream(new byte[] { 1, 2, 3, 4 });
bool rejected = false;
try { CueAudio.Clip.Decode(broken); }
catch (InvalidDataException) { rejected = true; }
Check(rejected && broken.Disposed, "Decoder must release its input after failure");

var reports = new List<object>();
foreach (var (name, clip) in clips)
{
    foreach (float rate in new[] { .5f, 1f, 1.5f })
    {
        var playback = clip.Create(0, .5f, rate, FieldCuePolicy.MaximumDuration(FieldCueLane.Focused));
        float[] samples = Render(playback.Samples);
        double actualSeconds = samples.Length / (2.0 * CueAudio.SampleRate);
        Check(Math.Abs(actualSeconds - playback.Duration.TotalSeconds) < 1.0 / CueAudio.SampleRate,
            "Reservation must describe the real rendered duration: " + name);
        Check(actualSeconds <= .36001, "Pitch must not stretch the bounded cue: " + name);
        Check(samples.Length > 0 && samples.All(float.IsFinite), "Decoded output must be finite: " + name);
        Check(Rms(samples) > .0001, "Trimmed asset must retain audible energy: " + name);
        Check(Math.Abs(samples[0]) < .00001 && Math.Abs(samples[^1]) < .00001,
            "Trimmed asset must start and end without a hard edge: " + name);
        reports.Add(new { name, rate, sourceSeconds = clip.Duration.TotalSeconds, renderedSeconds = actualSeconds,
            peak = samples.Max(x => Math.Abs(x)), rms = Rms(samples) });
    }
}

float[] left = Render(clips["ladder.mp3"].Create(-1, .4f, 1, TimeSpan.FromMilliseconds(280)).Samples);
Check(Energy(left, 0) > .01 && Energy(left, 1) < .000001, "Full-left object must stay in the left ear");
float[] right = Render(clips["door knock.mp3"].Create(1, .4f, 1, TimeSpan.FromMilliseconds(280)).Samples);
Check(Energy(right, 1) > .01 && Energy(right, 0) < .000001, "Full-right object must stay in the right ear");

CueAudio.Playback[] FourVoices() => new[]
{
    clips["mission beacon.mp3"].Create(-.5f, .5f, .5f, FieldCuePolicy.MaximumDuration(FieldCueLane.Route)),
    clips["exit.mp3"].Create(1, .9f, 1, FieldCuePolicy.MaximumDuration(FieldCueLane.Geometry)),
    WorldObjectSounds.Create(WorldObjectKind.Ladder, -1, .4f),
    WorldObjectSounds.Create(WorldObjectKind.Shop, .4f, .25f),
};
var independent = FourVoices().Select(x => Render(x.Samples)).ToArray();
var mixer = Mixer();
foreach (var voice in FourVoices()) mixer.AddMixerInput(voice.Samples);
float[] mixed = Render(CueAudio.LimitMix(mixer));
int overlapSamples = 0;
double error = 0;
for (int i = 0; i < mixed.Length; i++)
{
    float sum = 0;
    int active = 0;
    foreach (var voice in independent)
        if (i < voice.Length)
        {
            sum += voice[i];
            if (i % 2 == 0 && i + 1 < voice.Length && Math.Max(Math.Abs(voice[i]), Math.Abs(voice[i + 1])) > .0001) active++;
        }
    if (active == 4) overlapSamples += 2;
    float expected = Math.Abs(sum) <= .85f ? sum : MathF.CopySign(.85f + .15f * MathF.Tanh((Math.Abs(sum) - .85f) / .15f), sum);
    error = Math.Max(error, Math.Abs(mixed[i] - expected));
}
Check(overlapSamples > CueAudio.SampleRate / 20, "All four production cue streams must overlap for more than 25 ms");
Check(error < .00001, "Production mixer must preserve all four voices, including limiter behavior");
Check(mixer.MixerInputs.Count() == 0, "Finished PCM voices must be removed from the mixer");
Check(mixed.Max(x => Math.Abs(x)) <= 1, "Concurrent actual assets must remain within output range");
Write(Path.Combine(output, "four-lanes.wav"), mixed);

var stress = Mixer();
for (int i = 0; i < 4; i++)
    stress.AddMixerInput(new SignalGenerator(CueAudio.SampleRate, 2) { Type = SignalGeneratorType.Square, Gain = 1, Frequency = 440 });
var limiter = CueAudio.LimitMix(stress);
var overload = new float[CueAudio.SampleRate / 5 * 2];
limiter.Read(overload, 0, overload.Length);
Check(overload.All(x => float.IsFinite(x) && Math.Abs(x) <= 1), "Coincident full-scale inputs must never overflow the output");
Check(overload.Any(x => Math.Abs(x) > .9), "Peak protection must not mute overload");

var schedule = Mixer();
void Add(double seconds, CueAudio.Playback voice) => schedule.AddMixerInput(new OffsetSampleProvider(voice.Samples)
    { DelayBy = TimeSpan.FromSeconds(seconds) });
foreach (var voice in FourVoices()) Add(0, voice);
Add(.75, clips["mission beacon.mp3"].Create(.5f, .5f, 1.5f, FieldCuePolicy.MaximumDuration(FieldCueLane.Route)));
Add(.75, clips["impassable.mp3"].Create(-1, .9f, 1, FieldCuePolicy.MaximumDuration(FieldCueLane.Geometry)));
Add(1.5, CueAudio.Tone(new[] { 261.63f, 349.23f, 523.25f }, -.6f, .9f, .28 / 3));
Add(2.1, WorldObjectSounds.Create(WorldObjectKind.Travel, .6f, .4f));
Add(2.7, WorldObjectSounds.Create(WorldObjectKind.Door, -.4f, .25f, behind: true));
Add(2.7, clips["mission beacon.mp3"].Create(.2f, .5f * FieldCuePolicy.SpeechGain, 1, FieldCuePolicy.MaximumDuration(FieldCueLane.Route)));
float[] scenario = Render(CueAudio.LimitMix(schedule));
Check(scenario.Length > 2 * CueAudio.SampleRate * 2.9 && Rms(scenario) > .001, "Listening sequence must contain actual mixed output");
Write(Path.Combine(output, "navigation-sequence.wav"), scenario);

WorldObjectKind[] auditionOrder = { WorldObjectKind.Ladder, WorldObjectKind.Door, WorldObjectKind.Breakable,
    WorldObjectKind.Shop, WorldObjectKind.Place, WorldObjectKind.Travel };
var audition = Mixer();
var objects = new Dictionary<(WorldObjectKind Kind, bool Behind), float[]>();
foreach (bool behind in new[] { false, true })
{
    for (int index = 0; index < auditionOrder.Length; index++)
    {
        var kind = auditionOrder[index];
        var playback = WorldObjectSounds.Create(kind, 0, .5f, behind);
        float[] samples = Render(playback.Samples);
        objects[(kind, behind)] = samples;
        Check(samples.Length / (2.0 * CueAudio.SampleRate) == playback.Duration.TotalSeconds,
            "Object identity must reserve its complete real playback length");
        Check(playback.Duration <= FieldCuePolicy.MaximumDuration(FieldCueLane.Nearby),
            "Full identity must fit the shorter object lane in both directions");
        Check(samples.All(float.IsFinite) && Rms(samples) > .005, "Every object recipe must contain finite energy");
        Check(Math.Abs(samples[0]) < .00001 && Math.Abs(samples[^1]) < .00001, "Object patterns need shaped edges");
        audition.AddMixerInput(new OffsetSampleProvider(WorldObjectSounds.Create(kind, 0, .5f, behind).Samples)
            { DelayBy = TimeSpan.FromSeconds((behind ? 5.6 : 0) + index * .8) });
    }
    var ladder = objects[(WorldObjectKind.Ladder, behind)];
    Check(WindowRms(ladder, .005, .035) > .01 && WindowRms(ladder, .09, .12) > .01
        && WindowRms(ladder, .175, .205) > .01, "Ladder must retain all three strikes even when behind");
    Check(WindowRms(ladder, .065, .082) < .000001 && WindowRms(ladder, .15, .168) < .000001,
        "Ladder strikes must remain separate events rather than one beep");
    var door = objects[(WorldObjectKind.Door, behind)];
    Check(WindowRms(door, .005, .035) > .01 && WindowRms(door, .15, .18) > .01
        && WindowRms(door, .08, .14) < .000001, "Door must retain two spaced knocks even when behind");
    var shop = objects[(WorldObjectKind.Shop, behind)];
    Check(WindowRms(shop, .003, .025) > .002 && WindowRms(shop, .075, .14) > .01
        && WindowRms(shop, .04, .06) < .000001, "Shop must retain register attack and later bell in both directions");
    var place = objects[(WorldObjectKind.Place, behind)];
    Check(WindowRms(place, 0, .015) < WindowRms(place, .1, .16) / 5,
        "Place must swell gently instead of starting like a struck object");
    for (int i = 0; i < auditionOrder.Length; i++)
        for (int j = i + 1; j < auditionOrder.Length; j++)
            Check(Math.Abs(Correlation(objects[(auditionOrder[i], behind)], objects[(auditionOrder[j], behind)])) < .5,
                "Different category routes must not produce a duplicate or merely louder waveform");
}
Write(Path.Combine(output, "object-categories-ahead-then-behind.wav"), Render(CueAudio.LimitMix(audition)));
foreach (var kind in auditionOrder)
    Check(Brightness(objects[(kind, false)]) > 1.5 * Brightness(objects[(kind, true)]),
        "Behind rendering must be darker while retaining the full category pattern: " + kind);

// Stress the object family together at moderate gain; normal gameplay admits fewer simultaneous objects.
var allObjects = Mixer();
for (int i = 0; i < auditionOrder.Length; i++)
    allObjects.AddMixerInput(WorldObjectSounds.Create(auditionOrder[i], -1 + 2f * i / (auditionOrder.Length - 1), .25f).Samples);
float[] objectMix = Render(CueAudio.LimitMix(allObjects));
Check(objectMix.All(x => float.IsFinite(x) && Math.Abs(x) <= 1), "Six-object stress mix must remain finite and bounded");
Check(Rms(objectMix) > .01, "Six-object mix must retain audible output");
Check(allObjects.MixerInputs.Count() == 0, "Object pattern voices must finish and leave the mixer");
Write(Path.Combine(output, "six-objects-moderate-gain.wav"), objectMix);
File.WriteAllText(Path.Combine(output, "object-categories-order.txt"),
    "Ahead: ladder, door, breakable, shop, place, fast travel.\n" +
    "After the longer pause, behind: same order.\n" +
    "Ladder=three metallic strikes; door=two low knocks; breakable=dry crackle; shop=register click and bell; " +
    "place=airy hollow swell; travel=rising rough zip.\n" +
    "Waveform and timing checks verify complete patterns; listener discrimination remains a listening test.\n");

File.WriteAllText(Path.Combine(output, "measurements.json"), System.Text.Json.JsonSerializer.Serialize(new
{
    checks, overlapMilliseconds = overlapSamples * 1000.0 / (2 * CueAudio.SampleRate),
    mixedPeak = mixed.Max(x => Math.Abs(x)), mixError = error, assets = reports
}, new System.Text.Json.JsonSerializerOptions { WriteIndented = true }));
Console.WriteLine($"Passed {checks} production audio checks. Four-source overlap {overlapSamples * 1000.0 / (2 * CueAudio.SampleRate):F1} ms; peak {mixed.Max(x => Math.Abs(x)):F3}.");
Console.WriteLine(output);

static MixingSampleProvider Mixer() => new(WaveFormat.CreateIeeeFloatWaveFormat(CueAudio.SampleRate, 2));
static float[] Render(ISampleProvider provider)
{
    var all = new List<float>();
    var block = new float[2048];
    int read;
    while ((read = provider.Read(block, 0, block.Length)) > 0)
        for (int i = 0; i < read; i++) all.Add(block[i]);
    return all.ToArray();
}
static double Energy(float[] samples, int channel)
{
    double sum = 0;
    for (int i = channel; i < samples.Length; i += 2) sum += samples[i] * samples[i];
    return sum;
}
static double Rms(float[] samples) => Math.Sqrt(samples.Sum(x => (double)x * x) / samples.Length);
static double WindowRms(float[] samples, double start, double end)
{
    int first = (int)(start * CueAudio.SampleRate) * 2, last = Math.Min(samples.Length, (int)(end * CueAudio.SampleRate) * 2);
    double energy = 0;
    for (int i = first; i < last; i++) energy += samples[i] * samples[i];
    return Math.Sqrt(energy / (last - first));
}
static double Correlation(float[] a, float[] b)
{
    double dot = 0, aa = 0, bb = 0;
    for (int i = 0; i < Math.Min(a.Length, b.Length); i++) { dot += a[i] * b[i]; aa += a[i] * a[i]; bb += b[i] * b[i]; }
    return dot / Math.Sqrt(aa * bb);
}
static double Brightness(float[] samples)
{
    double changes = 0, energy = 0;
    for (int i = 2; i < samples.Length; i += 2)
    {
        double change = samples[i] - samples[i - 2];
        changes += change * change;
        energy += samples[i] * samples[i];
    }
    return changes / energy;
}
static void Write(string path, float[] samples)
{
    using var writer = new WaveFileWriter(path, WaveFormat.CreateIeeeFloatWaveFormat(CueAudio.SampleRate, 2));
    writer.WriteSamples(samples, 0, samples.Length);
}
sealed class TrackedStream(byte[] bytes) : MemoryStream(bytes)
{
    public bool Disposed { get; private set; }
    protected override void Dispose(bool disposing) { Disposed = true; base.Dispose(disposing); }
}
