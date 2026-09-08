using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using Bestiary.monsters;
using HarmonyLib;
using ProjectMage;
using ProjectMage.character;
using ProjectMage.hit;
using ProjectMage.Monsters;
using ProjectMage.particles;

namespace SaS2MageTweaks;

/// <summary>
/// Per-category scaling for HP, damage, poise, poise damage and loot.
/// Categories: mages, mage minions, regular enemies (mobs) and hazeburnt monsters.
/// Bosses (final arena mages, gauntlet mages, map bosses) get their own per-player HP scaling and mages get an extra HP multiplier for the final arena phase.
/// All multipliers default to 1.0 (vanilla).
/// Per-player HP scaling is off by default.
/// </summary>
[HarmonyPatch]
internal static class EnemyScalingPatch
{
    private enum Category
    {
        None,
        Mage,
        Minion,
        Mob,
        Hazeburnt
    }

    /// <summary>
    /// Classifies a monster character by its monster def flags.
    /// Hazeburnt wins over mob because hazeburnt monsters are mobs with the hazeburnt flag.
    /// Type 6 monsters (wretches, the hazeburnt mage variants) are classified as hazeburnt.
    /// </summary>
    private static Category GetCategory(Character character)
    {
        if (character == null) return Category.None;
        if (character.monsterIdx < 0 || character.monsterIdx >= MonsterCatalog.monsterDef.Count) return Category.None;

        var def = MonsterCatalog.monsterDef[character.monsterIdx];
        if (def.type != 1 && def.type != 6) return Category.None;

        var gm = def.gameMonster;
        if (gm.mage) return Category.Mage;
        if (gm.minion) return Category.Minion;
        if (gm.hazeBurnt) return Category.Hazeburnt;
        if (gm.mob) return Category.Mob;
        return Category.None;
    }

    /// <summary>
    /// Returns the configured HP multiplier for a character.
    /// MageHpMultiplier (the starting HP multiplier) is applied separately in MageSkipHelper.ReduceBossHp, so it is not included here.
    /// A mage that is currently a boss gets the final arena multiplier.
    /// </summary>
    private static float GetHpMultiplier(Character character)
    {
        var def = MonsterCatalog.monsterDef[character.monsterIdx];
        var gm = def.gameMonster;

        var mult = 1f;
        if (gm.minion)
            mult = Plugin.MinionHpMultiplier.Value * GetMinionFinalArenaMultiplier(character);
        else if (gm.hazeBurnt)
            mult = Plugin.HazeburntHpMultiplier.Value;
        else if (gm.mob)
            mult = Plugin.RegularEnemyHpMultiplier.Value;

        if (character.boss && gm.mage)
            mult *= Plugin.MageHpMultiplierFinalArena.Value;

        return mult;
    }

    /// <summary>
    /// Applies the minion final-arena multiplier when the minion's parent mage is a boss.
    /// </summary>
    private static float GetMinionFinalArenaMultiplier(Character character)
    {
        if (Math.Abs(Plugin.MinionHpMultiplierFinalArena.Value - 1f) < 0.001f) return 1f;
        if (character.minionParentIdx < 0 || character.minionParentIdx >= CharMgr.character.Length) return 1f;
        var parent = CharMgr.character[character.minionParentIdx];
        if (parent == null || !parent.exists || !parent.boss) return 1f;
        return Plugin.MinionHpMultiplierFinalArena.Value;
    }

    /// <summary>
    /// Returns the per-player HP scaling factor: 1 + allyCount * percent, where allyCount is the number of additional players (0 solo, 1 with two players).
    /// Only applies when EnemyHpScalingPerPlayer is on.
    /// Bosses use the boss percent, everything else uses its category percent.
    /// </summary>
    private static float GetPerPlayerHpMultiplier(Character character)
    {
        if (!Plugin.EnemyHpScalingPerPlayer.Value) return 1f;

        var def = MonsterCatalog.monsterDef[character.monsterIdx];
        var gm = def.gameMonster;

        float percent;
        if (character.boss)
            percent = Plugin.BossHpPerPlayer.Value;
        else if (gm.mage)
            percent = Plugin.MageHpPerPlayer.Value;
        else if (gm.minion)
            percent = Plugin.MinionHpPerPlayer.Value;
        else if (gm.hazeBurnt)
            percent = Plugin.HazeburntHpPerPlayer.Value;
        else if (gm.mob)
            percent = Plugin.RegularEnemyHpPerPlayer.Value;
        else
            return 1f;

        if (percent <= 0f) return 1f;

        var allies = NetworkMgr.Instance?.GetAllyCount() ?? 0;
        if (allies < 0) allies = 0;
        return 1f + allies * percent;
    }

