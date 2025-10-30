using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

namespace CombatsAdditions;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class IndependentOptionPlugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    static bool useQOLPatch = false;

    private void Awake()
    {
        Logger = base.Logger;
        var harmony = new Harmony("IndependentOption");

        useQOLPatch = AppDomain.CurrentDomain.GetAssemblies()
            .Any(assembly => assembly.GetTypes().Any(type => type.Name == "QOLPlugin"));

        if (useQOLPatch)
        {
            Logger.LogInfo("QOLPlugin detected, using QOLPlugin patch");
            // Dynamically patch QOLPlugin using reflection to avoid type loading issues
            PatchQOLPlugin(harmony);
        }
        else
        {
            Logger.LogInfo("QOLPlugin not detected, using Encyclopedia patch");
            harmony.CreateClassProcessor(typeof(EncyclopediaPatch)).Patch();
        }
        harmony.CreateClassProcessor(typeof(VersionGetterPatch)).Patch();
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
            // Use reflection to call QOLPlugin's FindGameObjectByExactPath
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