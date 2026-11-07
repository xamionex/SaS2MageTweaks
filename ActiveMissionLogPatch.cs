using HarmonyLib;
using ProjectMage.gamestate;

namespace SaS2MageTweaks;

/// Logs every change of GameSession.activeMission together with the code path that caused it.
/// This answers from a single play session whether entering a map force-assigns a mission or a selection flow (portal rune match, mission menu, network packet, save restore) set it.
[HarmonyPatch]
public static class ActiveMissionLogPatch
{
    private static int _portalBefore;

    /// Returns the caller of the first stack frame declared by the patched type, or "?" when the call was inlined and that frame is missing from the trace.
    private static string CallerOf(System.Type patchedType)
    {
        var stack = new System.Diagnostics.StackTrace(1, false);
        for (var i = 0; i < stack.FrameCount - 1; i++)
        {
            var method = stack.GetFrame(i)?.GetMethod();
            if (method?.DeclaringType != patchedType) continue;
            var caller = stack.GetFrame(i + 1)?.GetMethod();
            return caller == null ? "?" : $"{caller.DeclaringType?.Name}.{caller.Name}";
        }
        return "?";
    }

    [HarmonyPatch(typeof(GameSession), "StartTransition")]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void StartTransitionPostfix(GameSession __instance, int missionIdx, GameSession.GameStateAction stateAction)
    {
        Plugin.Instance.Log.LogInfo($"activeMission = {__instance.activeMission} (request: {missionIdx}, action: {stateAction}) via {CallerOf(typeof(GameSession))}");
    }

    [HarmonyPatch(typeof(GameSession), "UpdatePortal")]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    public static void UpdatePortalPrefix(GameSession __instance)
    {
        _portalBefore = __instance.activeMission;
    }

    [HarmonyPatch(typeof(GameSession), "UpdatePortal")]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void UpdatePortalPostfix(GameSession __instance)
    {
        if (__instance.activeMission == _portalBefore) return;
        Plugin.Instance.Log.LogInfo($"activeMission = {__instance.activeMission} (portal rune match, was {_portalBefore}) via GameSession.UpdatePortal");
    }

    [HarmonyPatch(typeof(GameSession), "StartMenuTransition")]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void StartMenuTransitionPostfix(GameSession __instance)
    {
        Plugin.Instance.Log.LogInfo($"activeMission = {__instance.activeMission} (exit to menu) via GameSession.StartMenuTransition");
    }

    [HarmonyPatch(typeof(GameSession), "StartHomeTransition")]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void StartHomeTransitionPostfix(GameSession __instance)
    {
        Plugin.Instance.Log.LogInfo($"activeMission = {__instance.activeMission} (return home) via GameSession.StartHomeTransition");
    }
}
