using System;
using System.Globalization;
using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

public static partial class FieldNavPath
{
    // ---------- resolving the surface (steps 1-4, all zero-risk reads) ----------

    /// <summary>A live navigation agent whose map is loaded, or null with a reason. The
    /// only engine CALL here is <c>hasValidMap()</c>: no arguments, returns a bool.
    ///
    /// <para><b>Never cached.</b> The agent belongs to an NPC who can walk away, despawn or
    /// be destroyed by a section stream, and a managed wrapper outliving its object is the
    /// finalizer-on-the-GC-thread crash this mod has already paid for twice. It is re-asked
    /// per query, which the 250 ms rate limit keeps to a human cadence.</para></summary>
    private static ManagedObject ReadySurface()
    {
        try
        {
            Step(1, $"asking the scene for {SURFACE_TYPE} components — the PLAYER has none, " +
                    "the surface is an agent and the player is not navigated");
            var list = FieldProbeService.SceneComponents(SURFACE_TYPE);
            int n = FlowHelper.GetListCount(list);
            if (n == 0)
            {
                Fail($"no {SURFACE_TYPE} anywhere in the scene: nothing here is being navigated");
                return null;
            }

            for (int i = 0; i < n; i++)
            {
                var surface = FlowHelper.GetListItem(list, i);
                if (surface == null) continue;
                if (!Bind(surface)) return null;

                Step(2, $"calling via.navigation.Navigation.hasValidMap() on agent {i + 1} of {n}");
                if (_hasValidMap.InvokeBoxed(typeof(bool), surface, null) is bool valid && valid)
                    return surface;
            }

            // Agents exist but none is on a loaded map — a loading screen, or a city whose
            // navmesh has not streamed in. Nothing further to probe.
            Fail($"none of the {n} navigation agents in the scene reports a valid map");
            return null;
        }
        catch (Exception ex) { Fail($"{ex.GetType().Name}: {ex.Message}"); return null; }
    }

    // ---------- binding, by shape only ----------

    private static bool Bind(ManagedObject surface)
    {
        if (_bindTried) return _bound;
        _bindTried = true;
        var td = surface.GetTypeDefinition();

        _hasValidMap = Find(td, "hasValidMap", m => ParamCount(m) == 0);
        // Three parameters, value / value / reference: the ONLY overload of the six that
        // takes just the two endpoints and a report (NavigationSurface.cs:556). The four-
        // and six-parameter ones add a PathQueryInfo, an interrupt distance or node
        // priorities, and none of them is what this class means.
        _queryPathSync = Find(td, QUERY_PATH_SYNC, m =>
        {
            var ps = m.GetParameters();
            return ps != null && ps.Count == 3
                   && ps[0].Type?.IsValueType() == true && ps[1].Type?.IsValueType() == true
                   && ps[2].Type != null && !ps[2].Type.IsValueType();
        });

        if (_queryPathSync == null || _hasValidMap == null)
        {
            Fail($"{td?.GetFullName()} publishes no {QUERY_PATH_SYNC}(vec3, vec3, report) / hasValidMap()");
            return false;
        }

        var pars = _queryPathSync.GetParameters();
        _vecType = pars[0].Type;
        _reportType = pars[2].Type;
        _start ??= FieldOutBuffer.Acquire(_vecType);
        _end ??= FieldOutBuffer.Acquire(_vecType);
        if (_start == null || _end == null) { Fail(FieldOutBuffer.Refusal(_vecType)); return false; }

        _bound = true;
        return true;
    }

    /// <summary>A method by name plus an arbitrary SHAPE test, walking up from the concrete
    /// type to wherever it is declared — <c>hasValidMap</c> lives on
    /// <c>via.navigation.Navigation</c>, the surface's own parent.</summary>
    private static Method Find(TypeDefinition start, string name, Func<Method, bool> shape)
    {
        for (var td = start; td != null; td = td.ParentType)
        {
            var methods = td.GetMethods();
            if (methods == null) continue;
            foreach (var m in methods)
            {
                if (m.Name != name) continue;
                try { if (shape(m)) return m; } catch { }
            }
        }
        return null;
    }

    private static int ParamCount(Method m) => m.GetParameters()?.Count ?? -1;

}