    /// <summary>
    /// Returns the configured damage multiplier for a character's category.
    /// </summary>
    private static float GetDamageMultiplier(Character character)
    {
        return GetCategory(character) switch
        {
            Category.Mage => Plugin.MageDamageMultiplier.Value,
            Category.Minion => Plugin.MinionDamageMultiplier.Value,
            Category.Mob => Plugin.RegularEnemyDamageMultiplier.Value,
            Category.Hazeburnt => Plugin.HazeburntDamageMultiplier.Value,
            _ => 1f
        };
    }

    /// <summary>
    /// Returns the configured poise multiplier for a character's category.
    /// </summary>
    private static float GetPoiseMultiplier(Character character)
    {
        return GetCategory(character) switch
        {
            Category.Mage => Plugin.MagePoiseMultiplier.Value,
            Category.Minion => Plugin.MinionPoiseMultiplier.Value,
            Category.Mob => Plugin.RegularEnemyPoiseMultiplier.Value,
            Category.Hazeburnt => Plugin.HazeburntPoiseMultiplier.Value,
            _ => 1f
        };
    }

    /// <summary>
    /// Returns the configured poise damage multiplier for a character's category.
    /// </summary>
    private static float GetPoiseDamageMultiplier(Character character)
    {
        return GetCategory(character) switch
        {
            Category.Mage => Plugin.MagePoiseDamageMultiplier.Value,
            Category.Minion => Plugin.MinionPoiseDamageMultiplier.Value,
            Category.Mob => Plugin.RegularEnemyPoiseDamageMultiplier.Value,
            Category.Hazeburnt => Plugin.HazeburntPoiseDamageMultiplier.Value,
            _ => 1f
        };
    }

    /// <summary>
    /// Returns the configured loot multiplier for a character's category.
    /// </summary>
    private static float GetLootMultiplier(Character character)
    {
        return GetCategory(character) switch
        {
            Category.Mage => Plugin.MageLootMultiplier.Value,
            Category.Minion => Plugin.MinionLootMultiplier.Value,
            Category.Mob => Plugin.RegularEnemyLootMultiplier.Value,
            Category.Hazeburnt => Plugin.HazeburntLootMultiplier.Value,
            _ => 1f
        };
    }

    /// <summary>
    /// Scales max HP for every monster category.
    /// GameMonster.GetMaxHP is the single funnel for monster HP: spawns, mage cycle math, arena heals, checkpoint resets and the player-side HP clamp all read through it, so scaling here stays consistent everywhere.
    /// </summary>
    [HarmonyPatch(typeof(GameMonster), "GetMaxHP")]
    [HarmonyPostfix]
    private static void GetMaxHpPatch(Character character, ref float __result)
    {
        if (character == null || character.playerIdx > -1) return;
        if (character.monsterIdx < 0 || character.monsterIdx >= MonsterCatalog.monsterDef.Count) return;
        var def = MonsterCatalog.monsterDef[character.monsterIdx];
        if (def.type != 1 && def.type != 6) return;

        var mult = GetHpMultiplier(character) * GetPerPlayerHpMultiplier(character);
        if (Math.Abs(mult - 1f) < 0.001f) return;
        __result *= mult;
    }

    /// <summary>
    /// Scales max poise for every monster category.
    /// GetMaxPoise is the funnel for poise on spawn, poise regen clamps and poise refills after a stagger.
    /// </summary>
    [HarmonyPatch(typeof(GameMonster), "GetMaxPoise")]
    [HarmonyPostfix]
    private static void GetMaxPoisePatch(Character character, ref float __result)
    {
        if (character == null || character.playerIdx > -1) return;
        if (character.monsterIdx < 0 || character.monsterIdx >= MonsterCatalog.monsterDef.Count) return;
        var def = MonsterCatalog.monsterDef[character.monsterIdx];
        if (def.type != 1 && def.type != 6) return;

        var mult = GetPoiseMultiplier(character);
        if (Math.Abs(mult - 1f) < 0.001f) return;
        __result *= mult;
    }

