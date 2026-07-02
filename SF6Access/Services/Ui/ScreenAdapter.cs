using REFrameworkNET;

namespace SF6Access.Services.Ui;

/// <summary>
/// Base for a per-screen accessibility adapter driven by the central
/// <see cref="UiDispatcher"/> (one shared LateUpdate callback ticks every
/// registered adapter, instead of each screen owning its own [Callback]).
/// Packages the poll lifecycle every screen hook used to re-implement by hand:
/// a slow "is my screen present" search while inactive, and a faster read tick
/// once active. Subclasses declare which flow Param(s) they own and fill in the
/// activate/poll behavior; the reusable archetype readers (GroupFocusPoller,
/// ValueTextWatcher, TabWatcher, ChangeGate) do the actual reading.
/// </summary>
public abstract class ScreenAdapter
{
    /// <summary>How often (frames) to search for the screen while inactive, and
    /// to re-verify the Param while active.</summary>
    protected int SearchInterval = 60;

    /// <summary>How often (frames) to read focus/values while active.</summary>
    protected int ReadInterval = 5;

    /// <summary>Whether this adapter currently owns its screen. Instance-level;
    /// screens that must expose a static suppression flag (e.g. IsInStatusMenu,
    /// read by MainMenuHooks) delegate a static to their single registered
    /// instance's Active — kept off the name "IsActive" to avoid clashing with
    /// those statics.</summary>
    public bool Active { get; private set; }

    /// <summary>The flow-Param type FullNames this adapter owns. Used for
    /// dispatcher bookkeeping and (later) suppression of the generic reader.</summary>
    public abstract string[] OwnedTypes { get; }

    internal void Tick(int frame)
    {
        try
        {
            bool searchTick = frame % SearchInterval == 0;

            if (!Active)
            {
                if (!searchTick) return;
                if (Locate())
                {
                    Active = true;
                    OnActivate();
                }
                return;
            }

            if (searchTick && !Locate())
            {
                Active = false;
                OnDeactivate();
                return;
            }

            if (frame % ReadInterval == 0) OnPoll();
        }
        catch { }
    }

    /// <summary>Find and cache this screen's Param(s); return true when present.
    /// Called on every search tick — both to activate and, while active, to
    /// re-verify. Must handle the game recreating a Param (re-bind child caches
    /// when the instance changes).</summary>
    protected abstract bool Locate();

    protected virtual void OnActivate() { }
    protected virtual void OnDeactivate() { }
    protected abstract void OnPoll();

    protected static void Speak(string text, bool interrupt = true) =>
        ScreenReaderService.Speak(text, interrupt);
}

/// <summary>
/// The common case: an adapter bound to exactly one flow Param type. Handles
/// finding the Param and tracking the live instance across menu re-entries — the
/// game destroys and recreates Params, so <see cref="OnBind"/> re-caches child
/// widgets whenever the instance appears or changes. Subclasses implement OnBind
/// (cache children + announce entry info) and <see cref="Poll"/> (read focus/values).
/// </summary>
public abstract class SingleParamScreenAdapter : ScreenAdapter
{
    protected abstract string ParamType { get; }
    public override string[] OwnedTypes => new[] { ParamType };

    protected ManagedObject Param { get; private set; }

    protected sealed override bool Locate()
    {
        var current = FlowHelper.TrackFlowParam(ParamType, Param, out bool instanceChanged);
        if (current == null)
        {
            Param = null;
            return false;
        }
        if (instanceChanged)
        {
            Param = current;
            OnBind();   // (re)cache children from the live Param + announce entry
        }
        return true;
    }

    protected sealed override void OnActivate() { }   // binding/entry happen in OnBind

    protected sealed override void OnDeactivate()
    {
        Param = null;
        OnExit();
    }

    protected sealed override void OnPoll() => Poll();

    /// <summary>Cache child widgets from <see cref="Param"/> and announce entry
    /// info. Called when the screen opens and whenever its Param is recreated.</summary>
    protected abstract void OnBind();

    /// <summary>Screen closed — reset watchers/state.</summary>
    protected virtual void OnExit() { }

    /// <summary>Read focus/value changes (called every ReadInterval frames).</summary>
    protected abstract void Poll();
}
