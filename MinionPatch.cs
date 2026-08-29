using System;
using HarmonyLib;
using ProjectMage.character;
using ProjectMage.gamestate.mage;
using ProjectMage.hit;
using ProjectMage.Monsters;

namespace SaS2MageTweaks;

[HarmonyPatch]
internal static class MinionPatch
{
    // Scale the HP of minions right after a mage summons them. Runs on the host, which is the
    // authority for character HP; clients receive the scaled value through the normal update packets.
    [HarmonyPatch(typeof(Mage), "SummonNext")]
    [HarmonyPostfix]
    public static void SummonNextPatch(Mage __instance)
    {
        if (Math.Abs(Plugin.MinionHpMultiplier.Value - 1f) < 0.001f) return;

        for (var i = 7; i < CharMgr.character.Length; i++)
        {
            var character = CharMgr.character[i];
            if (!character.exists || character.minionParentIdx != __instance.charIdx) continue;
            if (character.hp < character.stats.GetMaxHP()) continue; // only touch freshly spawned minions

            character.hp = character.stats.GetMaxHP() * Plugin.MinionHpMultiplier.Value;
        }
    }

    // Scale the damage of minions. GameMonster.PopulateHVals is the single funnel for monster
    // attack values, so scaling there covers melee, ranged and magic attacks.
    [HarmonyPatch(typeof(GameMonster), "PopulateHVals")]
    [HarmonyPostfix]
    public static void PopulateHValsPatch(GameMonster __instance, float[] hVals, Character character)
    {
        if (character == null) return;
        var multiplier = MageSkipHelper.GetMinionDamageMultiplier(character);
        if (multiplier == 1f) return;

        for (var i = 0; i < hVals.Length; i++)
            hVals[i] *= multiplier;
    }
}
