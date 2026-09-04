using System;
using System.Collections.Generic;
using Bestiary.monsters;
using Chronicler.missions;
using ProjectMage.character;
using ProjectMage.gamestate;
using ProjectMage.gamestate.arenastate;
using ProjectMage.gamestate.mage;

namespace SaS2MageTweaks;

internal static class MageSkipHelper
{
    /// Tracks skipped phase count per character ID for the loot bonus on death.
    internal static readonly Dictionary<int, int> SkippedPhasesCount = new();
    /// Tracks total cycles per character ID so loot amounts can be scaled correctly.
    internal static readonly Dictionary<int, int> TotalCyclesCount = new();

    /// Guards against double-promotion (co-op) and tracks successfully promoted mages.
    /// Only populated on SUCCESS, BATTLE_2 fallback mages are NOT added until promotion succeeds.
    private static readonly HashSet<int> PromotedCharIds = [];

    /// Tracks mages in the BATTLE_2 fallback state so we don't spam SetPhase/log every frame.
    private static readonly HashSet<int> Battle2Mages = [];

    /// Call when a new map loads so stale IDs don't block fresh mages.
    internal static void ClearPromotionCache()
    {
        PromotedCharIds.Clear();
        Battle2Mages.Clear();
    }

    private static readonly List<int> LoggedMages = [];

    internal static bool ShouldSkipMissionMage(Mage mage)
    {
        var activeMission = GameSessionMgr.gameSession.activeMission;
        var missionType   = activeMission >= 0 ? MissionCatalog.mission[activeMission].GetMissionType() : -1;

        if (!LoggedMages.Contains(mage.charIdx))
        {
            Plugin.Instance.Log.LogInfo($"mage detected: ID {mage.charIdx} ({GetMageName(mage)}), Invisible: {mage.missionInvisible}, Optional: {mage.optional}, HasCustomPath: {mage.hasCustomPath}, Mission: {activeMission}, Type: {missionType}");
            LoggedMages.Add(mage.charIdx);
        }

        // Gauntlet (type 6) checked first so it never falls through to other types.
        if (GauntletMgr.IsActive) return Plugin.SkipGauntletMages.Value;
        
        if (activeMission < 0) return false;

        return missionType switch
        {
            1 => Plugin.SkipNamedMages.Value,                                     // Named
            2 => mage.hasCustomPath && Plugin.SkipNamelessMages.Value,            // Nameless, requires custom path, token_nameless reward
            3 => Plugin.SkipFatedMages.Value,                                     // Fated, token_arena reward, tiered
            _ => mage.hasCustomPath && Plugin.SkipNamedMages.Value                // Fallback
        };
    }

    internal static bool ShouldSkipWanderingMage() => GameSessionMgr.gameSession.activeMission < 0 && Plugin.SkipWanderingMages.Value;
    internal static bool ShouldSkipGauntletMage() => GauntletMgr.IsActive && Plugin.SkipGauntletMages.Value;

    internal static string GetMageName(Mage mage)
    {
        if (mage.charIdx < 0) return "Unknown";
        var character = CharMgr.character[mage.charIdx];
        if (character == null) return "Unknown";
        var monsterDef = MonsterCatalog.monsterDef[character.monsterIdx];
        return monsterDef?.name ?? "Unknown";
    }

    /// Tries to promote a mage to boss. If the mage is not yet inside an arena rect, parks it in BATTLE_2 and returns, NextCyclePatch will retry each frame until the mage has moved inside the rect during combat, at which point promotion succeeds.
    internal static void TryPromoteToBoss(Character character, Mage mage)
    {
        // Already successfully promoted, nothing to do.
        if (PromotedCharIds.Contains(character.ID)) return;

        try
        {
            var arenas   = GameSessionMgr.gameSession.mapMgr.arenas;
            var arenaIdx = (int)Plugin.GetAddCharToArenaIdxMethod.Invoke(arenas, [character]);

            if (arenaIdx > -1)
            {
                // Success, only now do we mark as promoted.
                PromotedCharIds.Add(character.ID);
                Battle2Mages.Remove(character.ID);
                character.boss = true;
                Plugin.OnAddCharToArenaMethod.Invoke(null, [character.ID, arenaIdx]);
                Plugin.Instance.Log.LogInfo($"Mage {mage.charIdx} ({GetMageName(mage)}) promoted to boss in arena {arenaIdx}.");
            }
            else
            {
                // Not inside any arena rect yet.
                // Use _battle2Mages to only call SetPhase and log once, subsequent retries from NextCyclePatch are silent and cheap.
                if (!Battle2Mages.Add(character.ID)) return;
                Plugin.SetPhaseMethod?.Invoke(mage, [6]);
                Plugin.Instance.Log.LogInfo($"Mage {mage.charIdx} ({GetMageName(mage)}): no arena found, waiting in BATTLE_2.");
            }
        }
        catch (Exception ex)
        {
            Plugin.Instance.Log.LogWarning($"TryPromoteToBoss failed: {ex.Message}");
        }
    }

