using System;
using System.Text.RegularExpressions;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

public class DialogHooks
{
    private static bool _wasDialogShowing;
    private static string _lastDialogText;
    private static ManagedObject _dialogMgr;
    private static int _frameCounter;
    private static bool _loggedOnce;

    [Callback(typeof(UpdateBehavior), CallbackType.Pre)]
    static void OnUpdate()
    {
        try
        {
            // Only check every 15 frames
            if (++_frameCounter % 15 != 0) return;

            CheckDialogState();
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] DialogHooks error: {ex.Message}");
        }
    }

    private static void CheckDialogState()
    {
        // Try to find DialogManager if we don't have it yet
        if (_dialogMgr == null)
        {
            try
            {
                _dialogMgr = API.GetManagedSingletonT<app.DialogManager>() as ManagedObject;
            }
            catch { }

            if (_dialogMgr == null)
            {
                try
                {
                    _dialogMgr = API.GetManagedSingleton("app.DialogManager");
                }
                catch { }
            }

            if (_dialogMgr == null)
            {
                if (!_loggedOnce && _frameCounter > 300)
                {
                    API.LogWarning("[SF6Access] DialogManager not found yet");
                    _loggedOnce = true;
                }
                return;
            }

            API.LogInfo("[SF6Access] DialogManager found!");
        }

        bool isShowing = false;
        try
        {
            var result = (_dialogMgr as IObject)?.Call("get_IsShowDialog");
            if (result is bool b) isShowing = b;
        }
        catch
        {
            _dialogMgr = null; // Invalidate if object is dead
            return;
        }

        if (isShowing && !_wasDialogShowing)
        {
            ReadDialogContent();
        }
        else if (!isShowing && _wasDialogShowing)
        {
            _lastDialogText = null;
        }

        _wasDialogShowing = isShowing;
    }

    private static void ReadDialogContent()
    {
        try
        {
            var lineData = (_dialogMgr as IObject)?.Call("GetEnableLineData") as ManagedObject;
            if (lineData == null)
                return;

            // Log all fields of the line data for research
            var td = lineData.GetTypeDefinition();
            API.LogInfo($"[SF6Access] Dialog LineData type: {td?.GetFullName()}");

            if (td != null)
            {
                var fields = td.GetFields();
                if (fields != null)
                {
                    foreach (var f in fields)
                    {
                        try
                        {
                            var val = lineData.GetField(f.Name);
                            string valStr = val?.ToString() ?? "null";
                            if (valStr.Length > 200) valStr = valStr.Substring(0, 200);
                            API.LogInfo($"[SF6Access]   DLD.{f.Name} = {valStr}");
                        }
                        catch { }
                    }
                }
            }

            // Try various paths to get dialog text
            string text = TryGetDialogText(lineData);
            if (!string.IsNullOrEmpty(text))
                AnnounceDialog(text);
            else
                API.LogInfo("[SF6Access] Dialog detected but no text found");
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] ReadDialogContent error: {ex.Message}");
        }
    }

    private static string TryGetDialogText(ManagedObject lineData)
    {
        string title = null;
        string message = null;

        // Direct field access
        try { title = lineData.GetField("_Title") as string; } catch { }
        try { message = lineData.GetField("_Message") as string; } catch { }
        try { message ??= lineData.GetField("mMessage") as string; } catch { }

        // Try nested objects
        if (string.IsNullOrEmpty(title) && string.IsNullOrEmpty(message))
        {
            foreach (var fieldName in new[] { "_DialogData", "mDialogData", "_SharingData", "SharingData" })
            {
                try
                {
                    var nested = lineData.GetField(fieldName) as ManagedObject;
                    if (nested == null) continue;

                    API.LogInfo($"[SF6Access] Found nested: {fieldName} -> {nested.GetTypeDefinition()?.GetFullName()}");

                    // Log its fields too
                    var ntd = nested.GetTypeDefinition();
                    if (ntd != null)
                    {
                        var nfields = ntd.GetFields();
                        if (nfields != null)
                        {
                            foreach (var f in nfields)
                            {
                                try
                                {
                                    var val = nested.GetField(f.Name);
                                    if (val is string s && !string.IsNullOrEmpty(s))
                                    {
                                        API.LogInfo($"[SF6Access]   {fieldName}.{f.Name} = {s}");
                                        if (string.IsNullOrEmpty(message)) message = s;
                                        else if (string.IsNullOrEmpty(title)) title = s;
                                    }
                                }
                                catch { }
                            }
                        }
                    }
                }
                catch { }
            }
        }

        title = CleanTags(title);
        message = CleanTags(message);

        if (!string.IsNullOrEmpty(title) && !string.IsNullOrEmpty(message))
            return $"{title}. {message}";
        return title ?? message;
    }

    private static void AnnounceDialog(string text)
    {
        if (string.IsNullOrEmpty(text) || text == _lastDialogText) return;
        _lastDialogText = text;
        API.LogInfo($"[SF6Access] Dialog: {text}");
        ScreenReaderService.Speak(text);
    }

    private static string CleanTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(Regex.Replace(text, @"<[^>]+>", "").Trim(), @"\s+", " ").Trim();
    }
}
