using Bestiary.monsters;
using HarmonyLib;
using ProjectMage.character;
using ProjectMage.character.ai;
using ProjectMage.gamestate.arenastate;
using ProjectMage.Monsters;

namespace SaS2MageTweaks;

[HarmonyPatch]
internal static class HostilityPatch
{
    /// CharAI.GetHostileOneWay is the single funnel for monster-vs-monster hostility: it drives both aggro (FindNearestTarg) and damage (HitManager).
    /// Returning false here makes mages/minions ignore each other entirely.
    /// Trials of the Brave (gauntlet) keeps vanilla behavior.
    [HarmonyPatch(typeof(CharAI), "GetHostileOneWay")]
    [HarmonyPrefix]
    // ReSharper disable once InconsistentNaming
    private static bool GetHostileOneWayPrefix(Character me, Character other, ref bool __result)
    {
        if (me == null || other == null) return true;
        if (me.monsterIdx < 0 || me.monsterIdx >= MonsterCatalog.monsterDef.Count) return true;
        if (other.monsterIdx < 0 || other.monsterIdx >= MonsterCatalog.monsterDef.Count) return true;

        var meDef = MonsterCatalog.monsterDef[me.monsterIdx];
        var otherDef = MonsterCatalog.monsterDef[other.monsterIdx];

        // Traps (type 4) hostile to monsters: vanilla hits a monster unless the trap is friendly to monsters.
        // The per-enemy-type toggles make the trap read as friendly to that type.
        // Checked before the gauntlet gate because it is an explicit preference and traps exist inside the gauntlet too.
        if (meDef.type == 4 && otherDef.type == 1)
        {
            var targetIsMage = otherDef.gameMonster.mage;
            var targetIsMinion = otherDef.gameMonster.minion;
            var targetIsHazeburnt = otherDef.gameMonster.hazeBurnt;
            var targetIsMob = otherDef.gameMonster.mob;

            if ((!targetIsMage || !Plugin.TrapsWontHitMages.Value) &&
                (!targetIsMinion || !Plugin.TrapsWontHitMinions.Value) &&
                (!targetIsHazeburnt || !Plugin.TrapsWontHitHazeburnt.Value) &&
                (targetIsHazeburnt || !targetIsMob || !Plugin.TrapsWontHitRegularEnemies.Value)) return true;
            __result = false;
            return false;
        }

        if (GauntletMgr.IsActive) return true;

        if (meDef.type != 1 || otherDef.type != 1) return true;

        var meIsMage = meDef.gameMonster.mage;
        var meIsMinion = meDef.gameMonster.minion;
        var meIsMob = meDef.gameMonster.mob;
        var otherIsMage = otherDef.gameMonster.mage;
        var otherIsMinion = otherDef.gameMonster.minion;
        var otherIsMob = otherDef.gameMonster.mob;
        var otherIsHazeburnt = otherDef.gameMonster.hazeBurnt;

        // Global Tweaks: enemies won't hit other enemies, gated by the apply toggles.
        // Hazeburnt wins over mob because hazeburnt monsters are mobs with the hazeburnt flag.
        if (Plugin.EnemiesWontHitOtherEnemies.Value)
        {
            var meAffected = (meIsMage && Plugin.ApplyGlobalTweaksToMages.Value) ||
                             (meIsMinion && Plugin.ApplyGlobalTweaksToMinions.Value) ||
                             (meDef.gameMonster.hazeBurnt && Plugin.ApplyGlobalTweaksToHazeburnt.Value) ||
                             (!meDef.gameMonster.hazeBurnt && meIsMob &&
                              Plugin.ApplyGlobalTweaksToRegularEnemies.Value);
            var otherAffected = (otherIsMage && Plugin.ApplyGlobalTweaksToMages.Value) ||
                                (otherIsMinion && Plugin.ApplyGlobalTweaksToMinions.Value) ||
                                (otherIsHazeburnt && Plugin.ApplyGlobalTweaksToHazeburnt.Value) ||
                                (!otherIsHazeburnt && otherIsMob && Plugin.ApplyGlobalTweaksToRegularEnemies.Value);
            if (meAffected && otherAffected)
            {
                __result = false;
                return false;
            }
        }

        if ((!Plugin.MagesWontHitMages.Value || !meIsMage || !otherIsMage) &&
            (!Plugin.MinionsWontHitMinions.Value || !meIsMinion || !otherIsMinion) &&
            (!Plugin.MagesWontHitMinions.Value || !meIsMage || !otherIsMinion) &&
            (!Plugin.MinionsWontHitMages.Value || !meIsMinion || !otherIsMage) &&
            (!Plugin.MagesWontHitHazeburnt.Value || !meIsMage || !otherIsHazeburnt) &&
            (!Plugin.MinionsWontHitHazeburnt.Value || !meIsMinion || !otherIsHazeburnt) &&
            (!Plugin.MagesWontHitMobs.Value || !meIsMage || !otherIsMob) &&
            (!Plugin.MinionsWontHitMobs.Value || !meIsMinion || !otherIsMob) &&
            (!Plugin.MobsWontHitMinions.Value || !meIsMob || !otherIsMinion) &&
            (!Plugin.MobsWontHitMages.Value || !meIsMob || !otherIsMage) &&
            (!Plugin.MobsWontHitHazeburnt.Value || !meIsMob || !otherIsHazeburnt)) return true;
        __result = false;
        return false;
    }
}