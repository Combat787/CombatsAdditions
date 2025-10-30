
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CombatsAdditions;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class IndependentOptionPlugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;

    private void Awake()
    {
        Logger = base.Logger;
        var harmony = new Harmony("IndependentOption");

        var useQOLPatch = AppDomain.CurrentDomain.GetAssemblies()
            .Any(assembly => assembly.GetTypes().Any(type => type.Name == "QOLPlugin"));

        if (useQOLPatch)
        {
            Logger.LogInfo("QOLPlugin detected, using QOLPlugin patch");
            harmony.CreateClassProcessor(typeof(ProcessConfigLinesPatch)).Patch();
        }
        else
        {
            Logger.LogInfo("QOLPlugin not detected, using Encyclopedia patch");
            harmony.CreateClassProcessor(typeof(EncyclopediaPatch)).Patch();
        }
        harmony.CreateClassProcessor(typeof(VersionGetterPatch)).Patch();
        Logger.LogInfo($"Independent Option {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }


    public static void ModifyUI()
    {
        Type type = typeof(QOLPlugin);
        MethodInfo method = type.GetMethod("FindGameObjectByExactPath",
            BindingFlags.NonPublic | BindingFlags.Static);
        GameObject result = (GameObject)method.Invoke(null, ["HardpointSetSelector/HardpointSetDropdown/Template", true]);
        var transform = result.GetComponent<RectTransform>();
        transform.offsetMin += new Vector2(-60f, 0f);
        transform.offsetMax += new Vector2(60f, 0f);
    }

    public static void SplitHardpoints()
    {
        foreach (var aircraft in Resources.FindObjectsOfTypeAll<Aircraft>())
        {
            var weaponManager = aircraft.weaponManager;
            var groupedHardpointSets = GroupHardpointsByName(weaponManager);
            var independentHardpointSets = CreateIndependentHardpointSets(groupedHardpointSets);
            UpdatePrecludingSets(independentHardpointSets, weaponManager);
            weaponManager.hardpointSets = [.. independentHardpointSets];
        }
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

    public static void UpdatePrecludingSets(List<HardpointSet> independentSets, WeaponManager weaponManager)
    {
        foreach (var hardpointSet in independentSets)
        {
            var newPrecludingSets = new List<byte>();

            foreach (var set in hardpointSet.precludingHardpointSets)
            {
                var originalName = weaponManager.hardpointSets[set].name;
                var precludingIndex = independentSets.FindIndex(x => x.name == originalName);

                if (precludingIndex == -1)
                {
                    var side = GetHardpointSide(hardpointSet);
                    precludingIndex = independentSets.FindIndex(x => x.name == $"{side} {originalName}");
                }

                if (precludingIndex != -1)
                {
                    newPrecludingSets.Add((byte)precludingIndex);
                }
            }

            hardpointSet.precludingHardpointSets = newPrecludingSets;
        }
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
    private static bool Prefix(Encyclopedia __instance)
    {
        IndependentOptionPlugin.SplitHardpoints();

        IndependentOptionPlugin.ModifyUI();

        IndependentOptionPlugin.Logger.LogWarning("Hardpoints Split");
        return true;
    }
}

[HarmonyPatch(typeof(QOLPlugin), "ProcessConfigLinesSingleThread")]
public class ProcessConfigLinesPatch
{
    static IEnumerator Postfix(IEnumerator original)
    {
        while (original.MoveNext())
        {
            yield return original.Current;
        }
        IndependentOptionPlugin.SplitHardpoints();

        IndependentOptionPlugin.ModifyUI();

        IndependentOptionPlugin.Logger.LogWarning("Hardpoints Split");
    }

}