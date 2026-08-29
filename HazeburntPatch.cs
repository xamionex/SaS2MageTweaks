using System;
using System.Collections.Generic;
using System.Reflection;
using System.Reflection.Emit;
using HarmonyLib;
using ProjectMage.gamestate.hazeburnt;

namespace SaS2MageTweaks;

[HarmonyPatch]
internal static class HazeburntPatch
{
    private static readonly MethodInfo ScaleCapMethod =
        AccessTools.Method(typeof(HazeburntPatch), nameof(ScaleCap));

    /// Scales the hazeburnt population cap. Vanilla caps: 2 during hunts/roaming, 16 during Blue Heart invasions.
    internal static int ScaleCap(int vanilla)
    {
        var multiplier = Plugin.HazeburntCountMultiplier.Value;
        if (Math.Abs(multiplier - 1f) < 0.001f) return vanilla;
        return (int)Math.Round(vanilla * multiplier);
    }

    /// Returns the integer loaded by any of the ldc.i4 forms, or null if the instruction is not an int load.
    private static int? GetLdcI4Value(CodeInstruction instr)
    {
        if (instr.opcode == OpCodes.Ldc_I4 && instr.operand is int i) return i;
        if (instr.opcode == OpCodes.Ldc_I4_S)
        {
            if (instr.operand is sbyte sb) return sb;
            if (instr.operand is byte b) return b;
        }
        if (instr.opcode == OpCodes.Ldc_I4_M1) return -1;
        if (instr.opcode == OpCodes.Ldc_I4_0) return 0;
        if (instr.opcode == OpCodes.Ldc_I4_1) return 1;
        if (instr.opcode == OpCodes.Ldc_I4_2) return 2;
        if (instr.opcode == OpCodes.Ldc_I4_3) return 3;
        if (instr.opcode == OpCodes.Ldc_I4_4) return 4;
        if (instr.opcode == OpCodes.Ldc_I4_5) return 5;
        if (instr.opcode == OpCodes.Ldc_I4_6) return 6;
        if (instr.opcode == OpCodes.Ldc_I4_7) return 7;
        if (instr.opcode == OpCodes.Ldc_I4_8) return 8;
        return null;
    }

    private static int GetLocalIndex(CodeInstruction instr)
    {
        if (instr.opcode == OpCodes.Stloc_0) return 0;
        if (instr.opcode == OpCodes.Stloc_1) return 1;
        if (instr.opcode == OpCodes.Stloc_2) return 2;
        if (instr.opcode == OpCodes.Stloc_3) return 3;
        return instr.operand switch
        {
            LocalBuilder lb => lb.LocalIndex,
            int idx => idx,
            byte b => b,
            sbyte sb => sb,
            _ => -1
        };
    }

    /// HazeburntMgr.Update computes the population cap in local 7:
    ///   ldc.i4.2        (hunts/roaming cap)
    ///   stloc.s 7
    ///   ...
    ///   ldc.i4.s 0x10   (Blue Heart invasion cap)
    ///   stloc.s 7
    /// Both constants are replaced with ScaleCap(vanilla) so the configured multiplier applies to every context.
    /// Only ldc instructions immediately followed by a store into local 7 are patched, so the other
    /// ldc.i4.2 in the method (the face roll upper bound for GetRandomInt) is left untouched.
    [HarmonyPatch(typeof(HazeburntMgr), "Update")]
    [HarmonyTranspiler]
    private static IEnumerable<CodeInstruction> UpdateTranspiler(IEnumerable<CodeInstruction> instructions)
    {
        var list = new List<CodeInstruction>(instructions);
        var patched = 0;
        for (var i = 0; i < list.Count; i++)
        {
            var value = GetLdcI4Value(list[i]);
            if (value != 2 && value != 16) continue;

            // Only treat it as a cap when the very next instruction stores into local 7.
            if (i + 1 >= list.Count) continue;
            if (GetLocalIndex(list[i + 1]) != 7) continue;

            // Replace ldc with ldc + call ScaleCap(int) -> int. The following stloc.s 7 stays as-is.
            list[i] = new CodeInstruction(OpCodes.Ldc_I4, value.Value);
            list.Insert(i + 1, new CodeInstruction(OpCodes.Call, ScaleCapMethod));
            i++; // skip the inserted call
            patched++;
        }

        if (patched != 2)
            Plugin.Instance.Log.LogWarning($"[HazeburntPatch] Expected 2 cap assignments, found {patched}; Hazeburnt Count Multiplier may be incomplete.");

        return list;
    }
}