    /// <summary>
    /// Scales the damage dealt by monsters.
    /// GameMonster.PopulateHVals is the funnel for monster attack values (melee, ranged and magic), so scaling there covers every attack type.
    /// </summary>
    [HarmonyPatch(typeof(GameMonster), "PopulateHVals")]
    [HarmonyPostfix]
    private static void PopulateHValsPatch(float[] hVals, Character character)
    {
        if (character == null || character.playerIdx > -1) return;
        var mult = GetDamageMultiplier(character);
        if (Math.Abs(mult - 1f) < 0.001f) return;

        for (var i = 0; i < hVals.Length; i++)
            hVals[i] *= mult;
    }

    /// <summary>
    /// Scales the poise damage dealt by monsters.
    /// HitManager.GetPoiseAtkVal is the funnel for poise attack values for both players and monsters.
    /// </summary>
    [HarmonyPatch(typeof(HitManager), "GetPoiseAtkVal")]
    [HarmonyPostfix]
    private static void GetPoiseAtkValPatch(Particle p, ref float __result)
    {
        if (p == null || p.owner < 0 || p.owner >= CharMgr.character.Length) return;
        var owner = CharMgr.character[p.owner];
        if (owner == null || !owner.exists || owner.playerIdx > -1) return;

        var mult = GetPoiseDamageMultiplier(owner);
        if (Math.Abs(mult - 1f) < 0.001f) return;
        __result *= mult;
    }

    /// <summary>
    /// Scales the silver and material loot dropped by monsters.
    /// CharDeath.DropLoot is the funnel for all monster loot; the transpiler multiplies the silver amount (local 12, first store only, the other two stores are intermediate) and the material drop probability (local 17) by the category multiplier.
    /// Artifact and quest drops are left untouched.
    /// </summary>
    [HarmonyPatch(typeof(CharDeath), "DropLoot")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> DropLootTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var characterField = AccessTools.Field(typeof(CharDeath), "character");
        var getLootMult = AccessTools.Method(typeof(EnemyScalingPatch), nameof(GetLootMultiplier));

        var list = new List<CodeInstruction>(instructions);
        var patched = 0;
        var silverPatched = false;
        var probPatched = false;
        for (var i = 0; i < list.Count; i++)
        {
            if (list[i].opcode != OpCodes.Stloc_S || list[i].operand is not LocalBuilder lb) continue;

            // Silver amount: the first stloc.s 12 after the base silver value is computed.
            // The two later stores of local 12 are intermediate (random roll, charm scaling), patching those would cube the multiplier.
            if (!silverPatched && lb.LocalIndex == 12)
            {
                // stack: [..., silverValue]
                list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                list.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, characterField));
                list.Insert(i + 2, new CodeInstruction(OpCodes.Call, getLootMult));
                list.Insert(i + 3, new CodeInstruction(OpCodes.Mul));
                i += 4; // the original stloc is now at i+4, skip past it
                silverPatched = true;
                patched++;
                continue;
            }

            // Material probability: the single stloc.s 17 after the probability is computed.
            if (!probPatched && lb.LocalIndex == 17)
            {
                // stack: [..., probability]
                list.Insert(i, new CodeInstruction(OpCodes.Ldarg_0));
                list.Insert(i + 1, new CodeInstruction(OpCodes.Ldfld, characterField));
                list.Insert(i + 2, new CodeInstruction(OpCodes.Call, getLootMult));
                list.Insert(i + 3, new CodeInstruction(OpCodes.Mul));
                i += 4; // the original stloc is now at i+4, skip past it
                probPatched = true;
                patched++;
                continue;
            }
        }

        if (patched != 2)
            Plugin.Instance.Log.LogWarning($"[EnemyScalingPatch] Expected 2 loot stores in CharDeath.DropLoot, found {patched}; loot multipliers may be incomplete.");

        return list;
    }
}
