using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Accessibility for the fighter setting screen (costume, color, control type).
/// Reads values from Param cache fields (_cacheCostumeIndex, _cacheColorIndex)
/// and UIFlowUI10511 for controller type. Uses Group._FocusIndex for navigation.
/// </summary>
public class FighterSettingHooks
{
    private static bool _isActive;
    private static int _pollCounter;
    private const int POLL_SEARCH_INTERVAL = 60;
    private const int POLL_READ_INTERVAL = 5;

    // TDB cache
    private static Field _handlesField;
    private static Method _msgGetMethod;
    private static Method _getDefaultPresetNameMethod;
    private static Method _getBattlePresetNameMethod;
    private static Method _getFighterCostumesMethod;
    private static Method _getCostumeColorsMethod;
    private static Method _getItemNameMethod;

    // app.network.api.Enum.ItemCategory value for fighter costume colors
    // (verified via mail attachment log: cat=6 → "Elena: atuendo 1, color DX2")
    private const int ITEM_CATEGORY_COSTUME_COLOR = 6;
    private static Field _battlePresetCountField;
    private static bool _tdbCached;

    // Active param (the UIFlowUI10505.Param with mIsActive=True)
    private static ManagedObject _activeParam;
    private static int _activePlayerIndex;

    // Cached from active param
    private static ManagedObject _group;          // UIPartsGroup (has _FocusIndex)
    private static ManagedObject _spinsList;      // List<UIPartsSpin> (for count)
    private static ManagedObject _messDataArr;    // SpinText_MessageList[]
    private static int _spinCount;

    // Value tracking (read from Param fields, not spin Num)
    private static int _lastFocusIndex = -1;
    private static int _lastCostumeIdx = -1;
    private static int _lastColorIdx = -1;
    private static int _lastControllerType = -1;

    private static int _lastPresetIdx = -1;
    private static int _presetMax = -1;
    private static readonly Dictionary<int, string> _labelCache = new();

    public static bool IsInFighterSetting => _isActive;

    [PluginEntryPoint]
    public static void Initialize()
    {
        API.LogInfo("[SF6Access] FighterSettingHooks initialized");
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _pollCounter++;

        if (!_isActive)
        {
            if (_pollCounter % POLL_SEARCH_INTERVAL != 0) return;
            TryFindParam();
            return;
        }

        if (_pollCounter % POLL_SEARCH_INTERVAL == 0)
        {
            if (!RefreshActiveParam())
            {
                Reset();
                return;
            }
        }

        if (_pollCounter % POLL_READ_INTERVAL == 0)
        {
            PollFocusChange();
            PollValueChanges();
        }
    }

    // --- Param Discovery ---

    private static void CacheTDB()
    {
        if (_tdbCached) return;
        _tdbCached = true;
        _handlesField = TDB.Get().FindType("app.UIFlowManager")?.GetField("_Handles");
        _msgGetMethod = TDB.Get().FindType("via.gui.message")?.GetMethod("get(System.Guid)");
        _getDefaultPresetNameMethod = TDB.Get().FindType("app.InputAssign")?.GetMethod("GetDefaultBattlePresetName(System.Int32)");
        _getBattlePresetNameMethod = TDB.Get().FindType("app.UIKeyConfig.Utility")?.GetMethod("GetBattlePresetName(nBattle.TEAM.ID, app.EConfigInputType, System.Int32)");
        var tableMgrType = TDB.Get().FindType("app.TableDataManager");
        _getFighterCostumesMethod = tableMgrType?.GetMethod("GetFighterCostumes(System.UInt32)");
        _getCostumeColorsMethod = tableMgrType?.GetMethod("GetFighterCostumeColors(System.UInt32, System.Boolean)");
        var invType = TDB.Get().FindType("app.InventoryManager");
        _getItemNameMethod = invType?.GetMethod("GetName(app.network.api.Enum.ItemCategory, System.UInt32)")
            ?? invType?.GetMethod("GetName");
        _battlePresetCountField = TDB.Get().FindType("app.InputAssign")?.GetField("BattlePresetCount");
    }

    private static void TryFindParam()
    {
        CacheTDB();
        if (_handlesField == null) return;

        var activeParam = FindActiveParam(out int playerIndex);
        if (activeParam == null) return;

        ActivateWith(activeParam, playerIndex);
    }

