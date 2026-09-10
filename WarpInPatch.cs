using Bestiary.monsters;
using HarmonyLib;
using ProjectMage.character;
using ProjectMage.Monsters;

namespace SaS2MageTweaks;

/// <summary>
/// Mages and minions are invulnerable while warping in: GameMonster.GetDefense adds 1000 defense
/// during the first half of a warp-in (warpFrame between 0 and warpDuration / 2).
/// These toggles remove that protection so they take full damage during warp-in.
/// </summary>
[HarmonyPatch]
internal static class WarpInPatch
{
    [HarmonyPatch(typeof(GameMonster), "GetDefense")]
    [HarmonyPostfix]
    // ReSharper disable once InconsistentNaming
    private static void GetDefensePatch(Character character, ref float __result)
    {
        if (character == null || character.playerIdx > -1) return;
        if (!character.warp.active || character.warp.warpFrame <= 0f) return;
        if (character.warp.warpFrame >= character.warp.warpDuration / 2f) return;
        if (character.monsterIdx < 0 || character.monsterIdx >= MonsterCatalog.monsterDef.Count) return;
        // GetDefense returns 0 for damage types the monster has no defense for; only strip
        // the warp bonus when the result actually contains it, so defense never goes negative.
        if (__result <= 0f) return;

        var def = MonsterCatalog.monsterDef[character.monsterIdx];
        if (def.type != 1) return;
        var gm = def.gameMonster;

        // Global Tweaks: enemies take full damage during warp in, gated by the apply toggles.
        // Hazeburnt wins over mob because hazeburnt monsters are mobs with the hazeburnt flag.
        if (Plugin.EnemiesTakeFullDamageDuringWarpIn.Value)
        {
            if ((gm.mage && Plugin.ApplyGlobalTweaksToMages.Value) ||
                (gm.minion && Plugin.ApplyGlobalTweaksToMinions.Value) ||
                (gm.hazeBurnt && Plugin.ApplyGlobalTweaksToHazeburnt.Value) ||
                (!gm.hazeBurnt && gm.mob && Plugin.ApplyGlobalTweaksToRegularEnemies.Value))
            {
                __result -= 1000f;
                return;
            }
        }

        if (gm.mage && Plugin.MagesTakeFullDamageDuringWarpIn.Value)
        {
            __result -= 1000f;
        }
        else if (gm.minion && Plugin.MinionsTakeFullDamageDuringWarpIn.Value)
        {
            __result -= 1000f;
        }
    }
}
