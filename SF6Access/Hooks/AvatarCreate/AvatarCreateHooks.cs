using System;
using System.Collections.Generic;
using REFrameworkNET;
using SF6Access.Services;
using SF6Access.Services.Ui;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the World Tour character creator (UIFlowUI61000 + its
/// ~50 child flows: presets, body/face sliders, paints, colors, voice,
/// recipes, and the HLS color-picker popup).
///
/// ScreenAdapter with a CUSTOM _Handles walk in Locate(): one pass finds the
/// main param (UIFlowUI61000.Param) and the TOPMOST live child flow (handle
/// index 0 = newest, so the color popup wins over the flow under it). The
/// child flow is read generically by <see cref="AvatarChildFlowReader"/>;
/// applied model colors are watched by <see cref="AvatarColorWatcher"/>.
/// Category names come from the game's own localized message Guids
/// (Param.MainCategoryNameMessageId) with English fallbacks. The F11 avatar
/// dump lives in the Dump partial. Registered in ScreenRegistry.
/// </summary>
public sealed partial class AvatarCreateHooks : ScreenAdapter
{
    // The main param is matched by Contains on this fragment (namespace varies).
    private const string MAIN_PARAM_FRAGMENT = "UIFlowUI61000.Param";
    private static readonly string[] Types = { "app.worldtour.UIFlowUI61000.Param" };
    public override string[] OwnedTypes => Types;

    private static AvatarCreateHooks _self;

    /// <summary>Read by MainMenuHooks: mutes the generic focus/value readers
    /// while the creator's own readers own the screen (FocusValueHooks was
    /// double-speaking stale pool-cell numbers on page flips).</summary>
    public static bool IsInAvatarCreator => _self?.Active == true;

    public AvatarCreateHooks()
    {
        SearchInterval = 60;
        ReadInterval = 2;
        _self = this;
    }

    // ---- Tracked params ----
    private ManagedObject _avatarParam;
    private ManagedObject _childFlowParam;
    private string _lastChildFlowType = "";

    // ---- Readers ----
    private readonly AvatarChildFlowReader _childReader = new();
    private readonly AvatarColorWatcher _colorWatcher = new();

    // ---- State tracking ----
    private int _lastMainCategory = -1;
    private int _lastMiddleCategory = -1;
    private string _lastGuideDesc = "";
    private readonly Dictionary<int, string> _mainCategoryNameCache = new();
    private int _lastPaintSlotIndex = -1;
    private int _lastPaintTypeIndex = -1;

    // English fallbacks for when the localized Guid/table reads fail.
    private static readonly string[] MainCategoryFallback =
    {
        "Type", "Preset", "Body", "Face", "Body Paint",
        "Face Paint", "Color", "Voice", "Recipe"
    };

    private static readonly Dictionary<int, string[]> SubCategoryFallback = new()
    {
        [0] = new[] { "Body Type", "Gender Identity" },
        [1] = new[] { "Face Preset", "Body Preset", "Random", "Blend" },
        [2] = new[] { "Height", "Upper Body", "Lower Body", "Build", "Skin Color", "Body Hair" },
        [3] = new[] { "Face Shape", "Hair", "Eyes", "Pupils", "Eyelashes", "Eyebrows", "Nose", "Mouth", "Ears", "Beard", "Age", "Contour", "Expression" },
        [4] = new[] { "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5" },
        [5] = new[] { "Slot 1", "Slot 2", "Slot 3", "Slot 4", "Slot 5" },
        [6] = new[] { "Skin", "Body Hair", "Hair", "Pupils", "Eyelashes", "Eyebrows", "Beard", "Body Paint", "Face Paint" },
        [7] = new[] { "Voice" },
        [8] = new[] { "Recipe" },
    };

    #region Locate / lifecycle

    protected override bool Locate()
    {
        try
        {
            // One ordered pass over the flow handles (index 0 = newest). A
            // custom walk instead of FlowHelper's finders because dead handles
            // (IsEnd) linger and must be skipped, or a closed sub-menu's param
            // keeps being read (silent menu).
            var handles = GetFlowHandles();
            if (handles == null) return _avatarParam != null;
            int count = FlowHelper.GetListCount(handles);

            ManagedObject main = null;
            ManagedObject topChild = null;
            string topChildType = "";

            for (int i = 0; i < count && i < 50; i++)
            {
                ManagedObject param;
                string typeName;
                try
                {
                    var handle = FlowHelper.GetListItem(handles, i);
                    if (handle == null) continue;
                    if (FlowHelper.Call(handle, "get_IsEnd") is true) continue;
                    param = FlowHelper.Call(handle, "GetParam") as ManagedObject;
                    typeName = param?.GetTypeDefinition()?.FullName;
                }
                catch { continue; }
                if (param == null || typeName == null) continue;
                if (!typeName.StartsWith("app.worldtour.UIFlow") || !typeName.EndsWith(".Param")) continue;

                if (typeName.Contains(MAIN_PARAM_FRAGMENT))
                {
                    main ??= param;
                }
                else if (main == null && topChild == null)
                {
                    // Newest live creator flow seen BEFORE the main param in
                    // handle order = the topmost child (popup/sub-menu)
                    topChild = param;
                    topChildType = typeName;
                }
            }

            if (main == null)
            {
                _avatarParam = null;
                return false;
            }

            if (FlowHelper.AddressOf(main) != FlowHelper.AddressOf(_avatarParam))
            {
                _avatarParam = main;
                _mainCategoryNameCache.Clear();
                _colorWatcher.Bind(_avatarParam);
                API.LogInfo("[SF6Access] Avatar creator param bound");
            }

            // Re-bind the child reader when the child flow type OR instance changes
            if (topChildType != _lastChildFlowType ||
                FlowHelper.AddressOf(topChild) != FlowHelper.AddressOf(_childFlowParam))
            {
                _childFlowParam = topChild;
                _lastChildFlowType = topChildType;
                _childReader.Bind(topChild, topChildType);
                if (!string.IsNullOrEmpty(topChildType))
                    API.LogInfo($"[SF6Access] Avatar child flow: {topChildType}");
            }
            return true;
        }
        catch (Exception ex)
        {
            API.LogError($"[SF6Access] AvatarCreate Locate error: {ex.Message}");
            return _avatarParam != null;
        }
    }

    protected override void OnDeactivate()
    {
        _avatarParam = null;
        _childFlowParam = null;
        _lastChildFlowType = "";
        _lastMainCategory = -1;
        _lastMiddleCategory = -1;
        _lastGuideDesc = "";
        _lastPaintSlotIndex = -1;
        _lastPaintTypeIndex = -1;
        _mainCategoryNameCache.Clear();
        _childReader.Reset();
        _colorWatcher.Reset();
    }

    protected override void OnPoll()
    {
        if (_avatarParam == null) return;
        PollMainCategory();
        PollMiddleCategory();
        PollPaintLists();
        PollGuideDescription();
        _childReader.Poll();
        _colorWatcher.Poll();
    }

    #endregion

    #region Categories

    private void PollMainCategory()
    {
        try
        {
            int cat = FlowHelper.ReadIntField(_avatarParam, "CurrentMainCategory", -1);
            if (cat < 0) cat = FlowHelper.CallInt(_avatarParam, "get_CurrentMainCategory");
            if (cat < 0 || cat == _lastMainCategory) return;
            _lastMainCategory = cat;
            _lastMiddleCategory = -1;
            _lastPaintSlotIndex = -1;
            _lastPaintTypeIndex = -1;

            string name = ResolveMainCategoryName(cat);
            API.LogInfo($"[SF6Access] Avatar main category {cat}: {name}");
            Speak(name);
        }
        catch { }
    }

    /// <summary>Localized main-category name from the game's own Guid table
    /// (Param.MainCategoryNameMessageId), falling back to English.</summary>
    private string ResolveMainCategoryName(int cat)
    {
        if (_mainCategoryNameCache.TryGetValue(cat, out var cached)) return cached;

        string name = null;
        try
        {
            var guids = FlowHelper.GetObjectField(_avatarParam, "MainCategoryNameMessageId");
            if (guids != null &&
                FlowHelper.Call(guids, "Get", cat) is REFrameworkNET.ValueType guidVt)
            {
                name = FlowHelper.ResolveGuid(guidVt);
            }
        }
        catch { }

        if (string.IsNullOrEmpty(name))
            name = cat < MainCategoryFallback.Length ? MainCategoryFallback[cat] : $"Category {cat + 1}";

        _mainCategoryNameCache[cat] = name;
        return name;
    }

    private void PollMiddleCategory()
    {
        try
        {
            int mid = FlowHelper.ReadIntField(_avatarParam, "CurrentMiddleCategory", -1);
            if (mid < 0) mid = FlowHelper.CallInt(_avatarParam, "get_CurrentMiddleCategory");
            if (mid < 0 || mid == _lastMiddleCategory) return;
            _lastMiddleCategory = mid;

            // Prefer the on-screen row text of the middle list (localized);
            // paint categories use their own lists
            string listField = _lastMainCategory switch
            {
                4 => "PartsScrollListBodyPaintMiddleItem", // MainCategoryType.BODY_PAINT
                5 => "PartsScrollListFacePaintMiddleItem", // MainCategoryType.FACE_PAINT
                _ => "PartsScrollListMiddleItem",
            };
            var list = FlowHelper.GetObjectField(_avatarParam, listField);
            string name = FlowHelper.ReadSelectedItemText(list);

            if (string.IsNullOrEmpty(name) &&
                SubCategoryFallback.TryGetValue(_lastMainCategory, out var subs) &&
                mid >= 0 && mid < subs.Length)
            {
                name = subs[mid];
            }
            if (string.IsNullOrEmpty(name))
                name = $"{LangFile.Get("item", "Item")} {mid + 1}";

            API.LogInfo($"[SF6Access] Avatar sub-category {mid}: {name}");
            Speak(name);
        }
        catch { }
    }

    /// <summary>
    /// Body/Face Paint slot + swap lists on the MAIN param (the child paint
    /// flows only appear after entering a slot). Reads the focused row of the
    /// slot list and the paint-type list when their selection changes.
    /// </summary>
    private void PollPaintLists()
    {
        if (_lastMainCategory != 4 && _lastMainCategory != 5) return;
        try
        {
            bool isBody = _lastMainCategory == 4;
            var slotList = FlowHelper.GetObjectField(_avatarParam,
                isBody ? "PartsScrollListBodyPaintMiddleItem" : "PartsScrollListFacePaintMiddleItem");
            var typeList = FlowHelper.GetObjectField(_avatarParam,
                isBody ? "PartsSimpleListBodyPaintMiddleItem" : "PartsSimpleListFacePaintMiddleItem");

            PollListRow(slotList, ref _lastPaintSlotIndex, "paint slot");
            PollListRow(typeList, ref _lastPaintTypeIndex, "paint type");
        }
        catch { }
    }

    private static void PollListRow(ManagedObject list, ref int lastIndex, string logTag)
    {
        if (list == null) return;
        int idx = FlowHelper.CallInt(list, "get_SelectedIndex");
        if (idx < 0 || idx == lastIndex) return;
        bool first = lastIndex == -1;
        lastIndex = idx;
        if (first) return;

        // Empty paint slots render as "N ────────" dash runs — strip them
        string text = FlowHelper.ReadSelectedItemText(list);
        if (text != null)
            text = System.Text.RegularExpressions.Regex
                .Replace(text, @"[─—–\-]{2,}", "").Trim().TrimEnd('.').Trim();
        if (string.IsNullOrEmpty(text))
            text = $"{LangFile.Get("slot", "Slot")} {idx + 1}";
        else if (System.Text.RegularExpressions.Regex.IsMatch(text, @"^\d+$"))
            text = $"{LangFile.Get("slot", "Slot")} {text}. {LangFile.Get("empty", "Empty")}";
        API.LogInfo($"[SF6Access] Avatar {logTag} [{idx}]: {text}");
        ScreenReaderService.Speak(text, interrupt: true);
    }

    #endregion

    #region Guide description

    private void PollGuideDescription()
    {
        try
        {
            string desc = FlowHelper.ReadStringField(_avatarParam, "GuideDescriptionString") ?? "";
            if (desc == _lastGuideDesc || string.IsNullOrEmpty(desc)) return;
            _lastGuideDesc = desc;

            string cleaned = FlowHelper.CleanTags(desc);
            if (!string.IsNullOrEmpty(cleaned))
            {
                API.LogInfo($"[SF6Access] Avatar guide: {cleaned}");
                Speak(cleaned, false); // don't interrupt category announcements
            }
        }
        catch { }
    }

    #endregion
}