    internal static void ReduceBossHp(Character character, Mage mage)
    {
        if (!Plugin.ReduceMageHp.Value) return;

        var monsterDef = MonsterCatalog.monsterDef[character.monsterIdx];
        var maxHp      = (float)Plugin.GetMaxHpMethod.Invoke(monsterDef.gameMonster, [character]); 
        var targetHp = GauntletMgr.IsActive ? maxHp / 2f : maxHp / 4f;
        var newHp = targetHp * Plugin.MageHpMultiplier.Value;

        if (!(Math.Abs(character.hp - newHp) > 0.1f)) return;
        Plugin.Instance.Log.LogInfo($"Mage {mage.charIdx} HP: {character.hp} -> {newHp} (max: {maxHp})");
        character.hp = newHp;
    }

    /// Marks all hunt cycles complete and records skipped count + total cycles so CharDeathPatch can scale bonus loot correctly.
    /// Note: cycleDamage is intentionally left at cycleMaxDamage + 1 so that Mage.Update triggers NextCycle, which NextCyclePatch intercepts to retry arena promotion without allowing to flee/teleport.
    internal static void MarkCyclesComplete(Mage mage)
    {
        // Already fully completed? Nothing to do.
        if (mage.cycle >= mage.totalCycles) return;

        var skipped = mage.totalCycles - mage.cycle;
        if (skipped > 0)
        {
            SkippedPhasesCount[mage.charIdx] = skipped;
            TotalCyclesCount[mage.charIdx]   = mage.totalCycles > 0 ? mage.totalCycles : 1;
        }

        mage.cycle       = mage.totalCycles;
        mage.cycleDamage = mage.cycleMaxDamage + 1f;
        mage.totalDamage = mage.monsterHP;
        Plugin.Instance.Log.LogInfo($"Cycles completed for mage {mage.charIdx} ({GetMageName(mage)}): skipped {skipped}/{mage.totalCycles}.");
    }

    /// Scales the summon pools of a mage by the configured multipliers. Vanilla pools are 20 regular, 8 special, 4 elite.
    /// Both warp summons and phase summons draw from the same pool, so the pool is scaled by the larger of the two
    /// multipliers; the per-event count is scaled separately in MagePatch.SummonPatch.
    internal static void ApplyMinionCountMultipliers(Mage mage)
    {
        var poolMultiplier = Math.Max(Plugin.WarpSummonCountMultiplier.Value, Plugin.PhaseSummonCountMultiplier.Value);
        mage.pool[0] = (int)Math.Round(20f * poolMultiplier);
        mage.pool[1] = (int)Math.Round(8f  * poolMultiplier);
        mage.pool[2] = (int)Math.Round(4f  * poolMultiplier);
        for (var j = 0; j < 3; j++)
        {
            if (mage.pool[j] < 0) mage.pool[j] = 0;
            mage.poolMax[j] = mage.pool[j];
        }
    }

    /// Returns the configured per-event summon count multiplier for the given mage phase.
    /// Phase 9 is the warp-away summon (ambush summon); phases 0, 1, 4 and 14 are the regular phase summons (0/1 = cycle-start summon, 4 = re-summon when minions die, 14 = boss heartseeker/eyeseeker summon).
    internal static float GetSummonCountMultiplier(int phase)
    {
        return phase switch
        {
            9 => Plugin.WarpSummonCountMultiplier.Value,
            0 or 1 or 4 or 14 => Plugin.PhaseSummonCountMultiplier.Value,
            _ => 1f
        };
    }

    /// Returns the configured damage multiplier for a minion character, or 1.0 if the character is not a mage minion.
    internal static float GetMinionDamageMultiplier(Character character)
    {
        if (character.minionParentIdx < 0) return 1f;
        if (Math.Abs(Plugin.MinionDamageMultiplier.Value - 1f) < 0.001f) return 1f;
        return Plugin.MinionDamageMultiplier.Value;
    }
}
