using System;
using System.Collections.Generic;
using HarmonyLib;
using ProjectMage;
using ProjectMage.character;
using ProjectMage.gamestate.mage;

namespace SaS2MageTweaks;

[HarmonyPatch]
internal static class MagePatch
{
    // Track mages that have already been retroactively skipped to avoid double processing.
    private static readonly HashSet<int> SkippedMages = [];
    // Track mages whose HP has already been reduced to avoid double reduction.
    private static readonly HashSet<int> HpReducedMages = [];

    // Per-mage summon event state, keyed by charIdx so multiple mages summoning in the same frame don't interfere.
    // A summon event is one Summon() call: the mage plays the summon animation and SummonNext spawns minions every 0.5s while summonFrame is positive (vanilla: 2 minions per event).
    private static readonly Dictionary<int, float> SummonMultipliers = new();
    private static readonly Dictionary<int, int> SummonCounts = new();

    [HarmonyPatch(typeof(Mage), "NextCycle")]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    public static bool NextCyclePatch(Mage __instance)
    {
        if (__instance.charIdx < 0 || __instance.charIdx >= CharMgr.character.Length)
            return true;
        var character = CharMgr.character[__instance.charIdx];
        if (character is not { exists: true }) return true;

        // Skip decision per mage, from the same classification as the HP multipliers: the primary target of a hunt uses its hunt type toggle,
        // every other mage of a hunt uses the hunt wandering toggle, and mages outside a hunt (the wandering mages of an area's world mission) use the non-hunt wandering toggle.
        // Gauntlet mages use the gauntlet toggle.
        var shouldSkip = MageSkipHelper.ShouldSkip(MageSkipHelper.GetMageKind(character, __instance), __instance);

        if (!shouldSkip) return true;          // No skipping, run original NextCycle
        if (!NetworkMgr.Instance.IsHost()) return true;

        // Already a boss, nothing more to do, skip original
        if (character.boss) return false;

        // If we already skipped this mage before, don't do it again
        if (SkippedMages.Contains(__instance.charIdx))
            return false;

        // If cycles are not yet complete, finish them now (retroactive skip)
        if (__instance.cycle < __instance.totalCycles)
        {
            Plugin.Instance.Log.LogInfo($"Retroactive skip: completing {__instance.totalCycles - __instance.cycle} remaining cycles for mage {__instance.charIdx}");
            MageSkipHelper.MarkCyclesComplete(__instance);
            SkippedMages.Add(__instance.charIdx);
        }

        // Promote to boss
        MageSkipHelper.TryPromoteToBoss(character, __instance);

        // Reduce HP only once per mage
        if (!character.boss || HpReducedMages.Contains(__instance.charIdx)) return false;
        MageSkipHelper.ReduceBossHp(character, __instance);
        HpReducedMages.Add(__instance.charIdx);

        // Prevent original NextCycle from running
        return false;
    }

    // Clear tracking caches on map load to avoid stale IDs
    [HarmonyPatch(typeof(NetworkEvents), "OnMapLoading")]
    [HarmonyPostfix]
    public static void OnMapLoadingPatch()
    {
        SkippedMages.Clear();
        HpReducedMages.Clear();
        SummonMultipliers.Clear();
        SummonCounts.Clear();
        MageSkipHelper.ClearPromotionCache();
        Plugin.Instance.Log.LogDebug("MagePatch caches cleared on map load.");
    }

    // Mod-skipped mages parked in BATTLE_2 are cycle-complete and never boss, which is exactly vanilla UpdatePursuit's post-cycle branch: perpetual leap-warp at players plus an HP floor at 10% of max, leaving them unkillable while they chase.
    // Skipping the whole method for them is safe: the pre-cycle flee leap needs cycle < totalCycles and the warp key forcing only matters in warp phases, both inert for a parked mage, which is why warp phases are let through anyway.
    [HarmonyPatch(typeof(Mage), "UpdatePursuit")]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    public static bool UpdatePursuitPrefix(Mage __instance, Character character)
    {
        if (character == null) return true;
        if (!MageSkipHelper.IsBattle2Waiting(character.ID)) return true;
        return __instance.phase is 2 or 5 or 7 or 8 or 11;
    }

