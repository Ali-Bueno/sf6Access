using System;
using System.Collections.Generic;
using REFrameworkNET;

namespace SF6Access.Services;

/// <summary>
/// Reads on-screen via.gui.Text contents by scanning the current scene's
/// via.gui.GUI components and walking their control trees with getChildren.
/// Used for screens whose text is not exposed through flow Param fields
/// (e.g. the startup caution screen). Expensive — call on demand only.
/// </summary>
public static class GuiTextReader
{
    private static object _guiRuntimeType;
    private static object _textRuntimeType;
    private static object _controlRuntimeType;
    private static Method _getChildrenMethod;
    private static Method _findComponentsMethod;
    private static bool _typesCached;

    private const int MAX_DEPTH = 12;
    private const int MAX_TEXTS = 300;

    public sealed class GuiText
    {
        public string Owner;   // GameObject name of the owning GUI component
        public string Name;    // PlayObject name of the text element
        public string Text;    // Displayed message
        public bool Visible;
    }

    private static void CacheTypes()
    {
        if (_typesCached) return;
        _typesCached = true;
        try
        {
            _guiRuntimeType = TDB.Get().FindType("via.gui.GUI")?.GetRuntimeType();
            _textRuntimeType = TDB.Get().FindType("via.gui.Text")?.GetRuntimeType();
            _controlRuntimeType = TDB.Get().FindType("via.gui.Control")?.GetRuntimeType();
            _getChildrenMethod = TDB.Get().FindType("via.gui.Control")?.GetMethod("getChildren(System.Type)");
            _findComponentsMethod = TDB.Get().FindType("via.Scene")?.GetMethod("findComponents(System.Type)");

            API.LogInfo($"[SF6Access] GuiTextReader cache: gui={_guiRuntimeType != null}, " +
                $"text={_textRuntimeType != null}, control={_controlRuntimeType != null}, " +
                $"getChildren={_getChildrenMethod != null}, findComponents={_findComponentsMethod != null}");
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] GuiTextReader type cache failed: {ex.Message}");
        }
    }

    private static ManagedObject GetChildren(ManagedObject control, object runtimeType)
    {
        if (_getChildrenMethod == null || control == null || runtimeType == null) return null;
        try
        {
            return _getChildrenMethod.InvokeBoxed(typeof(object), control,
                new object[] { runtimeType }) as ManagedObject;
        }
        catch { return null; }
    }

    /// <summary>Collect texts from all enabled GUI components in the current scene.</summary>
    public static List<GuiText> ReadSceneTexts(bool visibleOnly = true)
    {
        var results = new List<GuiText>();
        CacheTypes();
        if (_guiRuntimeType == null || _textRuntimeType == null || _controlRuntimeType == null)
            return results;

        try
        {
            var sceneMgr = API.GetNativeSingleton("via.SceneManager");
            var scene = (sceneMgr as IObject)?.Call("get_CurrentScene") as IObject;
            if (scene == null || _findComponentsMethod == null) return results;

            var guis = _findComponentsMethod.InvokeBoxed(typeof(object), scene,
                new object[] { _guiRuntimeType }) as ManagedObject;
            if (guis == null) return results;

            int count = FlowHelper.GetListCount(guis);
            for (int i = 0; i < count && results.Count < MAX_TEXTS; i++)
            {
                var gui = FlowHelper.GetListItem(guis, i);
                if (gui == null) continue;

                try
                {
                    var enabled = FlowHelper.Call(gui, "get_Enabled");
                    if (enabled is bool en && !en) continue;

                    string owner = null;
                    var gameObject = FlowHelper.Call(gui, "get_GameObject") as ManagedObject;
                    if (gameObject != null)
                        owner = FlowHelper.Call(gameObject, "get_Name") as string;

                    var view = FlowHelper.Call(gui, "get_View") as ManagedObject;
                    if (view == null) continue;

                    WalkControl(view, owner, visibleOnly, results, 0);
                }
                catch { }
            }
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] GuiTextReader scan failed: {ex.Message}");
        }
        return results;
    }

    /// <summary>
    /// Collect visible texts under a single via.gui.Control subtree (e.g. a focused
    /// list item). Small subtrees also resolve MessageId Guids for text elements
    /// whose Message string is empty (tab bars and some labels render that way).
    /// </summary>
    public static List<GuiText> ReadControlTexts(ManagedObject control, bool visibleOnly = true)
    {
        var results = new List<GuiText>();
        CacheTypes();
        if (control == null || _textRuntimeType == null || _controlRuntimeType == null)
            return results;

        try { WalkControl(control, null, visibleOnly, results, 0, resolveMessageIds: true); }
        catch { }
        return results;
    }

    /// <summary>Join the texts under a control into a single announcement string.</summary>
    public static string ReadControlTextJoined(ManagedObject control, bool visibleOnly = true)
    {
        var texts = ReadControlTexts(control, visibleOnly);
        if (texts.Count == 0) return null;

        var sb = new System.Text.StringBuilder();
        foreach (var t in texts)
        {
            if (string.IsNullOrWhiteSpace(t.Text)) continue;
            if (sb.Length > 0) sb.Append(". ");
            sb.Append(t.Text.Trim());
        }
        return sb.Length > 0 ? sb.ToString() : null;
    }

    /// <summary>Collect texts only from GUI components whose GameObject name contains the filter.</summary>
    public static List<GuiText> ReadTextsByOwner(string ownerContains, bool visibleOnly = true)
    {
        var all = ReadSceneTexts(visibleOnly);
        if (string.IsNullOrEmpty(ownerContains)) return all;

        var filtered = new List<GuiText>();
        foreach (var t in all)
        {
            if (t.Owner != null && t.Owner.Contains(ownerContains, StringComparison.OrdinalIgnoreCase))
                filtered.Add(t);
        }
        return filtered;
    }

    private static void WalkControl(ManagedObject control, string owner, bool visibleOnly,
        List<GuiText> results, int depth, bool resolveMessageIds = false)
    {
        if (control == null || depth > MAX_DEPTH || results.Count >= MAX_TEXTS) return;

        // Text elements directly under this control
        var texts = GetChildren(control, _textRuntimeType);
        int textCount = FlowHelper.GetListCount(texts);
        for (int i = 0; i < textCount && results.Count < MAX_TEXTS; i++)
        {
            var textObj = FlowHelper.GetListItem(texts, i);
            if (textObj == null) continue;

            try
            {
                bool visible = true;
                var vis = FlowHelper.Call(textObj, "get_Visible");
                if (vis is bool vb) visible = vb;
                if (visibleOnly && !visible) continue;

                string message = FlowHelper.Call(textObj, "get_Message") as string;

                // Some labels render via MessageId with an empty Message string
                if (string.IsNullOrWhiteSpace(message) && resolveMessageIds)
                {
                    var guidObj = FlowHelper.Call(textObj, "get_MessageId");
                    if (guidObj is REFrameworkNET.ValueType vt)
                        message = FlowHelper.ResolveGuid(vt);
                }
                if (string.IsNullOrWhiteSpace(message)) continue;

                results.Add(new GuiText
                {
                    Owner = owner,
                    Name = FlowHelper.Call(textObj, "get_Name") as string,
                    Text = FlowHelper.CleanTags(message),
                    Visible = visible
                });
            }
            catch { }
        }

        // Recurse into child controls
        var children = GetChildren(control, _controlRuntimeType);
        int childCount = FlowHelper.GetListCount(children);
        for (int i = 0; i < childCount && results.Count < MAX_TEXTS; i++)
        {
            var child = FlowHelper.GetListItem(children, i);
            if (child == null) continue;

            try
            {
                if (visibleOnly)
                {
                    var vis = FlowHelper.Call(child, "get_Visible");
                    if (vis is bool vb && !vb) continue;
                }
                WalkControl(child, owner, visibleOnly, results, depth + 1, resolveMessageIds);
            }
            catch { }
        }
    }
}
