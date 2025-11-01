﻿using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;

namespace CombatsAdditions;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class IndependentOptionPlugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    public static bool useQOLPatch = false;
    public static Dictionary<WeaponManager, Dictionary<string, List<int>>> weaponManagerToIndexMap = new Dictionary<WeaponManager, Dictionary<string, List<int>>>();
    public static Dictionary<WeaponManager, HardpointSet[]> weaponManagerToOriginalSets = new Dictionary<WeaponManager, HardpointSet[]>();

    private void Awake()
    {
        Logger = base.Logger;
        var harmony = new Harmony("IndependentOption");

        useQOLPatch = AppDomain.CurrentDomain.GetAssemblies()
            .Any(assembly => assembly.GetTypes().Any(type => type.Name == "QOLPlugin"));

        if (useQOLPatch)
        {
            Logger.LogInfo("QOLPlugin detected, using QOLPlugin patch");
            PatchQOLPlugin(harmony);
        }
        else
        {
            Logger.LogInfo("QOLPlugin not detected, using Encyclopedia patch");
            harmony.CreateClassProcessor(typeof(EncyclopediaPatch)).Patch();
        }
        harmony.CreateClassProcessor(typeof(VersionGetterPatch)).Patch();
        harmony.CreateClassProcessor(typeof(SavedLoadoutPatch)).Patch(); // Add this line
        Logger.LogInfo($"Independent Option {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }
    private void PatchQOLPlugin(Harmony harmony)
    {
        try
        {
            // Find QOLPlugin type dynamically
            Type qolPluginType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "QOLPlugin");

            if (qolPluginType == null)
            {
                Logger.LogError("QOLPlugin type not found despite detection");
                return;
            }

            // Find the ProcessConfigLinesSingleThread method
            MethodInfo originalMethod = qolPluginType.GetMethod("ProcessConfigLinesSingleThread",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            if (originalMethod == null)
            {
                Logger.LogError("ProcessConfigLinesSingleThread method not found");
                return;
            }

            // Create postfix method
            MethodInfo postfixMethod = typeof(IndependentOptionPlugin).GetMethod(nameof(QOLPluginPostfix),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

            // Apply patch
            harmony.Patch(originalMethod, postfix: new HarmonyMethod(postfixMethod));
            Logger.LogInfo("Successfully patched QOLPlugin.ProcessConfigLinesSingleThread");
        }
        catch (Exception ex)
        {
            Logger.LogError($"Failed to patch QOLPlugin: {ex}");
        }
    }

    private static IEnumerator QOLPluginPostfix(IEnumerator original)
    {
        while (original.MoveNext())
        {
            yield return original.Current;
        }
        SplitHardpoints();
        ModifyUI();
        Logger.LogWarning("Hardpoints Split (QOL)");
    }

    public static void ModifyUI()
    {
        GameObject result = null;
        if (useQOLPatch)
        {
            Type qolPluginType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "QOLPlugin");

            if (qolPluginType != null)
            {
                MethodInfo method = qolPluginType.GetMethod("FindGameObjectByExactPath",
                    BindingFlags.NonPublic | BindingFlags.Static);

                if (method != null)
                {
                    result = (GameObject)method.Invoke(null, new object[] { "HardpointSetSelector/HardpointSetDropdown/Template", true });
                }
            }
        }

        if (result == null)
        {
            result = FindGameObjectByExactPath("HardpointSetSelector/HardpointSetDropdown/Template");
        }

        if (result == null)
        {
            Logger.LogWarning("Could not find HardpointSetDropdown Template");
            return;
        }

        var transform = result.GetComponent<RectTransform>();
        transform.offsetMin += new Vector2(-60f, 0f);
        transform.offsetMax += new Vector2(60f, 0f);
    }
    public static void SplitHardpoints()
    {
        foreach (var aircraft in Resources.FindObjectsOfTypeAll<Aircraft>())
        {
            Logger.LogInfo($"Processing aircraft: {aircraft.name}");
            var weaponManager = aircraft.weaponManager;
            var originalHardpointSets = weaponManager.hardpointSets;
            weaponManagerToOriginalSets[weaponManager] = originalHardpointSets;
            var groupedHardpointSets = GroupHardpointsByName(weaponManager);
            var independentHardpointSets = CreateIndependentHardpointSets(groupedHardpointSets);
            var nameToIndexMap = BuildNameToIndexMap(weaponManager, independentHardpointSets);
            weaponManagerToIndexMap[weaponManager] = nameToIndexMap;

            aircraft.GetAircraftParameters().loadouts = UpdateLoadouts(independentHardpointSets, nameToIndexMap, aircraft.GetAircraftParameters().loadouts, originalHardpointSets);
            var newStandardLoadouts = UpdateLoadouts(independentHardpointSets, nameToIndexMap, [.. aircraft.GetAircraftParameters().StandardLoadouts.Select(x => x.loadout)], originalHardpointSets);

            for (byte i = 0; i < newStandardLoadouts.Count; i++)
            {
                aircraft.GetAircraftParameters().StandardLoadouts[i].loadout = newStandardLoadouts[i];
            }

            UpdatePrecludingSets(independentHardpointSets, nameToIndexMap);
            weaponManager.hardpointSets = [.. independentHardpointSets];
        }
    }

    private static Dictionary<string, List<int>> BuildNameToIndexMap(WeaponManager weaponManager, List<HardpointSet> independentSets)
    {
        var map = new Dictionary<string, List<int>>();

        for (int i = 0; i < independentSets.Count; i++)
        {
            var set = independentSets[i];
            var originalName = set.name.StartsWith("Left ") || set.name.StartsWith("Right ")
                ? set.name.Substring(set.name.IndexOf(' ') + 1)
                : set.name;

            if (!map.ContainsKey(originalName))
            {
                map[originalName] = new List<int>();
            }
            map[originalName].Add(i);
        }

        return map;
    }

    public static Dictionary<string, List<HardpointSet>> GroupHardpointsByName(WeaponManager weaponManager)
    {
        var grouped = new Dictionary<string, List<HardpointSet>>();

        foreach (var hardpointSet in weaponManager.hardpointSets)
        {
            foreach (var hardpoint in hardpointSet.hardpoints)
            {
                var independentSet = CopyHardpointSet(hardpointSet);
                independentSet.hardpoints = [hardpoint];

                if (!grouped.ContainsKey(independentSet.name))
                {
                    grouped[independentSet.name] = [];
                }
                grouped[independentSet.name].Add(independentSet);
            }
        }

        return grouped;
    }

    public static List<HardpointSet> CreateIndependentHardpointSets(Dictionary<string, List<HardpointSet>> grouped)
    {
        var independentSets = new List<HardpointSet>();

        foreach (var group in grouped)
        {
            foreach (var hardpointSet in group.Value)
            {
                if (group.Value.Count > 1)
                {
                    var side = GetHardpointSide(hardpointSet);
                    hardpointSet.name = $"{side} {hardpointSet.name}";
                }
                independentSets.Add(hardpointSet);
            }
        }

        return independentSets;
    }

    public static void UpdatePrecludingSets(List<HardpointSet> independentSets, Dictionary<string, List<int>> nameToIndexMap)
    {
        foreach (var hardpointSet in independentSets)
        {
            var newPrecludingSets = new List<byte>();

            foreach (var setIndex in hardpointSet.precludingHardpointSets)
            {
                var originalName = independentSets[setIndex].name;
                if (originalName.StartsWith("Left ") || originalName.StartsWith("Right "))
                {
                    originalName = originalName.Substring(originalName.IndexOf(' ') + 1);
                }

                if (nameToIndexMap.TryGetValue(originalName, out var indices))
                {
                    newPrecludingSets.AddRange(indices.Select(x => (byte)x));
                }
            }

            hardpointSet.precludingHardpointSets = newPrecludingSets;
        }
    }

    public static List<Loadout> UpdateLoadouts(List<HardpointSet> independentSets, Dictionary<string, List<int>> nameToIndexMap, List<Loadout> loadouts, HardpointSet[] originalHardpointSets)
    {
        Logger.LogInfo($"Updating {loadouts.Count} loadouts with {independentSets.Count} independent hardpoint sets.");
        List<Loadout> newLoadouts = [];

        foreach (var loadout in loadouts)
        {
            Logger.LogInfo($"Processing loadout with {loadout.weapons.Count} weapons.");
            Loadout newLoadout = new()
            {
                weapons = [.. new WeaponMount[independentSets.Count]]
            };
            newLoadouts.Add(newLoadout);

            for (byte i = 0; i < loadout.weapons.Count && i < originalHardpointSets.Length; i++)
            {
                var originalName = originalHardpointSets[i].name;

                if (nameToIndexMap.TryGetValue(originalName, out var indices))
                {
                    Logger.LogInfo($"Processing weapon {loadout.weapons[i]?.name} index {i} to indexes {String.Join(" ",indices)}.");
                    foreach (var newIndex in indices)
                    {
                        newLoadout.weapons[newIndex] = loadout.weapons[i];
                    }
                }
            }
        }

        return newLoadouts;
    }

    public static string GetHardpointSide(HardpointSet hardpointSet)
    {
        return hardpointSet.hardpoints.First().transform.localPosition.x < 0.0 ? "Left" : "Right";
    }

    public static HardpointSet CopyHardpointSet(HardpointSet original)
    {
        if (original == null)
            return null;

        return new HardpointSet
        {
            name = original.name,
            precludingHardpointSets = [.. original.precludingHardpointSets],
            weaponOptions = [.. original.weaponOptions],
            weaponMount = original.weaponMount,
            hardpoints = [.. original.hardpoints]
        };
    }
    private static string GetFullPath(Transform transform)
    {
        string text = transform.name;
        while (transform.parent != null)
        {
            transform = transform.parent;
            text = transform.name + "/" + text;
        }

        return text;
    }
    public static GameObject FindGameObjectByExactPath(string path)
    {
        GameObject gameObject = null;
        try
        {
            gameObject = Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault((GameObject go) => GetFullPath(go.transform) == path);
        }
        catch (Exception arg)
        {
            Logger.LogWarning($"FindGOByEP error with {path}: {arg}");
        }

        if (gameObject == null)
        {
            Logger.LogDebug("FindGOByEP seach for " + path + " returned null.");
        }

        return gameObject;
    }
}

[HarmonyPatch(typeof(SavedLoadout), "CreateLoadout", new Type[] { typeof(WeaponManager) })]
public class SavedLoadoutPatch
{
    static void Postfix(SavedLoadout __instance, WeaponManager weaponManager, ref Loadout __result)
    {
        // Check if we have mapping data for this weapon manager
        if (!IndependentOptionPlugin.weaponManagerToIndexMap.TryGetValue(weaponManager, out var nameToIndexMap) ||
            !IndependentOptionPlugin.weaponManagerToOriginalSets.TryGetValue(weaponManager, out var originalSets))
        {
            return; // No mapping available, use original behavior
        }

        IndependentOptionPlugin.Logger.LogInfo($"Patching loaded loadout with {__instance.Selected.Count} selected mounts.");

        // Create new expanded loadout
        Loadout newLoadout = new Loadout
        {
            weapons = new List<WeaponMount>(new WeaponMount[weaponManager.hardpointSets.Length])
        };

        // Map from original loadout indices to new independent indices
        for (int i = 0; i < __instance.Selected.Count && i < originalSets.Length; i++)
        {
            var originalName = originalSets[i].name;
            var weaponMount = __instance.Selected[i].GetWeaponMount(originalSets[i]);

            if (nameToIndexMap.TryGetValue(originalName, out var indices))
            {
                IndependentOptionPlugin.Logger.LogInfo($"Mapping loaded weapon {weaponMount?.name} from index {i} to indices {String.Join(", ", indices)}.");
                foreach (var newIndex in indices)
                {
                    if (newIndex < newLoadout.weapons.Count)
                    {
                        newLoadout.weapons[newIndex] = weaponMount;
                    }
                }
            }
        }

        __result = newLoadout;
    }
}



[HarmonyPatch(typeof(Application), "version", MethodType.Getter)]
public class VersionGetterPatch
{
    private static void Postfix(ref string __result)
    {
        __result += "_independentOption1.0";
        IndependentOptionPlugin.Logger.LogWarning("Updated game version to " + __result);
    }
}

[HarmonyPatch(typeof(Encyclopedia), "AfterLoad")]
public class EncyclopediaPatch
{
    static void Postfix(Encyclopedia __instance)
    {
        IndependentOptionPlugin.SplitHardpoints();
        DelayedModifyUI();
        IndependentOptionPlugin.Logger.LogWarning("Hardpoints Split (Encyclopedia)");
    }

    static async void DelayedModifyUI()
    {
        await Task.Delay(3000);
        IndependentOptionPlugin.ModifyUI();
    }
}