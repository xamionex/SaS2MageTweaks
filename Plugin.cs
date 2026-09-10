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
    internal static ConfigEntry<bool> ReduceMageHp;
    internal static ConfigEntry<float> MageHpMultiplier;
    internal static ConfigEntry<float> MageHpMultiplierFinalArena;
    internal static ConfigEntry<float> MageDamageMultiplier;
    internal static ConfigEntry<float> MagePoiseMultiplier;
    internal static ConfigEntry<float> MagePoiseDamageMultiplier;
    internal static ConfigEntry<float> MageLootMultiplier;
    internal static ConfigEntry<float> HazeburntCountMultiplier;
    internal static ConfigEntry<float> WarpSummonCountMultiplier;
    internal static ConfigEntry<float> PhaseSummonCountMultiplier;
    internal static ConfigEntry<float> MinionHpMultiplier;
    internal static ConfigEntry<float> MinionHpMultiplierFinalArena;
    internal static ConfigEntry<float> MinionDamageMultiplier;
    internal static ConfigEntry<float> MinionPoiseMultiplier;
    internal static ConfigEntry<float> MinionPoiseDamageMultiplier;
    internal static ConfigEntry<float> MinionLootMultiplier;
    internal static ConfigEntry<float> RegularEnemyHpMultiplier;
    internal static ConfigEntry<float> RegularEnemyDamageMultiplier;
    internal static ConfigEntry<float> RegularEnemyPoiseMultiplier;
    internal static ConfigEntry<float> RegularEnemyPoiseDamageMultiplier;
    internal static ConfigEntry<float> RegularEnemyLootMultiplier;
    internal static ConfigEntry<float> HazeburntHpMultiplier;
    internal static ConfigEntry<float> HazeburntDamageMultiplier;
    internal static ConfigEntry<float> HazeburntPoiseMultiplier;
    internal static ConfigEntry<float> HazeburntPoiseDamageMultiplier;
    internal static ConfigEntry<float> HazeburntLootMultiplier;
    internal static ConfigEntry<bool> EnemyHpScalingPerPlayer;
    internal static ConfigEntry<float> HazeburntHpPerPlayer;
    internal static ConfigEntry<float> RegularEnemyHpPerPlayer;
    internal static ConfigEntry<float> MinionHpPerPlayer;
    internal static ConfigEntry<float> MageHpPerPlayer;
    internal static ConfigEntry<float> BossHpPerPlayer;
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
    internal static ConfigEntry<bool> MobsWontHitHazeburnt;
    internal static ConfigEntry<bool> MagesTakeFullDamageDuringWarpIn;
    internal static ConfigEntry<bool> MinionsTakeFullDamageDuringWarpIn;
    internal static ConfigEntry<float> MageRunSpeedMultiplier;
    internal static ConfigEntry<float> MinionRunSpeedMultiplier;
    internal static ConfigEntry<float> RegularEnemyRunSpeedMultiplier;
    internal static ConfigEntry<float> HazeburntRunSpeedMultiplier;
    internal static ConfigEntry<bool> EnemiesWontHitOtherEnemies;
    internal static ConfigEntry<bool> EnemiesTakeFullDamageDuringWarpIn;
    internal static ConfigEntry<bool> ApplyGlobalTweaksToMages;
    internal static ConfigEntry<bool> ApplyGlobalTweaksToMinions;
    internal static ConfigEntry<bool> ApplyGlobalTweaksToRegularEnemies;
    internal static ConfigEntry<bool> ApplyGlobalTweaksToHazeburnt;

    private FileSystemWatcher _configWatcher;
    private Timer _debounceTimer;

    public override void Load()
    {
        Instance = this;

        SkipNamedMages          = Config.Bind("General", "SkipNamedMages",          false,  "Skip hunt phases for named mission mages (e.g. Arzhan-Tin, Celus Zend).");
        SkipFatedMages          = Config.Bind("General", "SkipFatedMages",          false,  "Skip hunt phases for fated mages (tiered mages shown with a tier number in mission select).");
        SkipNamelessMages       = Config.Bind("General", "SkipNamelessMages",       false,  "Skip hunt phases for nameless mission mages (repeatable hunts, reward token_nameless).");
        SkipGauntletMages       = Config.Bind("General", "SkipGauntletMages",       false,  "Skip hunt phases for gauntlet mages (each one immediately starts a 'boss' fight).");
        SkipWanderingMages      = Config.Bind("General", "SkipWanderingMages",      false,  "Skip hunt phases for wandering/roaming mages.");
        SpawnAtFinalLocation    = Config.Bind("General", "SpawnAtFinalLocation",    false,  "Teleport the primary mission mage directly to its arena entrance when skipping. Off by default, mages spawn at zone 0 and walk to the arena naturally. Only affects the non-invisible target mage; companion mages in the same hunt should be unaffected.");
        DropLootRelativeAmount  = Config.Bind("Loot",    "DropLootRelativeAmount",  false,  "Drop bonus loot on death to compensate for skipped hunt phases.");
        DropLootMultiplier      = Config.Bind("Loot",    "DropLootMultiplier",      1.0f,   "Scales the bonus loot dropped per skipped phase. 1.0 = one extra phase-equivalent drop total.");
        ReduceMageHp            = Config.Bind("General", "ReduceMageHP",            false,  "Start mage fight with reduced HP (simulates hunt damage).");
        MageHpMultiplier        = Config.Bind("General", "MageHpMultiplier",        1.0f,   "Multiply mage starting HP by this value after the hunt-damage reduction.");
        MageHpMultiplierFinalArena = Config.Bind("Mages", "MageHpMultiplierFinalArena", 1.0f, "Multiply mage HP by this value only in the final arena phase, when the mage becomes a boss. 1.0 = vanilla.");
        MageDamageMultiplier    = Config.Bind("Mages", "MageDamageMultiplier",     1.0f,   "Scales the damage dealt by mages. 1.0 = vanilla.");
        MagePoiseMultiplier     = Config.Bind("Mages", "MagePoiseMultiplier",      1.0f,   "Scales the poise (stagger resistance) of mages. 1.0 = vanilla.");
        MagePoiseDamageMultiplier = Config.Bind("Mages", "MagePoiseDamageMultiplier", 1.0f, "Scales the poise damage dealt by mages. 1.0 = vanilla.");
        MageLootMultiplier      = Config.Bind("Mages", "MageLootMultiplier",       1.0f,   "Scales the silver and material loot dropped by mages. 1.0 = vanilla.");
        HazeburntCountMultiplier = Config.Bind("General", "HazeburntCountMultiplier", 1.0f,  "Scales how many hazeburnt monsters can be active at once during hunts, invasions and roaming. 1.0 = vanilla (2 during hunts, 16 during Blue Heart invasions). 0 = no hazeburnt spawn.");
        WarpSummonCountMultiplier  = Config.Bind("Minions", "WarpSummonCountMultiplier",  1.0f, "Scales how many minions a mage summons each time it warps away and summons mid-hunt. 1.0 = vanilla (2 per warp summon). 0 = mages never summon minions when they warp. Also scales the fight's total minion pool when higher than the phase multiplier.");
        PhaseSummonCountMultiplier = Config.Bind("Minions", "PhaseSummonCountMultiplier", 1.0f, "Scales how many minions a mage summons each time it casts a summon during a hunt phase. 1.0 = vanilla (2 per phase summon). 0 = mages never summon minions during phases. Also scales the fight's total minion pool when higher than the warp multiplier.");
        MinionHpMultiplier      = Config.Bind("Minions", "MinionHpMultiplier",      1.0f,   "Scales the HP of minions summoned by mages. 1.0 = vanilla.");
        MinionHpMultiplierFinalArena = Config.Bind("Minions", "MinionHpMultiplierFinalArena", 1.0f, "Multiply minion HP by this value only while their mage is in the final arena phase (boss fight). 1.0 = vanilla.");
        MinionDamageMultiplier  = Config.Bind("Minions", "MinionDamageMultiplier",  1.0f,   "Scales the damage dealt by minions summoned by mages. 1.0 = vanilla.");
        MinionPoiseMultiplier   = Config.Bind("Minions", "MinionPoiseMultiplier",   1.0f,   "Scales the poise (stagger resistance) of minions summoned by mages. 1.0 = vanilla.");
        MinionPoiseDamageMultiplier = Config.Bind("Minions", "MinionPoiseDamageMultiplier", 1.0f, "Scales the poise damage dealt by minions summoned by mages. 1.0 = vanilla.");
        MinionLootMultiplier    = Config.Bind("Minions", "MinionLootMultiplier",    1.0f,   "Scales the silver and material loot dropped by minions. 1.0 = vanilla.");
        RegularEnemyHpMultiplier = Config.Bind("Regular Enemies", "RegularEnemyHpMultiplier", 1.0f, "Scales the HP of regular enemies (mobs). 1.0 = vanilla.");
        RegularEnemyDamageMultiplier = Config.Bind("Regular Enemies", "RegularEnemyDamageMultiplier", 1.0f, "Scales the damage dealt by regular enemies (mobs). 1.0 = vanilla.");
        RegularEnemyPoiseMultiplier = Config.Bind("Regular Enemies", "RegularEnemyPoiseMultiplier", 1.0f, "Scales the poise (stagger resistance) of regular enemies (mobs). 1.0 = vanilla.");
        RegularEnemyPoiseDamageMultiplier = Config.Bind("Regular Enemies", "RegularEnemyPoiseDamageMultiplier", 1.0f, "Scales the poise damage dealt by regular enemies (mobs). 1.0 = vanilla.");
        RegularEnemyLootMultiplier = Config.Bind("Regular Enemies", "RegularEnemyLootMultiplier", 1.0f, "Scales the silver and material loot dropped by regular enemies (mobs). 1.0 = vanilla.");
        HazeburntHpMultiplier   = Config.Bind("Hazeburnt", "HazeburntHpMultiplier", 1.0f,   "Scales the HP of hazeburnt monsters. 1.0 = vanilla.");
        HazeburntDamageMultiplier = Config.Bind("Hazeburnt", "HazeburntDamageMultiplier", 1.0f, "Scales the damage dealt by hazeburnt monsters. 1.0 = vanilla.");
        HazeburntPoiseMultiplier = Config.Bind("Hazeburnt", "HazeburntPoiseMultiplier", 1.0f, "Scales the poise (stagger resistance) of hazeburnt monsters. 1.0 = vanilla.");
        HazeburntPoiseDamageMultiplier = Config.Bind("Hazeburnt", "HazeburntPoiseDamageMultiplier", 1.0f, "Scales the poise damage dealt by hazeburnt monsters. 1.0 = vanilla.");
        HazeburntLootMultiplier = Config.Bind("Hazeburnt", "HazeburntLootMultiplier", 1.0f, "Scales the silver and material loot dropped by hazeburnt monsters. 1.0 = vanilla.");
        EnemyHpScalingPerPlayer = Config.Bind("Enemy HP Scaling", "EnemyHpScalingPerPlayer", false, "Scale enemy HP based on the number of players in the session. Off = vanilla scaling only.");
        HazeburntHpPerPlayer    = Config.Bind("Enemy HP Scaling", "HazeburntHpPerPlayer",    0.75f,  "Extra HP percent per additional player for hazeburnt monsters. 0.75 = +75% HP per extra player.");
        RegularEnemyHpPerPlayer = Config.Bind("Enemy HP Scaling", "RegularEnemyHpPerPlayer", 0.75f,  "Extra HP percent per additional player for regular enemies (mobs). 0.75 = +75% HP per extra player.");
        MinionHpPerPlayer       = Config.Bind("Enemy HP Scaling", "MinionHpPerPlayer",       0.75f,  "Extra HP percent per additional player for mage minions. 0.75 = +75% HP per extra player.");
        MageHpPerPlayer         = Config.Bind("Enemy HP Scaling", "MageHpPerPlayer",         1.0f,   "Extra HP percent per additional player for mages. 1.0 = +100% HP per extra player.");
        BossHpPerPlayer         = Config.Bind("Enemy HP Scaling", "BossHpPerPlayer",         1.0f,   "Extra HP percent per additional player for bosses (final arena mages, gauntlet mages, map bosses). 1.0 = +100% HP per extra player.");
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
        MobsWontHitHazeburnt   = Config.Bind("General", "MobsWontHitHazeburnt",  false, "Regular enemies (mobs) will not damage or aggro hazeburnt monsters.");
        MagesTakeFullDamageDuringWarpIn = Config.Bind("Mages", "MagesTakeFullDamageDuringWarpIn", false, "Mages take full damage while warping in instead of being invulnerable during the warp-in animation.");
        MinionsTakeFullDamageDuringWarpIn = Config.Bind("Minions", "MinionsTakeFullDamageDuringWarpIn", false, "Minions take full damage while warping in instead of being invulnerable during the warp-in animation.");
        MageRunSpeedMultiplier = Config.Bind("Global Tweaks", "MageRunSpeedMultiplier", 1.0f, "Scales the run speed of mages. 1.0 = vanilla.");
        MinionRunSpeedMultiplier = Config.Bind("Global Tweaks", "MinionRunSpeedMultiplier", 1.0f, "Scales the run speed of minions summoned by mages. 1.0 = vanilla.");
        RegularEnemyRunSpeedMultiplier = Config.Bind("Global Tweaks", "RegularEnemyRunSpeedMultiplier", 1.0f, "Scales the run speed of regular enemies (mobs). 1.0 = vanilla.");
        HazeburntRunSpeedMultiplier = Config.Bind("Global Tweaks", "HazeburntRunSpeedMultiplier", 1.0f, "Scales the run speed of hazeburnt monsters. 1.0 = vanilla.");
        EnemiesWontHitOtherEnemies = Config.Bind("Global Tweaks", "EnemiesWontHitOtherEnemies", false, "Enemies will not damage or aggro other enemies.");
        EnemiesTakeFullDamageDuringWarpIn = Config.Bind("Global Tweaks", "EnemiesTakeFullDamageDuringWarpIn", false, "Enemies take full damage while warping in instead of being invulnerable during the warp-in animation.");
        ApplyGlobalTweaksToMages = Config.Bind("Global Tweaks", "ApplyGlobalTweaksToMages", true, "Apply the Global Tweaks settings to mages.");
        ApplyGlobalTweaksToMinions = Config.Bind("Global Tweaks", "ApplyGlobalTweaksToMinions", true, "Apply the Global Tweaks settings to minions.");
        ApplyGlobalTweaksToRegularEnemies = Config.Bind("Global Tweaks", "ApplyGlobalTweaksToRegularEnemies", true, "Apply the Global Tweaks settings to regular enemies (mobs).");
        ApplyGlobalTweaksToHazeburnt = Config.Bind("Global Tweaks", "ApplyGlobalTweaksToHazeburnt", true, "Apply the Global Tweaks settings to hazeburnt monsters.");

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
        const string mod = "Mage Tweaks";
        string cat;

        SaS2ModOptions.SaS2ModOptions.RegisterConfig(DropLootRelativeAmount, 			mod, cat = "General", "Extra Loot Based on Skipped Phases", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(DropLootMultiplier,     			mod, cat, "Loot Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(ReduceMageHp,           			mod, cat, "Reduce Mage HP", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MageHpMultiplier,       			mod, cat, "Mage HP Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MageHpMultiplierFinalArena, 		mod, cat, "Mage HP Multiplier (Final Arena)", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MageDamageMultiplier,    			mod, cat, "Mage Damage Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MagePoiseMultiplier,     			mod, cat, "Mage Poise Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MagePoiseDamageMultiplier, 		mod, cat, "Mage Poise Damage Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MageLootMultiplier,       			mod, cat, "Mage Loot Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(HazeburntCountMultiplier, 			mod, cat, "Hazeburnt Count Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MagesWontHitMages,            		mod, cat, "Mages Won't Hit Other Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MagesWontHitMinions,          		mod, cat, "Mages Won't Hit Minions", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MagesWontHitHazeburnt,        		mod, cat, "Mages Won't Hit Hazeburnt", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MagesWontHitMobs,             		mod, cat, "Mages Won't Hit Regular Enemies", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MagesTakeFullDamageDuringWarpIn, 	mod, cat, "Mages Take Full Damage During Warp-In", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MobsWontHitMinions,           		mod, cat, "Regular Enemies Won't Hit Minions", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MobsWontHitMages,             		mod, cat, "Regular Enemies Won't Hit Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MobsWontHitHazeburnt,         		mod, cat, "Regular Enemies Won't Hit Hazeburnt", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MageRunSpeedMultiplier,     		mod, cat = "Global Tweaks", "Mage Run Speed Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionRunSpeedMultiplier,   		mod, cat, "Minion Run Speed Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(RegularEnemyRunSpeedMultiplier, 	mod, cat, "Regular Enemy Run Speed Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(HazeburntRunSpeedMultiplier, 		mod, cat, "Hazeburnt Run Speed Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(EnemiesWontHitOtherEnemies,  		mod, cat, "Enemies Won't Hit Other Enemies", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(EnemiesTakeFullDamageDuringWarpIn, 	mod, cat, "Enemies Take Full Damage During Warp-In", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(ApplyGlobalTweaksToMages,   		mod, cat, "Apply Settings To Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(ApplyGlobalTweaksToMinions,  		mod, cat, "Apply Settings To Minions", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(ApplyGlobalTweaksToRegularEnemies, 	mod, cat, "Apply Settings To Regular Enemies", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(ApplyGlobalTweaksToHazeburnt, 		mod, cat, "Apply Settings To Hazeburnt", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SkipNamedMages,         			mod, cat = "Skip Hunts", "Skip Named Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SkipFatedMages,         			mod, cat, "Skip Fated Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SkipNamelessMages,					mod, cat, "Skip Nameless Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SkipGauntletMages,					mod, cat, "Skip Gauntlet Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SkipWanderingMages,     			mod, cat, "Skip Wandering Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(SpawnAtFinalLocation,   			mod, cat, "Spawn At Final Location", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(DisableWarpAndSummonMinions,    	mod, cat, "Disable Warp And Summon Minions", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(DisableWarpAndAggressiveAttack,  	mod, cat, "Disable Warp And Aggressive Attack", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(WarpSummonCountMultiplier,  		mod, cat = "Minions", "Warp Summon Count Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(PhaseSummonCountMultiplier, 		mod, cat, "Phase Summon Count Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionHpMultiplier,           		mod, cat, "Minion HP Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionHpMultiplierFinalArena, 		mod, cat, "Minion HP Multiplier (Final Arena)", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionDamageMultiplier,       		mod, cat, "Minion Damage Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionPoiseMultiplier,        		mod, cat, "Minion Poise Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionPoiseDamageMultiplier,  		mod, cat, "Minion Poise Damage Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionLootMultiplier,         		mod, cat, "Minion Loot Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionsWontHitMinions,        		mod, cat, "Minions Won't Hit Other Minions", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionsWontHitMages,          		mod, cat, "Minions Won't Hit Mages", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionsWontHitHazeburnt,      		mod, cat, "Minions Won't Hit Hazeburnt", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionsWontHitMobs,           		mod, cat, "Minions Won't Hit Regular Enemies", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionsTakeFullDamageDuringWarpIn, 	mod, cat, "Minions Take Full Damage During Warp-In", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(RegularEnemyHpMultiplier,     		mod, cat = "Regular Enemies", "Regular Enemy HP Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(RegularEnemyDamageMultiplier, 		mod, cat, "Regular Enemy Damage Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(RegularEnemyPoiseMultiplier,  		mod, cat, "Regular Enemy Poise Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(RegularEnemyPoiseDamageMultiplier, 	mod, cat, "Regular Enemy Poise Damage Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(RegularEnemyLootMultiplier,   		mod, cat, "Regular Enemy Loot Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(HazeburntHpMultiplier,         		mod, cat = "Hazeburnt", "Hazeburnt HP Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(HazeburntDamageMultiplier,   		mod, cat, "Hazeburnt Damage Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(HazeburntPoiseMultiplier,     		mod, cat, "Hazeburnt Poise Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(HazeburntPoiseDamageMultiplier,		mod, cat, "Hazeburnt Poise Damage Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(HazeburntLootMultiplier,      		mod, cat, "Hazeburnt Loot Multiplier", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(EnemyHpScalingPerPlayer,      		mod, cat = "Enemy HP Scaling", "Enemy HP Scaling Per Player", order += 1);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(HazeburntHpPerPlayer,         		mod, cat, "Hazeburnt Bonus HP Per Player", order += 1, showAsPercent: true);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(RegularEnemyHpPerPlayer,      		mod, cat, "Regular Enemy Bonus HP Per Player", order += 1, showAsPercent: true);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MinionHpPerPlayer,            		mod, cat, "Mage Minion Bonus HP Per Player", order += 1, showAsPercent: true);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(MageHpPerPlayer,              		mod, cat, "Mage Bonus HP Per Player", order += 1, showAsPercent: true);
        SaS2ModOptions.SaS2ModOptions.RegisterConfig(BossHpPerPlayer,              		mod, cat, "Boss Bonus HP Per Player", order += 1, showAsPercent: true);
        // ReSharper restore RedundantAssignment
    }

    public override bool Unload()
    {
        _configWatcher?.Dispose();
        _debounceTimer?.Dispose();
        return base.Unload();
    }
}
