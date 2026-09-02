using System;
using System.IO;
using System.Reflection;
using System.Timers;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.NET.Common;
using HarmonyLib;
using ProjectMage;
using ProjectMage.gamestate.mage;
using ProjectMage.Monsters;
using System.Runtime.CompilerServices;

namespace SaS2MageTweaks;

[BepInPlugin(PluginInfo.PluginGuid, PluginInfo.PluginName, PluginInfo.PluginVersion)]
// ReSharper disable once StringLiteralTypo
[BepInDependency("amione.SaS2ModOptions", BepInDependency.DependencyFlags.SoftDependency)]
// ReSharper disable once ClassNeverInstantiated.Global
public class Plugin : BasePlugin
{
    internal static Plugin Instance;
    internal static MethodInfo GetPathNodeMethod;
    internal static MethodInfo SetPhaseMethod;
    internal static MethodInfo GetMaxHpMethod;
    internal static MethodInfo GetAddCharToArenaIdxMethod;
    internal static MethodInfo OnAddCharToArenaMethod;

    internal static ConfigEntry<bool> SkipNamedMages;
    internal static ConfigEntry<bool> SkipFatedMages;
    internal static ConfigEntry<bool> SkipNamelessMages;
    internal static ConfigEntry<bool> SkipGauntletMages;
    internal static ConfigEntry<bool> SkipWanderingMages;
    internal static ConfigEntry<bool> SpawnAtFinalLocation;
    internal static ConfigEntry<bool> DropLootRelativeAmount;
    internal static ConfigEntry<float> DropLootMultiplier;
    internal static ConfigEntry<bool> ReduceBossHp;
    internal static ConfigEntry<float> BossHpMultiplier;
    internal static ConfigEntry<float> HazeburntCountMultiplier;
    internal static ConfigEntry<float> WarpSummonCountMultiplier;
    internal static ConfigEntry<float> PhaseSummonCountMultiplier;
    internal static ConfigEntry<float> MinionHpMultiplier;
    internal static ConfigEntry<float> MinionDamageMultiplier;
    internal static ConfigEntry<bool> DisableWarpAndSummonMinions;
    internal static ConfigEntry<bool> DisableWarpAndAggressiveAttack;
    internal static ConfigEntry<bool> MagesWontHitMages;
    internal static ConfigEntry<bool> MinionsWontHitMinions;
    internal static ConfigEntry<bool> MagesWontHitMinions;
    internal static ConfigEntry<bool> MinionsWontHitMages;
    internal static ConfigEntry<bool> MagesWontHitHazeburnt;
    internal static ConfigEntry<bool> MinionsWontHitHazeburnt;
    internal static ConfigEntry<bool> MagesWontHitMobs;
    internal static ConfigEntry<bool> MinionsWontHitMobs;
    internal static ConfigEntry<bool> MobsWontHitMinions;
    internal static ConfigEntry<bool> MobsWontHitMages;

    private FileSystemWatcher _configWatcher;
    private Timer _debounceTimer;

