using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces the training-mode frame data display. The in-battle HUD GUI
/// "ui11255" holds two ready-to-read localized lines (e_txt_up / e_txt_down),
/// e.g. "Início 7F/Total 25F/Vantagem 2F" (Startup/Total/Advantage). They read
/// "--" until a move is performed, then fill with numbers. Confirmed present in
/// every training autodump; no need for the uint-keyed widget dictionary.
/// </summary>
public class TrainingFrameDataHooks
{
    private const string FRAME_GUI = "ui11255";
    private static readonly string[] LineNames = { "e_txt_up", "e_txt_down" };

    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 6;

    private static readonly List<(string owner, ManagedObject view)> _views = new();
    private static readonly Dictionary<string, string> _lastByLine = new();

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] TrainingFrameDataHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (_views.Count == 0 ? _pollCounter % 120 == 0 : _pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            _views.Clear();
            foreach (var v in GuiTextReader.FindGuiViews(FRAME_GUI))
                _views.Add(v);
        }

        if (_views.Count == 0 || _pollCounter % POLL_READ_INTERVAL != 0) return;
        PollFrameData();
    }

    private static void PollFrameData()
    {
        foreach (var (owner, view) in _views)
        {
            try
            {
                foreach (var t in GuiTextReader.ReadViewTexts(view, owner))
                {
                    if (System.Array.IndexOf(LineNames, t.Name) < 0) continue;
                    if (string.IsNullOrWhiteSpace(t.Text)) continue;

                    string text = FlowHelper.CleanTags(t.Text).Trim();
                    // Only speak once a move filled in numbers (skip the "--" idle
                    // state and frames mid-animation that have no digits yet)
                    if (!ContainsDigit(text)) { _lastByLine[t.Name] = null; continue; }

                    _lastByLine.TryGetValue(t.Name, out string last);
                    if (text == last) continue;
                    _lastByLine[t.Name] = text;

                    API.LogInfo($"[SF6Access] Frame data [{t.Name}]: {text}");
                    ScreenReaderService.Speak(text, interrupt: false);
                }
            }
            catch { }
        }
    }

    private static bool ContainsDigit(string s)
    {
        foreach (char c in s) if (c >= '0' && c <= '9') return true;
        return false;
    }
}
