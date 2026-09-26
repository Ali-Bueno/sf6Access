using System;
using REFrameworkNET;

namespace SF6Access.Services.WorldTour;

/// <summary>Nearest enabled interaction-shape point. Geometry, not proof of reachability.
/// GetNearPos belongs to AccessCheckShape, not the target component.</summary>
public static class AccessPointReader
{
    private const string SHAPE_TYPE = "app.worldtour.avatar.AvatarAccessTargetBase.AccessCheckShape";
    private static Method _getNearPos, _getFlag;
    private static FieldOutBuffer _out, _searcher;
    private static int _enabledFlag = -1;
    private static bool _tried, _logged, _callLogged;

    public static bool TryRead(ManagedObject accessTarget, ManagedObject targetObject,
        float px, float py, float pz, float tx, float ty, float tz,
        out float ax, out float ay, out float az)
    {
        ax = ay = az = 0f;
        if (accessTarget == null) return false;
        try
        {
            if (!Bind()) return false;
            if (MemberAccess.ReadFieldsFirst(accessTarget, "Enable_AllAccess", typeof(bool)) is not true
                || MemberAccess.ReadFieldsFirst(accessTarget, "Enable_DefaultAccess", typeof(bool)) is not true)
                return false;
            var go = FlowHelper.Call(accessTarget, "get_GameObject") as ManagedObject;
            var transform = FlowHelper.Call(go, "get_Transform") as ManagedObject;
            if (transform == null) return false;
            var setting = MemberAccess.ReadFieldsFirst(accessTarget, "CheckSetting");
            var shapes = MemberAccess.ReadFieldsFirst(setting, "ShapeData") as ManagedObject;
            var disabled = MemberAccess.ReadFieldsFirst(accessTarget, "DynamicDisableShapeIndexList") as ManagedObject;
            int count = FlowHelper.GetListCount(shapes);
            if (!Write(_searcher, px, py, pz)) return false;
            float best = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                if (FlowHelper.Call(disabled, "Contains", i) is true) continue;
                var shape = FlowHelper.GetListItem(shapes, i);
                if (shape == null || _getFlag.InvokeBoxed(typeof(bool), shape,
                    new object[] { _enabledFlag }) is not true) continue;
                // Reject partial writes, while allowing legitimate zero coordinates.
                if (!Write(_out, float.NaN, float.NaN, float.NaN)) continue;
                if (!_callLogged)
                {
                    _callLogged = true;
                    API.LogInfo("[SF6Access] Access point: calling enabled shape GetNearPos");
                }
                if (_getNearPos.InvokeBoxed(typeof(bool), shape,
                    new object[] { _out.View, transform, _searcher.View }) is not true) continue;
                float x = _out.Component("x"), y = _out.Component("y"), z = _out.Component("z");
                float dx = x - px, dy = y - py, dz = z - pz;
                float distanceSq = dx * dx + dy * dy + dz * dz;
                if (!float.IsFinite(distanceSq) || distanceSq >= best) continue;
                best = distanceSq; ax = x; ay = y; az = z;
            }
            if (!float.IsFinite(best)) return false;
            LogOnce(FormattableString.Invariant($"shape point ({ax:F2}, {ay:F2}, {az:F2}), {count} shapes"));
            return true;
        }
        catch (Exception ex) { LogOnce($"read failed: {ex.GetType().Name}: {ex.Message}"); return false; }
    }

    private static bool Bind()
    {
        if (_tried) return _getNearPos != null && _getFlag != null && _enabledFlag >= 0
            && _out != null && _searcher != null;
        _tried = true;
        var td = TDB.Get().FindType(SHAPE_TYPE);
        _getNearPos = FieldProbeService.FindByShape(td, "GetNearPos", 3, "vec3");
        _getFlag = td?.GetMethod("GetFlag");
        foreach (var (name, value) in FieldProbeService.ReadEnum(SHAPE_TYPE + ".OptionFlagType", false))
            if (name == "Enable") _enabledFlag = value;
        if (_getNearPos == null || _getFlag == null || _enabledFlag < 0)
        { LogOnce("AccessCheckShape binding unavailable"); return false; }
        var parameters = _getNearPos.GetParameters();
        if (parameters[1].Type?.FullName != "via.Transform"
            || parameters[2].Type?.FullName != parameters[0].Type?.FullName)
        { _getNearPos = null; LogOnce("unexpected GetNearPos signature"); return false; }
        _out ??= FieldOutBuffer.Acquire(parameters[0].Type);
        _searcher ??= FieldOutBuffer.Acquire(parameters[0].Type);
        API.LogInfo("[SF6Access] Access point: bound AccessCheckShape.GetNearPos");
        return _out != null && _searcher != null;
    }

    public static void Reset() { _getNearPos = _getFlag = null; _tried = _logged = _callLogged = false; }
    private static bool Write(FieldOutBuffer buffer, float x, float y, float z)
        => buffer.SetComponent("x", x) && buffer.SetComponent("y", y) && buffer.SetComponent("z", z);
    private static void LogOnce(string message)
    {
        if (_logged) return;
        _logged = true;
        API.LogInfo("[SF6Access] Access point: " + message);
    }
}
