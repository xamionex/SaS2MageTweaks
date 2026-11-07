using System;
using System.Collections.Generic;
using Bestiary.monsters;
using Chronicler.missions;
using ProjectMage.character;
using ProjectMage.gamestate;
using ProjectMage.gamestate.arenastate;
using ProjectMage.gamestate.mage;

namespace SaS2MageTweaks;

/// Category of a mage, matching the five kinds a player knows: named, fated, nameless, gauntlet and wandering.
/// The primary target of a hunt gets its hunt type: named, fated or nameless. Gauntlet mages get gauntlet.
/// Wandering mages are split by where they roam: hunt wandering for the other mages of an active hunt (companion and optional targets),
/// non-hunt wandering for every mage outside a hunt. Out in an area that is the area's world mission (type 0), which is active but not a hunt.
internal enum MageKind
{
    Named,
    Fated,
    Nameless,
    Gauntlet,
    HuntWandering,
    NonHuntWandering
}

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

    /// Call when a new map loads so stale IDs don't block fresh mages or suppress their diagnostic log.
    internal static void ClearPromotionCache()
    {
        PromotedCharIds.Clear();
        Battle2Mages.Clear();
        LoggedMages.Clear();
    }

    private static readonly List<int> LoggedMages = [];

    /// Drops everything remembered about a character ID.
    /// CharMgr.AddCharacter hands out the lowest free slot, so the mage that spawns right after another one died usually gets the dead mage's ID.
    /// Without this, a fresh mage inherits "already promoted" or "parked in BATTLE_2" from its predecessor and is never parked itself,
    /// leaving it as a finished-cycles mage that vanilla UpdatePursuit holds above 10% HP.
    internal static void ForgetCharacter(int charId)
    {
        PromotedCharIds.Remove(charId);
        Battle2Mages.Remove(charId);
        LoggedMages.Remove(charId);
        SkippedPhasesCount.Remove(charId);
        TotalCyclesCount.Remove(charId);
    }

    /// Greater than 0 while CreateMages is running. CreateMages only spawns mission targets, but it calls
    /// AddMage and GetMaxHP before SetMissionTarget designates them, so classification needs this window.
    internal static int CreateMagesDepth;

    /// Greater than 0 while SetMissionTarget is applying the target flags inside CreateMages.
    /// The flags identify the primary target, so classification must use them instead of the CreateMages session fallback for this target.
    internal static int DesignatedTargetDepth;

    /// Returns the registered Mage for a character, or null when the character is not a registered hunt mage.
    internal static Mage ResolveMage(Character character)
    {
        var gameSession = GameSessionMgr.gameSession;
        if (character == null || gameSession == null || character.mageIdx < 0) return null;
        var mages = gameSession.mageMgr.mage;
        if (character.mageIdx >= mages.Length) return null;
        var mage = mages[character.mageIdx];
        if (mage is not { exists: true } || mage.charIdx != character.ID) return null;
        return mage;
    }

    /// True when the mage is a target of the currently active mission: the active mission's target at the mage's missionTargetIdx points back at the mage's slot.
    /// This mirrors the membership test vanilla Mage.Kill and BossSplash use for mission credit, so it says nothing about session state that could misclassify a roaming mage.
    internal static bool IsMissionMage(Mage mage)
    {
        if (mage is not { exists: true }) return false;
        var gameSession = GameSessionMgr.gameSession;
        if (gameSession == null) return false;
        var activeMission = gameSession.activeMission;
        if (activeMission < 0 || activeMission >= MissionCatalog.mission.Count) return false;
        var targets = MissionCatalog.mission[activeMission].target;
        var targetIdx = mage.missionTargetIdx;
        if (targetIdx < 0 || targetIdx >= targets.Count) return false;
        var slot = GetMageSlot(gameSession, mage);
        if (slot < 0) return false;
        return targets[targetIdx].targetMageIdx == slot;
    }

    /// True when the mage is the required target of the active mission.
    /// Vanilla marks story targets, companions and optional targets with flag 19 (invisible) or flag 20 (optional), so those never count as the primary.
    internal static bool IsPrimaryMissionTarget(Mage mage)
    {
        return IsMissionMage(mage) && !mage.missionInvisible && !mage.optional;
    }

    /// The mage's slot index in the session mage array.
    /// Vanilla stores it in a private field, and mission targets are compared against that same slot, so it is recovered by reference scan over the 4-slot array instead of reflection.
    private static int GetMageSlot(GameSession gameSession, Mage mage)
    {
        var mages = gameSession.mageMgr.mage;
        for (var i = 0; i < mages.Length; i++)
        {
            if (ReferenceEquals(mages[i], mage)) return i;
        }
        return -1;
    }

    /// Single skip decision for every mage, so spawn, NextCycle and mission designation all agree.
    /// The primary target of a hunt uses its hunt type toggle, companions and other in-hunt mages use the hunt wandering toggle, and mages outside a hunt use the non-hunt wandering toggle.
    internal static bool ShouldSkip(MageKind kind, Mage mage)
    {
        if (mage == null) return false;
        LogMageDetection(mage, kind);

        return kind switch
        {
            MageKind.Gauntlet         => Plugin.SkipGauntletMages.Value,
            // Named hunts are mission type 1. A primary target of any other unlisted hunt type counts as named only with a custom path, as the fallback always required.
            MageKind.Named            => (GetActiveMissionType() == 1 || mage.hasCustomPath) && Plugin.SkipNamedMages.Value,
            MageKind.Fated            => Plugin.SkipFatedMages.Value,
            MageKind.Nameless         => mage.hasCustomPath && Plugin.SkipNamelessMages.Value,
            MageKind.HuntWandering    => Plugin.SkipHuntWanderingMages.Value,
            MageKind.NonHuntWandering => Plugin.SkipNonHuntWanderingMages.Value,
            _                         => false
        };
    }

    internal static bool ShouldSkipGauntletMage() => GauntletMgr.IsActive && Plugin.SkipGauntletMages.Value;

    /// Logs the first skip decision for each character so a repro shows which class and toggle applied.
    private static void LogMageDetection(Mage mage, MageKind kind)
    {
        if (mage.charIdx < 0 || LoggedMages.Contains(mage.charIdx)) return;

        var gameSession   = GameSessionMgr.gameSession;
        var activeMission = gameSession?.activeMission ?? -1;
        var missionType   = GetActiveMissionType();
        Plugin.Instance.Log.LogInfo($"mage detected: ID {mage.charIdx} ({GetMageName(mage)}), kind: {kind}, Invisible: {mage.missionInvisible}, Optional: {mage.optional}, HasCustomPath: {mage.hasCustomPath}, Mission: {activeMission}, Type: {missionType}");
        LoggedMages.Add(mage.charIdx);
    }

    /// True while a mod-skipped mage is parked in the BATTLE_2 fallback waiting for an arena (never promoted to boss).
    internal static bool IsBattle2Waiting(int charId) => Battle2Mages.Contains(charId);

    /// Classifies a mage per-mage, from the player's five kinds: named, fated, nameless, gauntlet and wandering.
    /// Wandering splits by where the mage roams. Outside a hunt, that is at home (no active mission) or out in an area, where the area's world mission (type 0) is the active mission and its targets are the wandering mages: non-hunt wandering.
    /// Inside a hunt (any other mission type), only the primary target uses the hunt type (named, fated or nameless).
    /// Every other mage of a hunt, such as the optional and invisible companion targets, is hunt wandering.
    /// During CreateMages the new targets are not designated yet, so that window falls back to the hunt type.
    internal static MageKind GetMageKind(Character character, Mage mage)
    {
        if (GauntletMgr.IsActive) return MageKind.Gauntlet;

        // Not in a hunt: at home, or roaming an area under its world mission. The world mission is an active mission, but it is not a hunt.
        // Whatever its flags or state, a mage of the world mission is a wandering mage outside any hunt.
        var missionType = GetActiveMissionType();
        if (missionType <= 0) return MageKind.NonHuntWandering;

        // CreateMages only spawns mission targets and runs before SetMissionTarget designates them, so every GetMaxHP call inside it (including the character constructor) is a mission spawn.
        // SetMissionTargetPatch raises DesignatedTargetDepth once the flags are available for one target.
        if (CreateMagesDepth > 0 && DesignatedTargetDepth == 0) return GetHuntTypeKind(missionType);

        if (mage != null)
        {
            // Only the required target of the hunt uses the Named/Fated/Nameless toggle.
            // Companions and optional targets are hunt wandering.
            return IsPrimaryMissionTarget(mage) ? GetHuntTypeKind(missionType) : MageKind.HuntWandering;
        }

        // Unregistered minions keep the hunt type so their HP follows the mage that spawned them.
        // Any other unregistered mage-type character inside a hunt is an ambient spawn, which counts as hunt wandering.
        return character is { minionParentIdx: >= 0 } ? GetHuntTypeKind(missionType) : MageKind.HuntWandering;
    }

    /// The kind a hunt of this mission type gives its primary target: 1 named, 2 nameless, 3 fated.
    /// Any other hunt type falls back to named, as the skip toggles always did.
    private static MageKind GetHuntTypeKind(int missionType)
    {
        return missionType switch
        {
            2 => MageKind.Nameless,
            3 => MageKind.Fated,
            _ => MageKind.Named
        };
    }

    /// The type of the active mission, or -1 when no mission is active.
    /// 0 is the area's world mission (roaming, not a hunt), 1 named, 2 nameless, 3 fated (bounty), 6 gauntlet.
    private static int GetActiveMissionType()
    {
        var gameSession = GameSessionMgr.gameSession;
        if (gameSession == null) return -1;
        var activeMission = gameSession.activeMission;
        if (activeMission < 0 || activeMission >= MissionCatalog.mission.Count) return -1;
        return MissionCatalog.mission[activeMission].GetMissionType();
    }

    internal static string GetMageName(Mage mage)
    {
        if (mage.charIdx < 0) return "Unknown";
        var character = CharMgr.character[mage.charIdx];
        if (character == null) return "Unknown";
        var monsterDef = MonsterCatalog.monsterDef[character.monsterIdx];
        return monsterDef?.name ?? "Unknown";
    }

    /// Tries to promote a mage to boss.
    /// If the mage is not yet inside an arena rect, parks it in BATTLE_2 and returns, NextCyclePatch will retry each frame until the mage has moved inside the rect during combat, at which point promotion succeeds.
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
                // Reset to WAITING after promotion.
                // Vanilla NextCycle calls SetPhase(0) itself, but NextCyclePatch skips the original to avoid the flee/teleport.
                // A mage promoted mid-flee stays in FLEE (phase 7), which forces the warp key every frame, so it warps in place forever instead of fighting.
                Plugin.SetPhaseMethod?.Invoke(mage, [0]);
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
        var newHp = GauntletMgr.IsActive ? maxHp / 2f : maxHp / 4f;

        if (!(Math.Abs(character.hp - newHp) > 0.1f)) return;
        Plugin.Instance.Log.LogInfo($"Mage {mage.charIdx} HP: {character.hp} -> {newHp} (max: {maxHp})");
        character.hp = newHp;
    }

    /// Re-applies max HP once SetMissionTarget has designated a freshly spawned mage.
    /// CreateMages assigns spawn HP before the target flags exist, so a companion of a hunt starts with the hunt type multiplier instead of the hunt wandering multiplier.
    /// Does nothing when the classification did not change (world wanderers and hunt primaries), so it never touches HP that was already right.
    /// A mage spawned this moment has not taken damage, so when the max changes it restarts at full health on the new max.
    internal static void ReapplyMaxHpAfterDesignation(Character character, Mage mage)
    {
        if (character == null || mage == null) return;
        if (character.monsterIdx < 0 || character.monsterIdx >= MonsterCatalog.monsterDef.Count) return;

        var maxHp = (float)Plugin.GetMaxHpMethod.Invoke(MonsterCatalog.monsterDef[character.monsterIdx].gameMonster, [character]);
        if (Math.Abs(maxHp - mage.monsterHP) < 0.01f) return;

        character.hp = maxHp;
        mage.monsterHP = maxHp;
        // Vanilla derives the per-cycle damage threshold from max HP, so keep it in sync with the new max.
        mage.cycleMaxDamage = maxHp / Math.Max(1, mage.totalCycles);
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
    /// Both warp summons and phase summons draw from the same pool, so the pool is scaled by the larger of the two multipliers;
    /// the per-event count is scaled separately in MagePatch.SummonPatch.
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
}
