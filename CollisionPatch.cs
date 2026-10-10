using System.Collections.Generic;
using System.Reflection.Emit;
using HarmonyLib;
using ProjectMage.character;
using ProjectMage.Monsters;

namespace SaS2MageTweaks;

/// <summary>
/// Enemy pass-through: configured pairs of enemy types overlap instead of pushing each other apart.
///
/// CharCols.CheckCharCols resolves the physical push for every pair of active characters it finds.
/// Its guard contains the vanilla clause "(!a.gameMonster.noBossCol || !b.gameMonster.noBossCol)", which the transpiler replaces with CollisionPatch.CanCollide(a, b).
/// The replacement keeps the vanilla noBossCol behavior and additionally drops the pair when the two enemy types are configured to pass through each other.
///
/// The replacement is a straight drop-in for the clause:
///   IL_0473: ldloc.1            -> ldloc.1      (c's GameMonster)
///   IL_0474: ldfld noBossCol    -> ldloc.s V_18 (the other GameMonster)
///   IL_0479: brfalse.s IL_0487  -> call CanCollide(GameMonster, GameMonster)
///   IL_047b: ldloc.s V_18       -> brfalse IL_09a8
///   IL_047d: ldfld noBossCol
///   IL_0482: brtrue IL_09a8
/// The branch that used to skip the pair when noBossCol was set is kept as the branch taken when CanCollide returns false, so a failed match leaves the method byte-for-byte equivalent.
/// </summary>
[HarmonyPatch]
internal static class CollisionPatch
{
    internal enum EnemyKind
    {
        None = 0,
        Mage = 1,
        Minion = 2,
        Mob = 3,
        Hazeburnt = 4,
    }

    /// Buckets a monster into one of the four configurable enemy types.
    /// Hazeburnt wins over mob (hazeburnt monsters are mobs with the hazeburnt flag), then the more specific summon kinds win over mob.
    internal static EnemyKind KindOf(GameMonster gm)
    {
        if (gm == null) return EnemyKind.None;
        if (gm.hazeBurnt) return EnemyKind.Hazeburnt;
        if (gm.mage) return EnemyKind.Mage;
        if (gm.minion) return EnemyKind.Minion;
        if (gm.mob) return EnemyKind.Mob;
        return EnemyKind.None;
    }

    /// True when the two enemy types are configured to pass through each other.
    /// The toggles are symmetric: either side of the pair disables the collision for both.
    internal static bool PassThrough(EnemyKind a, EnemyKind b)
    {
        if (a == EnemyKind.None || b == EnemyKind.None || a == b) return false;
        var lo = a < b ? a : b;
        var hi = a < b ? b : a;
        return lo switch
        {
            EnemyKind.Mage when hi == EnemyKind.Minion => Plugin.MagesWontCollideWithMinions.Value,
            EnemyKind.Mage when hi == EnemyKind.Mob => Plugin.MagesWontCollideWithRegularEnemies.Value,
            EnemyKind.Mage when hi == EnemyKind.Hazeburnt => Plugin.MagesWontCollideWithHazeburnt.Value,
            EnemyKind.Mage => false,
            EnemyKind.Minion when hi == EnemyKind.Mob => Plugin.MinionsWontCollideWithRegularEnemies.Value,
            EnemyKind.Minion when hi == EnemyKind.Hazeburnt => Plugin.MinionsWontCollideWithHazeburnt.Value,
            EnemyKind.Minion => false,
            EnemyKind.Mob when hi == EnemyKind.Hazeburnt => Plugin.RegularEnemiesWontCollideWithHazeburnt.Value,
            _ => false
        };
    }

    /// Stand-in for the vanilla "(!a.noBossCol || !b.noBossCol)" clause of the collision guard.
    /// Returning false makes CheckCharCols skip the pair entirely (no push, no avoid-job roll).
    internal static bool CanCollide(GameMonster a, GameMonster b)
    {
        if (a == null || b == null) return true;
        if (a.noBossCol || b.noBossCol) return false;
        return !PassThrough(KindOf(a), KindOf(b));
    }

    private static bool IsLocalLoad(CodeInstruction instr)
    {
        return instr.opcode == OpCodes.Ldloc
               || instr.opcode == OpCodes.Ldloc_S
               || instr.opcode == OpCodes.Ldloc_0
               || instr.opcode == OpCodes.Ldloc_1
               || instr.opcode == OpCodes.Ldloc_2
               || instr.opcode == OpCodes.Ldloc_3;
    }

    [HarmonyPatch(typeof(CharCols), "CheckCharCols")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> CheckCharColsTranspiler(
        IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var noBossCol = AccessTools.Field(typeof(GameMonster), nameof(GameMonster.noBossCol));

        var firstLdfld = -1;
        for (var i = 1; i < list.Count; i++)
        {
            if (list[i].opcode == OpCodes.Ldfld && Equals(list[i].operand, noBossCol))
            {
                firstLdfld = i;
                break;
            }
        }

        if (firstLdfld < 1)
        {
            Plugin.Instance.Log.LogWarning("[CollisionPatch] noBossCol clause not found; pass-through disabled.");
            return list;
        }

        // "ldloc a; ldfld noBossCol; brfalse next-test"
        if (!list[firstLdfld + 1].opcode.Name.StartsWith("brfalse")
            || !IsLocalLoad(list[firstLdfld - 1]))
        {
            Plugin.Instance.Log.LogWarning("[CollisionPatch] Unexpected noBossCol guard shape; pass-through disabled.");
            return list;
        }

        var secondLdfld = -1;
        for (var i = firstLdfld + 2; i < list.Count; i++)
        {
            if (list[i].opcode == OpCodes.Ldfld && Equals(list[i].operand, noBossCol))
            {
                secondLdfld = i;
                break;
            }
        }

        if (secondLdfld < 1)
        {
            Plugin.Instance.Log.LogWarning(
                "[CollisionPatch] Second noBossCol test not found; pass-through disabled.");
            return list;
        }

        // "ldloc b; ldfld noBossCol; brtrue skip-pair"
        if (!list[secondLdfld + 1].opcode.Name.StartsWith("brtrue")
            || !IsLocalLoad(list[secondLdfld - 1]))
        {
            Plugin.Instance.Log.LogWarning(
                "[CollisionPatch] Unexpected noBossCol second test; pass-through disabled.");
            return list;
        }

        // The two locals must be different (c's and the other character's GameMonster).
        if (Equals(list[firstLdfld - 1].operand, list[secondLdfld - 1].operand))
        {
            Plugin.Instance.Log.LogWarning(
                "[CollisionPatch] noBossCol tests share a local; pass-through disabled.");
            return list;
        }

        var skipTarget = list[secondLdfld + 1].operand;
        var start = firstLdfld - 1;
        var end = secondLdfld + 1;

        var replacement = new List<CodeInstruction>
        {
            new(list[start].opcode, list[start].operand),
            new(list[secondLdfld - 1].opcode, list[secondLdfld - 1].operand),
            new(OpCodes.Call, AccessTools.Method(typeof(CollisionPatch), nameof(CanCollide))),
            new(OpCodes.Brfalse, skipTarget),
        };

        // Any label that pointed into the replaced range now points at the replacement.
        for (var i = start; i <= end; i++)
        {
            foreach (var label in list[i].labels)
            {
                replacement[0].labels.Add(label);
            }
        }

        list.RemoveRange(start, end - start + 1);
        list.InsertRange(start, replacement);
        return list;
    }
}