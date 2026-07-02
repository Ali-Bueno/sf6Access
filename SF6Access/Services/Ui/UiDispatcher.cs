using System.Collections.Generic;
using REFrameworkNET;
using REFrameworkNET.Attributes;
using REFrameworkNET.Callbacks;

namespace SF6Access.Services.Ui;

/// <summary>
/// The single LateUpdate callback that drives every registered
/// <see cref="ScreenAdapter"/>. Replaces the per-hook [Callback] + poll
/// lifecycle: adapters register once (see <see cref="ScreenRegistry"/>) and are
/// ticked here. Also answers "is a mapped screen currently active" so the
/// generic focus reader / fallback can stand down when a dedicated adapter owns
/// the screen. Legacy hooks that still own their own [Callback] run alongside
/// this untouched during the migration.
/// </summary>
public class UiDispatcher
{
    private static readonly List<ScreenAdapter> _adapters = new();
    private static int _frame;

    public static void Register(ScreenAdapter adapter)
    {
        if (adapter != null && !_adapters.Contains(adapter))
            _adapters.Add(adapter);
    }

    /// <summary>True when any registered adapter currently owns the screen.</summary>
    public static bool AnyAdapterActive
    {
        get
        {
            for (int i = 0; i < _adapters.Count; i++)
                if (_adapters[i].Active) return true;
            return false;
        }
    }

    [Callback(typeof(LateUpdateBehavior), CallbackType.Post)]
    public static void OnUpdate()
    {
        _frame++;
        for (int i = 0; i < _adapters.Count; i++)
            _adapters[i].Tick(_frame);
    }
}