    public override void Load()
    {
        Instance = this;

        SkipNamedMages          = Config.Bind("General", "SkipNamedMages",          false,  "Skip hunt phases for named mission mages (e.g. Arzhan-Tin, Celus Zend).");
        SkipFatedMages          = Config.Bind("General", "SkipFatedMages",          false,  "Skip hunt phases for fated mages (tiered mages shown with a tier number in mission select).");
        SkipNamelessMages       = Config.Bind("General", "SkipNamelessMages",       false,  "Skip hunt phases for nameless mission mages (repeatable hunts, reward token_nameless).");
        SkipGauntletMages       = Config.Bind("General", "SkipGauntletMages",       false,  "Skip hunt phases for gauntlet mages (each one immediately starts a boss fight).");
        SkipWanderingMages      = Config.Bind("General", "SkipWanderingMages",      false,  "Skip hunt phases for wandering/roaming mages.");
        SpawnAtFinalLocation    = Config.Bind("General", "SpawnAtFinalLocation",    false,  "Teleport the primary mission mage directly to its arena entrance when skipping. Off by default, mages spawn at zone 0 and walk to the arena naturally. Only affects the non-invisible target mage; companion mages in the same hunt should be unaffected.");
        DropLootRelativeAmount  = Config.Bind("Loot",    "DropLootRelativeAmount",  false,  "Drop bonus loot on death to compensate for skipped hunt phases.");
        DropLootMultiplier      = Config.Bind("Loot",    "DropLootMultiplier",      1.0f,   new ConfigDescription("Scales the bonus loot dropped per skipped phase. 1.0 = one extra phase-equivalent drop total.", new AcceptableValueRange<float>(0.1f, 10.0f)));
        ReduceBossHp            = Config.Bind("General", "ReduceBossHP",            false,  "Start boss fight with reduced HP (simulates hunt damage).");
        BossHpMultiplier        = Config.Bind("General", "BossHpMultiplier",        1.0f,   "Multiply mage starting HP by this value after the hunt-damage reduction.");
        HazeburntCountMultiplier = Config.Bind("General", "HazeburntCountMultiplier", 1.0f,  new ConfigDescription("Scales how many hazeburnt monsters can be active at once during hunts, invasions and roaming. 1.0 = vanilla (2 during hunts, 16 during Blue Heart invasions). 0 = no hazeburnt spawn.", new AcceptableValueRange<float>(0.0f, 10.0f)));
        WarpSummonCountMultiplier  = Config.Bind("Minions", "WarpSummonCountMultiplier",  1.0f, new ConfigDescription("Scales how many minions a mage summons each time it warps away and summons mid-hunt. 1.0 = vanilla (2 per warp summon). 0 = mages never summon minions when they warp. Also scales the fight's total minion pool when higher than the phase multiplier.", new AcceptableValueRange<float>(0.0f, 10.0f)));
        PhaseSummonCountMultiplier = Config.Bind("Minions", "PhaseSummonCountMultiplier", 1.0f, new ConfigDescription("Scales how many minions a mage summons each time it casts a summon during a hunt phase. 1.0 = vanilla (2 per phase summon). 0 = mages never summon minions during phases. Also scales the fight's total minion pool when higher than the warp multiplier.", new AcceptableValueRange<float>(0.0f, 10.0f)));
        MinionHpMultiplier      = Config.Bind("Minions", "MinionHpMultiplier",      1.0f,   new ConfigDescription("Scales the HP of minions summoned by mages. 1.0 = vanilla.", new AcceptableValueRange<float>(0.1f, 10.0f)));
        MinionDamageMultiplier  = Config.Bind("Minions", "MinionDamageMultiplier",  1.0f,   new ConfigDescription("Scales the damage dealt by minions summoned by mages. 1.0 = vanilla.", new AcceptableValueRange<float>(0.1f, 10.0f)));
        DisableWarpAndSummonMinions = Config.Bind("General", "DisableWarpAndSummonMinions", false, "Disable the ambush summon phase for mages that have it (they will not warp away and summon minions mid-hunt).");
        DisableWarpAndAggressiveAttack = Config.Bind("General", "DisableWarpAndAggressiveAttack", false, "Disable the ambush rage phase for mages that have it (they will not warp to the player and enter an aggressive attack state mid-hunt).");
        MagesWontHitMages       = Config.Bind("Mages", "MagesWontHitMages",       false, "Mages will not damage or aggro other mages.");
        MinionsWontHitMinions   = Config.Bind("Minions", "MinionsWontHitMinions", false, "Minions will not damage or aggro other minions.");
        MagesWontHitMinions     = Config.Bind("Mages", "MagesWontHitMinions",     false, "Mages will not damage or aggro minions.");
        MinionsWontHitMages     = Config.Bind("Minions", "MinionsWontHitMages",   false, "Minions will not damage or aggro mages.");
        MagesWontHitHazeburnt   = Config.Bind("Mages", "MagesWontHitHazeburnt",   false, "Mages will not damage or aggro hazeburnt monsters.");
        MinionsWontHitHazeburnt = Config.Bind("Minions", "MinionsWontHitHazeburnt", false, "Minions will not damage or aggro hazeburnt monsters.");
        MagesWontHitMobs        = Config.Bind("Mages", "MagesWontHitMobs",        false, "Mages will not damage or aggro regular enemies (mobs).");
        MinionsWontHitMobs      = Config.Bind("Minions", "MinionsWontHitMobs",    false, "Minions will not damage or aggro regular enemies (mobs).");
        MobsWontHitMinions      = Config.Bind("General", "MobsWontHitMinions",    false, "Regular enemies (mobs) will not damage or aggro minions.");
        MobsWontHitMages        = Config.Bind("General", "MobsWontHitMages",      false, "Regular enemies (mobs) will not damage or aggro mages.");

        var modOptionsType = Type.GetType("SaS2ModOptions.SaS2ModOptions, amione.SaS2ModOptions");
        if (modOptionsType != null)
        {
            TryRegisterModOptions();
            Instance.Log.LogInfo("Successfully registered configs with SaS2ModOptions.");
        }
        else
        {
            Instance.Log.LogInfo("Mod Options not installed; config file only.");
        }

        GetPathNodeMethod = AccessTools.Method(typeof(Mage), "GetPathNode");
        if (GetPathNodeMethod == null)
            Instance.Log.LogWarning("GetPathNode not found, SpawnAtFinalLocation will be disabled.");

        SetPhaseMethod = AccessTools.Method(typeof(Mage), "SetPhase");
        if (SetPhaseMethod == null)
            Instance.Log.LogWarning("SetPhase not found, hunt phase skipping may not work correctly.");

        GetMaxHpMethod = AccessTools.Method(typeof(GameMonster), "GetMaxHP");
        if (GetMaxHpMethod == null)
            Instance.Log.LogWarning("GetMaxHP not found, HP capping on boss promotion will be skipped.");

        GetAddCharToArenaIdxMethod = AccessTools.Method(
            typeof(ProjectMage.map.arena.MapArenas), "GetAddCharToArenaIdx");
        if (GetAddCharToArenaIdxMethod == null)
            Instance.Log.LogWarning("GetAddCharToArenaIdx not found, boss promotion may require one extra warp.");

        OnAddCharToArenaMethod = AccessTools.Method(typeof(NetworkEvents), "OnAddCharToArena");
        if (OnAddCharToArenaMethod == null)
            Instance.Log.LogWarning("OnAddCharToArena not found, boss promotion may require one extra warp.");

        var configDirectory = Path.GetDirectoryName(Config.ConfigFilePath);
        var configFileName  = Path.GetFileName(Config.ConfigFilePath);
        if (!string.IsNullOrEmpty(configDirectory))
        {
            _configWatcher = new FileSystemWatcher(configDirectory, configFileName)
            {
                NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.Size,
                EnableRaisingEvents = true
            };
            _debounceTimer = new Timer(1000) { AutoReset = false };
            _debounceTimer.Elapsed += (_, _) =>
            {
                Config.Reload();
                Instance.Log.LogInfo("Configuration reloaded.");
            };
            _configWatcher.Changed += (_, _) => { _debounceTimer.Stop(); _debounceTimer.Start(); };
        }
        else
        {
            Instance.Log.LogWarning("Could not determine config directory, live reload disabled.");
        }

        var harmony = new Harmony(PluginInfo.PluginGuid);
        harmony.PatchAll();
        Instance.Log.LogInfo($"{PluginInfo.PluginName} v{PluginInfo.PluginVersion} loaded.");
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private static void TryRegisterModOptions()
    {
        // ReSharper disable RedundantAssignment
        var order = 0;
        string cat;

        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SkipNamedMages,         			cat = "Mages - Skip Hunt Chases", "Skip Named Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SkipFatedMages,         			cat, "Skip Fated Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SkipNamelessMages,					cat, "Skip Nameless Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SkipGauntletMages,					cat, "Skip Gauntlet Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SkipWanderingMages,     			cat, "Skip Wandering Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SpawnAtFinalLocation,   			cat, "Spawn At Final Location", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(DisableWarpAndSummonMinions,    	cat, "Disable Warp And Summon Minions", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(DisableWarpAndAggressiveAttack,  	cat, "Disable Warp And Aggressive Attack", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(DropLootRelativeAmount, 			cat = "Mages - General", "Extra Loot Based on Skipped Phases", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(DropLootMultiplier,     			cat, "Loot Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(ReduceBossHp,           			cat, "Reduce Boss HP", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(BossHpMultiplier,       			cat, "Boss HP Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(HazeburntCountMultiplier, 		cat, "Hazeburnt Count Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(WarpSummonCountMultiplier,  		cat = "Mages - Minions", "Warp Summon Count Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(PhaseSummonCountMultiplier, 		cat, "Phase Summon Count Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionHpMultiplier,           		cat, "Minion HP Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionDamageMultiplier,       		cat, "Minion Damage Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionsWontHitMinions,        		cat, "Minions Won't Hit Other Minions", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionsWontHitMages,          		cat, "Minions Won't Hit Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionsWontHitHazeburnt,      		cat, "Minions Won't Hit Hazeburnt", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionsWontHitMobs,           		cat, "Minions Won't Hit Regular Enemies", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MagesWontHitMages,            		cat = "Mages - General", "Won't Hit Other Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MagesWontHitMinions,          		cat, "Mages Won't Hit Minions", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MagesWontHitHazeburnt,        		cat, "Mages Won't Hit Hazeburnt", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MagesWontHitMobs,             		cat, "Mages Won't Hit Regular Enemies", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MobsWontHitMinions,           		cat, "Regular Enemies Won't Hit Minions", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MobsWontHitMages,             		cat, "Regular Enemies Won't Hit Mages", order += 1);
        // ReSharper restore RedundantAssignment
    }

    public override bool Unload()
    {
        _configWatcher?.Dispose();
        _debounceTimer?.Dispose();
        return base.Unload();
    }
}
