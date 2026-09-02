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
    private static bool GetHostileOneWayPrefix(Character me, Character other, ref bool __result)
    {
        if (GauntletMgr.IsActive) return true;

        if (me == null || other == null) return true;
        if (me.monsterIdx < 0 || me.monsterIdx >= MonsterCatalog.monsterDef.Count) return true;
        if (other.monsterIdx < 0 || other.monsterIdx >= MonsterCatalog.monsterDef.Count) return true;

        var meDef    = MonsterCatalog.monsterDef[me.monsterIdx];
        var otherDef = MonsterCatalog.monsterDef[other.monsterIdx];
        if (meDef.type != 1 || otherDef.type != 1) return true;

        var meIsMage       = meDef.gameMonster.mage;
        var meIsMinion     = meDef.gameMonster.minion;
        var meIsMob        = meDef.gameMonster.mob;
        var otherIsMage    = otherDef.gameMonster.mage;
        var otherIsMinion  = otherDef.gameMonster.minion;
        var otherIsMob     = otherDef.gameMonster.mob;
        var otherIsHazeburnt = otherDef.gameMonster.hazeBurnt;

        if (Plugin.MagesWontHitMages.Value && meIsMage && otherIsMage)
        {
            __result = false;
            return false;
        }
        if (Plugin.MinionsWontHitMinions.Value && meIsMinion && otherIsMinion)
        {
            __result = false;
            return false;
        }
        if (Plugin.MagesWontHitMinions.Value && meIsMage && otherIsMinion)
        {
            __result = false;
            return false;
        }
        if (Plugin.MinionsWontHitMages.Value && meIsMinion && otherIsMage)
        {
            __result = false;
            return false;
        }
        if (Plugin.MagesWontHitHazeburnt.Value && meIsMage && otherIsHazeburnt)
        {
            __result = false;
            return false;
        }
        if (Plugin.MinionsWontHitHazeburnt.Value && meIsMinion && otherIsHazeburnt)
        {
            __result = false;
            return false;
        }
        if (Plugin.MagesWontHitMobs.Value && meIsMage && otherIsMob)
        {
            __result = false;
            return false;
        }
        if (Plugin.MinionsWontHitMobs.Value && meIsMinion && otherIsMob)
        {
            __result = false;
            return false;
        }
        if (Plugin.MobsWontHitMinions.Value && meIsMob && otherIsMinion)
        {
            __result = false;
            return false;
        }
        if (Plugin.MobsWontHitMages.Value && meIsMob && otherIsMage)
        {
            __result = false;
            return false;
        }
        if (Plugin.MobsWontHitHazeburnt.Value && meIsMob && otherIsHazeburnt)
        {
            __result = false;
            return false;
        }
        return true;
    }
}
