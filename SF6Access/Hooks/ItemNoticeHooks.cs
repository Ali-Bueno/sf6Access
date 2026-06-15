using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;
using SF6Access.Services;

namespace SF6Access.Hooks;

/// <summary>
/// Announces "item obtained" toast notifications (cosmetics, titles, artwork
/// such as "SF Legacy: Kinu Nishimura"). app.UIPartsGetItemNotice.ShowNotice
/// pops the toast; its UIPartsItemLabel.mTextItemName holds the localized item
/// name. The label is populated during ShowNotice, so capture the notice in the
/// pre-hook and read the name a couple of frames later once it is filled.
/// </summary>
public class ItemNoticeHooks
{
    private static ManagedObject _pendingNotice;
    private static int _readDelay;
    private const int READ_DELAY_FRAMES = 2;
    private static int _retries;
    private static string _lastName;

    [PluginEntryPoint]
    public static void Initialize()
    {
        try
        {
            var td = TDB.Get().FindType("app.UIPartsGetItemNotice");
            var method = td?.GetMethod("ShowNotice") ??
                         td?.GetMethod("ShowNotice(app.udWTItemUserData, System.UInt32, System.Single, System.Single, System.Boolean)");
            if (method == null)
            {
                API.LogInfo("[SF6Access] UIPartsGetItemNotice.ShowNotice not found, item notices skipped");
                return;
            }

            var hook = method.AddHook(false);
            hook.AddPre(args =>
            {
                try
                {
                    _pendingNotice = ManagedObject.ToManagedObject(args[1]); // this
                    _readDelay = READ_DELAY_FRAMES;
                    _retries = 6;
                }
                catch { }
                return PreHookResult.Continue;
            });
            API.LogInfo("[SF6Access] ItemNoticeHooks initialized");
        }
        catch (System.Exception ex)
        {
            API.LogError($"[SF6Access] ItemNoticeHooks init failed: {ex.Message}");
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        if (_pendingNotice == null) return;
        if (--_readDelay > 0) return;

        try
        {
            var label = FlowHelper.GetObjectField(_pendingNotice, "mPartsItemLabel");
            var textObj = FlowHelper.GetObjectField(label, "mTextItemName");
            string name = FlowHelper.ReadGuiText(textObj);

            if (string.IsNullOrEmpty(name))
            {
                // Label may still be filling — retry a few frames before giving up
                if (--_retries > 0) { _readDelay = READ_DELAY_FRAMES; return; }
                _pendingNotice = null;
                return;
            }

            _pendingNotice = null;
            name = name.Trim();
            if (name == _lastName) return;
            _lastName = name;

            API.LogInfo($"[SF6Access] Item obtained: {name}");
            ScreenReaderService.Speak($"Obtained. {name}", interrupt: false);
        }
        catch { _pendingNotice = null; }
    }
}
