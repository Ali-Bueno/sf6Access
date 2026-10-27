using System;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Hooks;

namespace SF6Access.Services;

/// <summary>
/// Asks GitHub once per launch for the latest SF6Access release and, if it is newer, says so on the first announced screen.
/// </summary>
public static class UpdateChecker
{
    private const string LATEST_RELEASE_API = "https://api.github.com/repos/Ali-Bueno/sf6Access/releases/latest";
    private const string RELEASES_PAGE = "https://github.com/Ali-Bueno/sf6Access/releases/latest";
    private static readonly TimeSpan RequestTimeout = TimeSpan.FromSeconds(15);

    private record Result(Version Latest, string Error);

    // Written by the background request, consumed on the game thread
    private static volatile Result _result;
    private static bool _done;

    [PluginEntryPoint]
    public static void Start() => Task.Run(CheckAsync);

    private static async Task CheckAsync()
    {
        try
        {
            using var http = new HttpClient { Timeout = RequestTimeout };
            // GitHub's API rejects requests without a User-Agent
            http.DefaultRequestHeaders.UserAgent.ParseAdd($"SF6Access/{Format(Installed())}");
            http.DefaultRequestHeaders.Accept.ParseAdd("application/vnd.github+json");
            using var json = JsonDocument.Parse(await http.GetStringAsync(LATEST_RELEASE_API));
            string tag = json.RootElement.GetProperty("tag_name").GetString();
            _result = TryParseTag(tag, out var latest)
                ? new Result(latest, null)
                : new Result(null, $"unreadable release tag '{tag}'");
        }
        catch (Exception ex)
        {
            _result = new Result(null, ex.Message);
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        if (_done) return;
        var result = _result;
        if (result == null) return;

        var installed = Installed();
        if (result.Latest == null || result.Latest <= installed)
        {
            _done = true;
            API.LogInfo(result.Latest == null
                ? $"[SF6Access] Update check failed: {result.Error}"
                : $"[SF6Access] Up to date (installed {Format(installed)}, latest {Format(result.Latest)})");
            return;
        }

        // Wait for the first spoken screen: the game language is known by then and boot speech can't cut the notice
        if (FlowTrackerHooks.CurrentContext == null) return;
        _done = true;
        API.LogInfo($"[SF6Access] Update available: {Format(result.Latest)} (installed {Format(installed)}) — {RELEASES_PAGE}");
        ScreenReaderService.Speak(LocalizedText.UpdateAvailable(Format(result.Latest), Format(installed)), interrupt: false);
    }

    /// <summary>The version this DLL was built as (csproj Version, taken from CHANGELOG.md).</summary>
    private static Version Installed() => Normalize(typeof(UpdateChecker).Assembly.GetName().Version);

    private static bool TryParseTag(string tag, out Version version)
    {
        version = null;
        if (string.IsNullOrEmpty(tag) || !Version.TryParse(tag.TrimStart('v', 'V'), out var parsed)) return false;
        version = Normalize(parsed);
        return true;
    }

    // Tags are major.minor.patch; assembly versions add a fourth part that would break the comparison
    private static Version Normalize(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0));

    private static string Format(Version v) => v.ToString(3);
}