    // Character IDs are reused: CharMgr.AddCharacter hands out the lowest free slot, so a wanderer that respawns after a kill usually gets the dead mage's ID.
    // Every per-ID cache must be dropped when a mage is activated, or the new mage inherits "already skipped", "already promoted" or "parked in BATTLE_2" from its predecessor.
    // A prefix, so the state the new mage creates during Activate itself (its first GetMaxHP calls) is kept.
    [HarmonyPatch(typeof(Mage), "Activate")]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    public static void ActivateForgetPrefix(int charIdx)
    {
        SkippedMages.Remove(charIdx);
        HpReducedMages.Remove(charIdx);
        SummonMultipliers.Remove(charIdx);
        SummonCounts.Remove(charIdx);
        MageSkipHelper.ForgetCharacter(charIdx);
        EnemyScalingPatch.ForgetCharacter(charIdx);
    }

    // Scale the summon pools right after a mage is activated so the configured minion counts apply to every fight.
    [HarmonyPatch(typeof(Mage), "Activate")]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void ActivatePatch(Mage __instance)
    {
        MageSkipHelper.ApplyMinionCountMultipliers(__instance);
    }

    // Capture the phase before Summon() advances it, so the per-event multiplier can be looked up.
    [HarmonyPatch(typeof(Mage), "Summon")]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    public static void SummonPrefix(Mage __instance)
    {
        SummonMultipliers[__instance.charIdx] = MageSkipHelper.GetSummonCountMultiplier(__instance.phase);
        SummonCounts[__instance.charIdx] = 0;
    }

    // Extend the summon window so the scaled number of minions can actually spawn.
    // Vanilla: summonFrame = 1.5s, one SummonNext every 0.5s -> 2 minions per event.
    [HarmonyPatch(typeof(Mage), "Summon")]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void SummonPostfix(Mage __instance)
    {
        var multiplier = SummonMultipliers.TryGetValue(__instance.charIdx, out var m) ? m : 1f;
        if (Math.Abs(multiplier - 1f) < 0.001f) return;
        __instance.summonFrame = 1.5f * multiplier;
    }

    // Cap each summon event at the scaled count so a 0 multiplier means no minions at all.
    [HarmonyPatch(typeof(Mage), "SummonNext")]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    public static bool SummonNextPrefix(Mage __instance)
    {
        if (!SummonMultipliers.TryGetValue(__instance.charIdx, out var multiplier)) return true;
        if (Math.Abs(multiplier - 1f) < 0.001f) return true;

        var count = SummonCounts.TryGetValue(__instance.charIdx, out var c) ? c : 0;
        var max = (int)Math.Round(2f * multiplier);
        if (count >= max)
        {
            __instance.summonFrame = 0f; // stop this summon event
            return false;               // skip the original SummonNext
        }
        SummonCounts[__instance.charIdx] = count + 1;
        return true;
    }

    // The original SummonNext ends the event early whenever a special or elite minion is picked (summonFrame = 0).
    // That would cut the scaled count short in later cycles, so when the multiplier is active we keep the summon window open until the cap is reached.
    [HarmonyPatch(typeof(Mage), "SummonNext")]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    public static void SummonNextPostfix(Mage __instance)
    {
        if (!SummonMultipliers.TryGetValue(__instance.charIdx, out var multiplier)) return;
        if (Math.Abs(multiplier - 1f) < 0.001f) return;

        var count = SummonCounts.TryGetValue(__instance.charIdx, out var c) ? c : 0;
        var max = (int)Math.Round(2f * multiplier);
        if (count >= max) return; // prefix already stopped the event

        __instance.summonFrame = 1.5f * multiplier;
    }

    // Disable the ambush summon phase (mage warps away and summons minions mid-hunt).
    [HarmonyPatch(typeof(Mage), "Update")]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    public static void UpdateAmbushSummonPatch(Mage __instance)
    {
        if (Plugin.DisableWarpAndSummonMinions.Value)
            __instance.ambushSummon = false;
    }

    // Disable the ambush rage phase (mage warps to the player and enters an aggressive attack state mid-hunt).
    [HarmonyPatch(typeof(Mage), "Update")]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    public static void UpdateAmbushRagePatch(Mage __instance)
    {
        if (Plugin.DisableWarpAndAggressiveAttack.Value)
            __instance.ambushRage = false;
    }
}