    private static ManagedObject FindActiveParam(out int playerIndex)
    {
        playerIndex = -1;
        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) return null;

            var handles = _handlesField.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
            if (handles == null) return null;

            var countMethod = handles.GetTypeDefinition()?.GetMethod("get_Count");
            var getItemMethod = handles.GetTypeDefinition()?.GetMethod("get_Item(System.Int32)");
            if (countMethod == null || getItemMethod == null) return null;

            int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
            for (int i = 0; i < count && i < 50; i++)
            {
                try
                {
                    var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                    if (handle == null) continue;
                    var param = handle.GetField("<Param>k__BackingField") as ManagedObject;
                    if (param?.GetTypeDefinition()?.FullName != "app.UIFlowUI10505.Param") continue;

                    if (ReadBoolField(param, "mIsActive"))
                    {
                        playerIndex = ReadIntField(param, "mPlayerIndex");
                        return param;
                    }
                }
                catch { }
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Find UIFlowUI10511.Param matching our player and read controllerType + preset.
    /// </summary>
    private static (int controllerType, int preset) ReadFromUI10511()
    {
        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) return (-1, -1);

            var handles = _handlesField.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
            if (handles == null) return (-1, -1);

            var countMethod = handles.GetTypeDefinition()?.GetMethod("get_Count");
            var getItemMethod = handles.GetTypeDefinition()?.GetMethod("get_Item(System.Int32)");
            if (countMethod == null || getItemMethod == null) return (-1, -1);

            int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
            for (int i = 0; i < count && i < 50; i++)
            {
                try
                {
                    var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                    if (handle == null) continue;
                    var param = handle.GetField("<Param>k__BackingField") as ManagedObject;
                    if (param?.GetTypeDefinition()?.FullName != "app.UIFlowUI10511.Param") continue;

                    int pIdx = ReadIntField(param, "mPlayerIndex");
                    if (pIdx == _activePlayerIndex)
                        return (ReadIntField(param, "mControllerType"), ReadIntField(param, "mPreset"));
                }
                catch { }
            }
        }
        catch { }
        return (-1, -1);
    }

    private static bool RefreshActiveParam()
    {
        if (_activeParam != null && ReadBoolField(_activeParam, "mIsActive"))
            return true;

        var newActive = FindActiveParam(out int playerIndex);
        if (newActive != null)
        {
            ActivateWith(newActive, playerIndex);
            return true;
        }

        return ExistsInHandles();
    }

    private static bool ExistsInHandles()
    {
        try
        {
            var flowMgr = API.GetManagedSingleton("app.UIFlowManager");
            if (flowMgr == null) return false;
            var handles = _handlesField?.GetDataBoxed(typeof(object), flowMgr.GetAddress(), false) as ManagedObject;
            if (handles == null) return false;
            var countMethod = handles.GetTypeDefinition()?.GetMethod("get_Count");
            var getItemMethod = handles.GetTypeDefinition()?.GetMethod("get_Item(System.Int32)");
            if (countMethod == null || getItemMethod == null) return false;
            int count = Convert.ToInt32(countMethod.InvokeBoxed(typeof(int), handles, Array.Empty<object>()));
            for (int i = 0; i < count && i < 50; i++)
            {
                try
                {
                    var handle = getItemMethod.InvokeBoxed(typeof(object), handles, new object[] { i }) as ManagedObject;
                    if (handle == null) continue;
                    var param = handle.GetField("<Param>k__BackingField") as ManagedObject;
                    if (param?.GetTypeDefinition()?.FullName == "app.UIFlowUI10505.Param")
                        return true;
                }
                catch { }
            }
        }
        catch { }
        return false;
    }

    private static void ActivateWith(ManagedObject param, int playerIndex)
    {
        _activeParam = param;
        _activePlayerIndex = playerIndex;
        _isActive = true;

        // Cache child objects
        _group = GetField(param, "Group");
        _spinsList = GetField(param, "Spins");
        _messDataArr = GetField(param, "mMessData");
        _labelCache.Clear();

        // Use Spins list count (reflects actual visible spins including preset)
        _spinCount = GetListCount(_spinsList);
        int messCount = GetArrayLength(_messDataArr);
        if (_spinCount < messCount) _spinCount = messCount;

        // Initialize current values (don't announce on first read)
        _lastCostumeIdx = ReadIntField(param, "_cacheCostumeIndex");
        _lastColorIdx = ReadIntField(param, "_cacheColorIndex");
        _lastControllerType = ReadFromUI10511().controllerType;
        _presetMax = -1;
        _lastPresetIdx = _spinCount > 3 ? ReadPresetIndex() : -1;

        // Read initial focus
        int focusIdx = _group != null ? ReadIntField(_group, "_FocusIndex") : 0;
        bool focusChanged = focusIdx != _lastFocusIndex;
        _lastFocusIndex = focusIdx;

        if (focusChanged)
            AnnounceCurrentItem();

        API.LogInfo($"[SF6Access] FighterSetting active (player={playerIndex}, spins={_spinCount}, messData={GetArrayLength(_messDataArr)}, costume={_lastCostumeIdx}, color={_lastColorIdx}, ctrl={_lastControllerType}, preset={_lastPresetIdx})");
    }

    // --- Focus & Value Polling ---

    private static void PollFocusChange()
    {
        if (_group == null) return;

        int focusIdx = ReadIntField(_group, "_FocusIndex");
        if (focusIdx < 0 || focusIdx >= _spinCount) return;
        if (focusIdx == _lastFocusIndex) return;

        _lastFocusIndex = focusIdx;
        AnnounceCurrentItem();
    }

    private static void PollValueChanges()
    {
        if (_activeParam == null) return;

        // Costume (spin 0) — read from Param cache field
        int costumeIdx = ReadIntField(_activeParam, "_cacheCostumeIndex");
        if (costumeIdx >= 0 && costumeIdx != _lastCostumeIdx)
        {
            _lastCostumeIdx = costumeIdx;
            string text = WithName(
                WithLabel(0, ReadValueFromTextList(0, costumeIdx)),
                GetCostumeName(costumeIdx));
            API.LogInfo($"[SF6Access] Costume changed: {text} (idx={costumeIdx})");
            ScreenReaderService.Speak(text);
        }

        // Color (spin 1) — read from Param cache field
        int colorIdx = ReadIntField(_activeParam, "_cacheColorIndex");
        if (colorIdx >= 0 && colorIdx != _lastColorIdx)
        {
            _lastColorIdx = colorIdx;
            string text = WithName(
                WithLabel(1, ReadValueFromTextList(1, colorIdx)),
                GetColorName(_lastCostumeIdx, colorIdx));
            API.LogInfo($"[SF6Access] Color changed: {text} (idx={colorIdx})");
            ScreenReaderService.Speak(text);
        }

        // Controller type + Preset — both from UIFlowUI10511 handle
        var ui10511 = ReadFromUI10511();

        if (ui10511.controllerType >= 0 && ui10511.controllerType != _lastControllerType)
        {
            _lastControllerType = ui10511.controllerType;
            _presetMax = -1; // Invalidate — different control types may have different presets
            string text = WithLabel(2, ReadValueFromTextList(2, ui10511.controllerType));
            API.LogInfo($"[SF6Access] Control type changed: {text} (type={ui10511.controllerType})");
            ScreenReaderService.Speak(text);
        }

        // Preset — read from spin, capped to BattleStyleDataList range
        if (_spinCount > 3)
        {
            int preset = ReadPresetIndex();
            if (preset >= 0 && preset != _lastPresetIdx)
            {
                _lastPresetIdx = preset;
                string text = ReadPresetText(preset);
                API.LogInfo($"[SF6Access] Preset changed: {text} (idx={preset})");
                ScreenReaderService.Speak(text);
            }
        }
    }

    /// <summary>
    /// Prefix a value with its localized spin label ("Costume 2" instead of "2"),
    /// so bare numbers are meaningful when only the value changes.
    /// </summary>
    private static string WithLabel(int spinIndex, string valueText)
    {
        if (string.IsNullOrEmpty(valueText)) return valueText;

        string label = ReadSpinLabel(spinIndex);
        if (string.IsNullOrEmpty(label)) return valueText;

        return $"{label} {valueText}";
    }

    // --- Costume / Color Names (TableDataManager) ---

    private static readonly Dictionary<string, string> _costumeNameCache = new();

    private static bool _recordsLogged;

    /// <summary>
    /// Find the costume record matching the game's cached costume ID
    /// (_cacheCostumeId, exact) or the 1-based costumeNo. No positional
    /// fallback: the table may have gaps and would yield wrong names.
    /// </summary>
    private static ManagedObject FindCostumeRecord(int costumeIdx)
    {
        if (_getFighterCostumesMethod == null || _activeParam == null) return null;
        try
        {
            int fighterId = ReadIntField(_activeParam, "_FighterId");
            if (fighterId < 0) return null;

            var tableMgr = API.GetManagedSingleton("app.TableDataManager");
            if (tableMgr == null) return null;

            var list = _getFighterCostumesMethod.InvokeBoxed(
                typeof(object), tableMgr, new object[] { (uint)fighterId }) as ManagedObject;
            if (list == null) return null;

            int cacheCostumeId = ReadIntField(_activeParam, "_cacheCostumeId");
            int count = GetListCount(list);

            if (!_recordsLogged)
            {
                _recordsLogged = true;
                for (int i = 0; i < count; i++)
                {
                    var r = GetListItem(list, i);
                    API.LogInfo($"[SF6Access] CostumeTable[{i}]: id={ReadIntField(r, "id")}, " +
                        $"manageId={ReadIntField(r, "ManageId")}, costumeNo={ReadIntField(r, "costumeNo")}");
                }
                API.LogInfo($"[SF6Access] cacheCostumeId={cacheCostumeId}, costumeIdx={costumeIdx}");
            }

            // costumeNo is 0-BASED (verified via CostumeTable log: no=0,1,2,4)
            ManagedObject byNo = null;
            ManagedObject byPosition = null;
            for (int i = 0; i < count; i++)
            {
                var rec = GetListItem(list, i);
                if (rec == null) continue;
                if (i == costumeIdx) byPosition = rec;

                // Exact match against the game's own cached costume id
                if (cacheCostumeId > 0 &&
                    (ReadIntField(rec, "id") == cacheCostumeId || ReadIntField(rec, "ManageId") == cacheCostumeId))
                    return rec;

                if (ReadIntField(rec, "costumeNo") == costumeIdx)
                    byNo = rec;
            }
            return byNo ?? byPosition;
        }
        catch { }
        return null;
    }

    /// <summary>Resolve the localized costume name via its messageId GUID.</summary>
    private static string GetCostumeName(int costumeIdx)
    {
        string cacheKey = $"c{ReadIntField(_activeParam, "_FighterId")}_{costumeIdx}";
        if (_costumeNameCache.TryGetValue(cacheKey, out var cached)) return cached;

        string name = null;
        try
        {
            var rec = FindCostumeRecord(costumeIdx);
            var msg = GetField(rec, "messageId");
            if (msg != null)
            {
                var td = msg.GetTypeDefinition();
                var guidField = td?.GetField("GUID");
                if (guidField != null)
                {
                    var raw = guidField.GetDataBoxed(typeof(Guid), msg.GetAddress(), false);
                    if (raw is REFrameworkNET.ValueType vt)
                        name = CleanTags(ResolveGuid(vt));
                }
            }
            API.LogInfo($"[SF6Access] Costume record [{costumeIdx}]: name='{name}', isShowName={ReadBoolField(rec, "isShowName")}");
        }
        catch { }

        _costumeNameCache[cacheKey] = name;
        return name;
    }

    /// <summary>Resolve the color name string from the costume's color records.</summary>
    private static string GetColorName(int costumeIdx, int colorIdx)
    {
        if (_getCostumeColorsMethod == null) return null;
        string cacheKey = $"k{ReadIntField(_activeParam, "_FighterId")}_{costumeIdx}_{colorIdx}";
        if (_costumeNameCache.TryGetValue(cacheKey, out var cached)) return cached;

        string name = null;
        try
        {
            var rec = FindCostumeRecord(costumeIdx);
            int costumeId = ReadIntField(rec, "id");
            if (costumeId < 0) return null;

            var tableMgr = API.GetManagedSingleton("app.TableDataManager");
            if (tableMgr == null) return null;

            int cacheColorId = ReadIntField(_activeParam, "_cacheColorId");

            foreach (bool isDefault in new[] { true, false })
            {
                var list = _getCostumeColorsMethod.InvokeBoxed(
                    typeof(object), tableMgr, new object[] { (uint)costumeId, isDefault }) as ManagedObject;
                int count = GetListCount(list);
                for (int i = 0; i < count; i++)
                {
                    var colorRec = GetListItem(list, i);
                    if (colorRec == null) continue;

                    // Exact match against the game's cached color id; fallback to 0-based colorNo
                    bool exact = cacheColorId > 0 &&
                        (ReadIntField(colorRec, "id") == cacheColorId || ReadIntField(colorRec, "ManageId") == cacheColorId);
                    if (!exact && ReadIntField(colorRec, "colorNo") != colorIdx) continue;

                    // The record's `name` field is the internal (Japanese) id — resolve
                    // the localized shop item name instead and keep its color segment
                    name = ResolveColorItemName(ReadIntField(colorRec, "ManageId"))
                        ?? ResolveColorItemName(ReadIntField(colorRec, "id"));
                    API.LogInfo($"[SF6Access] Color record [{costumeIdx},{colorIdx}]: item name='{name}', exact={exact}");
                    if (exact || name != null) break;
                }
                if (name != null) break;
            }
        }
        catch { }

        _costumeNameCache[cacheKey] = name;
        return name;
    }

    /// <summary>
    /// Localized color item name via InventoryManager ("Luke: outfit 1, color DX2"),
    /// keeping only the final color segment. Returns null for unnamed colors.
    /// </summary>
    private static string ResolveColorItemName(int itemId)
    {
        if (_getItemNameMethod == null || itemId <= 0) return null;
        try
        {
            var invMgr = API.GetManagedSingleton("app.InventoryManager");
            if (invMgr == null) return null;

            var full = _getItemNameMethod.InvokeBoxed(typeof(string), invMgr,
                new object[] { ITEM_CATEGORY_COSTUME_COLOR, (uint)itemId }) as string;
            if (string.IsNullOrWhiteSpace(full)) return null;

            full = CleanTags(full);
            int comma = full.LastIndexOf(',');
            string segment = comma >= 0 ? full.Substring(comma + 1).Trim() : full;
            return string.IsNullOrEmpty(segment) ? null : segment;
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Append the costume/color name to a value text when available.
    /// Skips redundant names ("Atuendo 2: Atuendo 2" → "Atuendo 2").
    /// </summary>
    private static string WithName(string text, string name)
    {
        if (string.IsNullOrEmpty(name)) return text;
        if (string.IsNullOrEmpty(text)) return name;

        string normText = Regex.Replace(text, @"[\s:0]+", "").ToLowerInvariant();
        string normName = Regex.Replace(name, @"[\s:0]+", "").ToLowerInvariant();
        if (normText == normName || normText.EndsWith(normName)) return text;

        return $"{text}: {name}";
    }

    private static void AnnounceCurrentItem()
    {
        if (_lastFocusIndex < 0 || _lastFocusIndex >= _spinCount) return;

        string label = ReadSpinLabel(_lastFocusIndex);
        string valueText;

        if (_lastFocusIndex <= 2)
        {
            int valueIdx = GetCurrentValueForSpin(_lastFocusIndex);
            valueText = valueIdx >= 0 ? ReadValueFromTextList(_lastFocusIndex, valueIdx) : null;

            // Append costume/color names when available
            if (valueIdx >= 0 && _lastFocusIndex == 0)
                valueText = WithName(valueText, GetCostumeName(valueIdx));
            else if (valueIdx >= 0 && _lastFocusIndex == 1)
                valueText = WithName(valueText, GetColorName(_lastCostumeIdx, valueIdx));
        }
        else if (_lastFocusIndex == 3)
        {
            int preset = ReadPresetIndex();
            if (preset >= 0) _lastPresetIdx = preset;
            valueText = _lastPresetIdx >= 0 ? ReadPresetText(_lastPresetIdx) : null;
        }
        else
        {
            valueText = null;
        }

        var parts = new List<string>();
        if (!string.IsNullOrEmpty(label)) parts.Add(label);
        if (!string.IsNullOrEmpty(valueText)) parts.Add(valueText);

        string announcement = parts.Count > 0 ? string.Join(": ", parts) : $"Item {_lastFocusIndex}";

        API.LogInfo($"[SF6Access] FighterSetting [{_lastFocusIndex}]: {announcement}");
        ScreenReaderService.Speak(announcement);
    }

    private static int GetCurrentValueForSpin(int spinIndex)
    {
        return spinIndex switch
        {
            0 => _lastCostumeIdx >= 0 ? _lastCostumeIdx : ReadIntField(_activeParam, "_cacheCostumeIndex"),
            1 => _lastColorIdx >= 0 ? _lastColorIdx : ReadIntField(_activeParam, "_cacheColorIndex"),
            2 => _lastControllerType >= 0 ? _lastControllerType : ReadFromUI10511().controllerType,
            _ => -1
        };
    }

    // --- Value Resolution from TextList ---

    private static string ReadValueFromTextList(int spinIndex, int valueIndex)
    {
        string resolved = TryResolveValueText(spinIndex, valueIndex);
        if (!string.IsNullOrEmpty(resolved))
            return resolved;

        resolved = TryReadSpinDisplayedText(spinIndex);
        if (!string.IsNullOrEmpty(resolved))
            return resolved;

        return (valueIndex + 1).ToString();
    }

    /// <summary>
    /// Try to resolve localized value text from mMessData[spinIndex].TextList[valueIndex] Guid.
    /// Returns null if not available.
    /// </summary>
    private static string TryResolveValueText(int spinIndex, int valueIndex)
    {
        if (_messDataArr == null || _msgGetMethod == null) return null;
        try
        {
            var msgEntry = GetArrayElement(_messDataArr, spinIndex);
            if (msgEntry == null) return null;

            var textList = GetField(msgEntry, "TextList");
            if (textList == null) return null;

            int listCount = GetListCount(textList);
            if (valueIndex < 0 || valueIndex >= listCount) return null;

            var guidObj = (textList as IObject)?.Call("get_Item", valueIndex);
            if (guidObj is REFrameworkNET.ValueType guidVt)
            {
                string resolved = CleanTags(ResolveGuid(guidVt));
                if (!string.IsNullOrEmpty(resolved))
                    return resolved;
            }
        }
        catch { }
        return null;
    }

    /// <summary>
    /// Try to read the displayed text from a spin's _numText component (via.gui.Text).
    /// Reads what's actually shown on screen to sighted players.
    /// </summary>
    private static string TryReadSpinDisplayedText(int spinIndex)
    {
        if (_spinsList == null) return null;
        try
        {
            var spin = GetListItem(_spinsList, spinIndex);
            if (spin == null) return null;

            // Try field access first, then property method call
            var numText = GetField(spin, "_numText");
            if (numText == null)
            {
                try { numText = (spin as IObject)?.Call("get__numText") as ManagedObject; } catch { }
            }
            if (numText == null) return null;

            return CleanTags(ReadTextComponent(numText));
        }
        catch { }
        return null;
    }

    private static string ReadTextComponent(ManagedObject textObj)
    {
        if (textObj == null) return null;
        foreach (var m in new[] { "get_Message", "get_Text", "get_String" })
        {
            try
            {
                var text = (textObj as IObject)?.Call(m) as string;
                if (!string.IsNullOrEmpty(text)) return text.Trim();
            }
            catch { }
        }
        return null;
    }

    // --- Label Resolution ---

    private static string ReadSpinLabel(int index)
    {
        if (_labelCache.TryGetValue(index, out string cached))
            return cached;

        // Try cached mMessData first, then re-read fresh from Param
        foreach (var messData in new[] { _messDataArr, GetField(_activeParam, "mMessData") })
        {
            if (messData == null || _msgGetMethod == null) continue;
            try
            {
                int len = GetArrayLength(messData);
                if (index >= len) continue;

                var msgEntry = GetArrayElement(messData, index);
                if (msgEntry == null) continue;

                var td = msgEntry.GetTypeDefinition();
                var textField = td?.GetField("<Text>k__BackingField") ?? td?.GetField("Text");
                if (textField == null) continue;

                var raw = textField.GetDataBoxed(typeof(Guid), msgEntry.GetAddress(), false);
                if (raw is REFrameworkNET.ValueType vt)
                {
                    string label = CleanTags(ResolveGuid(vt));
                    if (!string.IsNullOrEmpty(label))
                    {
                        _labelCache[index] = label;
                        return label;
                    }
                }
            }
            catch { }
        }

        string[] fallbacks = { "Costume", "Color", "Control Type", "Preset" };
        string fb = index < fallbacks.Length ? fallbacks[index] : $"Setting {index}";
        _labelCache[index] = fb;
        return fb;
    }

    // --- Utilities ---

    /// <summary>
    /// Read spin[index] value, wrapped to [MinNum, MaxNum] range via method calls.
    /// </summary>
    private static int ReadSpinWrapped(int spinIndex)
    {
        if (_spinsList == null) return -1;
        try
        {
            var spin = GetListItem(_spinsList, spinIndex);
            if (spin == null) return -1;

            // Read _Num via field (method call unreliable for IL2CPP)
            var td = spin.GetTypeDefinition();
            int num = -1;
            if (td != null)
            {
                foreach (var name in new[] { "<_Num>k__BackingField", "_Num" })
                {
                    var f = td.GetField(name);
                    if (f != null) { num = Convert.ToInt32(f.GetDataBoxed(typeof(int), spin.GetAddress(), false)); break; }
                }
            }
            if (num < 0) return -1;

            // Read MinNum/MaxNum via method calls (fields have unpredictable names)
            int min = 0, max = -1;
            try { var r = (spin as IObject)?.Call("get_MinNum"); if (r != null) min = Convert.ToInt32(r); } catch { }
            try { var r = (spin as IObject)?.Call("get_MaxNum"); if (r != null) max = Convert.ToInt32(r); } catch { }

            if (max < 0) return num; // Can't wrap without MaxNum

            // Modulo wrap
            int range = max - min + 1;
            if (range > 0)
                return ((num - min) % range + range) % range;

            return num - min;
        }
        catch { }
        return -1;
    }

    /// <summary>
    /// Read preset index via Param.getPreset() (real-time), fallback to spin capped by BattlePresetCount.
    /// </summary>
    private static int ReadPresetIndex()
    {
        // Param.getPreset() tracks real-time navigation
        if (_activeParam != null)
        {
            try
            {
                var result = (_activeParam as IObject)?.Call("getPreset");
                if (result != null) return Convert.ToInt32(result);
            }
            catch { }
        }

        // Fallback: spin value capped to actual count
        int raw = ReadSpinWrapped(3);
        if (raw < 0) return -1;

        int max = GetPresetCount();
        if (max > 0)
            return raw % max;
        return raw;
    }

    private static int GetPresetCount()
    {
        if (_presetMax > 0) return _presetMax;

        if (_battlePresetCountField != null)
        {
            try
            {
                var val = _battlePresetCountField.GetDataBoxed(typeof(int), 0, false);
                if (val != null)
                {
                    _presetMax = Convert.ToInt32(val);
                    API.LogInfo($"[SF6Access] BattlePresetCount = {_presetMax}");
                    if (_presetMax > 0) return _presetMax;
                }
            }
            catch (Exception ex)
            {
                API.LogError($"[SF6Access] BattlePresetCount read error: {ex.Message}");
            }
        }
        return -1;
    }

    /// <summary>
    /// Resolve preset name: UIKeyConfig.Utility.GetBattlePresetName returns the
    /// custom/localized name; fall back to InputAssign default name.
    /// </summary>
    private static string ReadPresetText(int presetIndex)
    {
        // 1. UIKeyConfig.Utility.GetBattlePresetName(teamId, inputType, presetIndex)
        if (_getBattlePresetNameMethod != null)
        {
            try
            {
                int teamId = ReadIntField(_activeParam, "mTeamId");
                int inputType = _lastControllerType >= 0 ? _lastControllerType : 0;
                var name = _getBattlePresetNameMethod.InvokeBoxed(
                    typeof(string), null, new object[] { teamId, inputType, presetIndex }) as string;
                if (!string.IsNullOrEmpty(name))
                    return CleanTags(name);
            }
            catch { }
        }

        // 2. InputAssign.GetDefaultBattlePresetName — localized default name
        if (_getDefaultPresetNameMethod != null)
        {
            try
            {
                var name = _getDefaultPresetNameMethod.InvokeBoxed(
                    typeof(string), null, new object[] { presetIndex }) as string;
                if (!string.IsNullOrEmpty(name))
                    return CleanTags(name);
            }
            catch { }
        }

        // 3. Spin's displayed text (_numText via.gui.Text)
        string spinText = TryReadSpinDisplayedText(3);
        if (!string.IsNullOrEmpty(spinText))
            return spinText;

        return $"Preset {presetIndex + 1}";
    }

    private static string CleanTags(string text)
    {
        if (string.IsNullOrEmpty(text)) return text;
        return Regex.Replace(text, @"<[^>]+>", "").Trim();
    }

    private static ManagedObject GetField(ManagedObject obj, string name)
    {
        if (obj == null) return null;
        try { return obj.GetField(name) as ManagedObject; } catch { }
        try { return obj.GetField($"<{name}>k__BackingField") as ManagedObject; } catch { }
        return null;
    }

    private static int ReadIntField(ManagedObject obj, string name)
    {
        if (obj == null) return -1;
        try
        {
            var td = obj.GetTypeDefinition();
            var field = td?.GetField($"<{name}>k__BackingField") ?? td?.GetField(name);
            if (field != null)
                return Convert.ToInt32(field.GetDataBoxed(typeof(int), obj.GetAddress(), false));
        }
        catch { }
        return -1;
    }

    private static bool ReadBoolField(ManagedObject obj, string name)
    {
        if (obj == null) return false;
        try
        {
            var td = obj.GetTypeDefinition();
            var field = td?.GetField(name) ?? td?.GetField($"<{name}>k__BackingField");
            if (field != null)
            {
                var raw = field.GetDataBoxed(typeof(bool), obj.GetAddress(), false);
                return raw != null && Convert.ToBoolean(raw);
            }
        }
        catch { }
        return false;
    }

    private static int GetListCount(ManagedObject list)
    {
        if (list == null) return 0;
        try
        {
            var result = (list as IObject)?.Call("get_Count");
            if (result != null) return Convert.ToInt32(result);
        }
        catch { }
        return 0;
    }

    private static int GetArrayLength(ManagedObject arr)
    {
        if (arr == null) return 0;
        try
        {
            var result = (arr as IObject)?.Call("get_Length");
            if (result != null) return Convert.ToInt32(result);
        }
        catch { }
        return GetListCount(arr);
    }

    private static ManagedObject GetListItem(ManagedObject list, int index)
    {
        if (list == null) return null;
        try { return (list as IObject)?.Call("get_Item", index) as ManagedObject; }
        catch { return null; }
    }

    private static ManagedObject GetArrayElement(ManagedObject arr, int index)
    {
        if (arr == null) return null;
        try { return (arr as IObject)?.Call("Get", index) as ManagedObject; }
        catch { return null; }
    }

    private static string ResolveGuid(REFrameworkNET.ValueType guidVt)
    {
        if (_msgGetMethod == null || guidVt == null) return null;

        ulong vtAddr = guidVt.GetAddress();
        if (vtAddr == 0) return null;

        bool allZero = true;
        for (int i = 0; i < 16; i++)
        {
            if (Marshal.ReadByte((IntPtr)(long)(vtAddr + (ulong)i)) != 0)
            { allZero = false; break; }
        }
        if (allZero) return null;

        try
        {
            var task = Task.Run(() =>
            {
                try { return _msgGetMethod.InvokeBoxed(typeof(string), null, new object[] { guidVt }) as string; }
                catch { return null; }
            });

            if (task.Wait(TimeSpan.FromMilliseconds(200)))
                return task.Result?.Trim();
        }
        catch { }
        return null;
    }

    private static void Reset()
    {
        API.LogInfo("[SF6Access] FighterSetting ended");
        _isActive = false;
        _activeParam = null;
        _group = null;
        _spinsList = null;
        _messDataArr = null;
        _presetMax = -1;
        _spinCount = 0;
        _lastFocusIndex = -1;
        _lastCostumeIdx = -1;
        _lastColorIdx = -1;
        _lastControllerType = -1;
        _lastPresetIdx = -1;
        _labelCache.Clear();
        _costumeNameCache.Clear();
        _recordsLogged = false;
    }
}
