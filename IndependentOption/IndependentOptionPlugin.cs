using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using NuclearOption.SavedMission;
using UnityEngine;
using UnityEngine.Assertions.Must;
using UnityEngine.UIElements;

namespace CombatsAdditions;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class IndependentOptionPlugin : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    public static bool useQOLPatch = false;
    public static Dictionary<WeaponManager, Dictionary<string, List<int>>> weaponManagerToIndexMap = [];
    public static Dictionary<WeaponManager, HardpointSet[]> weaponManagerToOriginalSets = [];

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
        harmony.CreateClassProcessor(typeof(SavedLoadoutPatch)).Patch();
        Logger.LogInfo($"Independent Option {MyPluginInfo.PLUGIN_GUID} is loaded!");
    }
    private void PatchQOLPlugin(Harmony harmony)
    {
        try
        {
            Type qolPluginType = AppDomain.CurrentDomain.GetAssemblies()
                .SelectMany(a => a.GetTypes())
                .FirstOrDefault(t => t.Name == "QOLPlugin");

            if (qolPluginType == null)
            {
                Logger.LogError("QOLPlugin type not found despite detection");
                return;
            }
            MethodInfo originalMethod = qolPluginType.GetMethod("ProcessConfigLinesSingleThread",
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Static);

            if (originalMethod == null)
            {
                Logger.LogError("ProcessConfigLinesSingleThread method not found");
                return;
            }

            MethodInfo postfixMethod = typeof(IndependentOptionPlugin).GetMethod(nameof(QOLPluginPostfix),
                BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Static);

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
                    result = (GameObject)method.Invoke(null, ["HardpointSetSelector/HardpointSetDropdown/Template", true]);
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
            var originalHardpointSets = weaponManager.hardpointSets;
            weaponManagerToOriginalSets[weaponManager] = originalHardpointSets;
            var groupedHardpointSets = GroupHardpointsByName(weaponManager);
            var independentHardpointSets = CreateIndependentHardpointSets(groupedHardpointSets);
            independentHardpointSets.Sort((a, b) => a.Item2.CompareTo(b.Item2));
            var nameToIndexMap = BuildNameToIndexMap(weaponManager, independentHardpointSets);
            weaponManagerToIndexMap[weaponManager] = nameToIndexMap;

            aircraft.GetAircraftParameters().loadouts = UpdateLoadouts(independentHardpointSets, nameToIndexMap, aircraft.GetAircraftParameters().loadouts, originalHardpointSets);
            var newStandardLoadouts = UpdateLoadouts(independentHardpointSets, nameToIndexMap, [.. aircraft.GetAircraftParameters().StandardLoadouts.Select(x => x.loadout)], originalHardpointSets);

            for (byte i = 0; i < newStandardLoadouts.Count; i++)
            {
                aircraft.GetAircraftParameters().StandardLoadouts[i].loadout = newStandardLoadouts[i];
            }

            UpdatePrecludingSets(independentHardpointSets, nameToIndexMap, weaponManager);
            weaponManager.hardpointSets = RenamedIndependentHardpointNames(independentHardpointSets);
        }
    }


    private static HardpointSet[] RenamedIndependentHardpointNames(List<(HardpointSet, Side)> independentHardpointSets)
    {
        var renamedSets = new List<HardpointSet>();
        foreach (var (hardpointSet, side) in independentHardpointSets)
        {
            if (side.name != null)
            {
                hardpointSet.name = side.name + " " + hardpointSet.name;
            }
        }
        return [.. independentHardpointSets.Select(x => { return x.Item1; })];
    }

    private static Dictionary<string, List<int>> BuildNameToIndexMap(WeaponManager weaponManager, List<(HardpointSet, Side)> independentSets)
    {
        var map = new Dictionary<string, List<int>>();

        for (int i = 0; i < independentSets.Count; i++)
        {
            var name = independentSets[i].Item1.name;

            if (!map.ContainsKey(name))
            {
                map[name] = [];
            }
            map[name].Add(i);
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

    public static List<(HardpointSet, Side)> CreateIndependentHardpointSets(Dictionary<string, List<HardpointSet>> grouped)
    {
        var independentSets = new List<(HardpointSet, Side)>();

        foreach (var group in grouped)
        {
            foreach (var hardpointSet in group.Value)
            {

                independentSets.Add((hardpointSet, GetHardpointSide(hardpointSet,group.Value)));
            }
        }

        return independentSets;
    }
    public static void UpdatePrecludingSets(List<(HardpointSet, Side)> independentSets, Dictionary<string, List<int>> nameToIndexMap, WeaponManager weaponManager)
    {
        foreach (var (hardpointSet, side) in independentSets)
        {
            var newPrecludingSets = new List<byte>();

            foreach (var setIndex in hardpointSet.precludingHardpointSets)
            {
                var precludedSet = weaponManagerToOriginalSets[weaponManager][setIndex];
                var precludedName = precludedSet.name;

                nameToIndexMap.TryGetValue(precludedName, out var indices);

                foreach (var newIndex in indices)
                {
                    var targetSet = independentSets[newIndex];

                    if (targetSet.Item2 == side || targetSet.Item2 == null || side == null)
                    {
                        newPrecludingSets.Add((byte)newIndex);
                    }
                }
            }

            hardpointSet.precludingHardpointSets = newPrecludingSets;
        }
    }


    public static List<Loadout> UpdateLoadouts(List<(HardpointSet, Side)> independentSets, Dictionary<string, List<int>> nameToIndexMap, List<Loadout> loadouts, HardpointSet[] originalHardpointSets)
    {
        List<Loadout> newLoadouts = [];

        foreach (var loadout in loadouts)
        {
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
                    foreach (var newIndex in indices)
                    {
                        newLoadout.weapons[newIndex] = loadout.weapons[i];
                    }
                }
            }
        }

        return newLoadouts;
    }
    public class Side : IComparable<Side>
    {
        const float epsilon = 0.01f;
        public Vector3 position;
        public List<Vector3> relativePoints;

        public string name
        {
            get
            {
                if (relativePoints == null || relativePoints.Count == 0)
                {
                    return null;
                }

                List<string> nameParts = [];

                var leftPoints = relativePoints.Count(p => p.x < position.x - epsilon);
                var rightPoints = relativePoints.Count(p => p.x > position.x + epsilon);

                if (leftPoints > 0 && rightPoints == 0)
                {
                    nameParts.Add("Right");
                }
                else if (rightPoints > 0 && leftPoints == 0)
                {
                    nameParts.Add("Left");
                }
                else if (leftPoints > 0 && rightPoints > 0)
                {
                    nameParts.Add("Center");
                }

                var backPoints = relativePoints.Count(p => p.z < position.z - epsilon);
                var frontPoints = relativePoints.Count(p => p.z > position.z + epsilon);

                if (backPoints > 0 && frontPoints == 0)
                {
                    nameParts.Add("Front");
                }
                else if (frontPoints > 0 && backPoints == 0)
                {
                    nameParts.Add("Back");
                }
                else if (backPoints > 0 && frontPoints > 0)
                {
                    nameParts.Add("Middle");
                }

                var belowPoints = relativePoints.Count(p => p.y < position.y - epsilon);
                var abovePoints = relativePoints.Count(p => p.y > position.y + epsilon);

                if (belowPoints > 0 && abovePoints == 0)
                {
                    nameParts.Add("Top");
                }
                else if (abovePoints > 0 && belowPoints == 0)
                {
                    nameParts.Add("Bottom");
                }

                return string.Join(" ", nameParts);
            }
        }

        public int CompareTo(Side other)
        {
            int result = CompareWithTolerance(position.x, other.position.x, epsilon);
            if (result != 0) return result;
            result = CompareWithTolerance(position.z, other.position.z, epsilon);
            if (result != 0) return result;
            return CompareWithTolerance(position.y, other.position.y, epsilon);
        }

        private static int CompareWithTolerance(float a, float b, float epsilon)
        {
            float diff = a - b;
            if (Math.Abs(diff) < epsilon) return 0;
            return diff < 0 ? -1 : 1;
        }
    }

    public static Side GetHardpointSide(HardpointSet hardpointSet, List<HardpointSet> value)
    {
        Side side = new()
        {
            position = hardpointSet.hardpoints.First().transform.position,
            relativePoints = [.. value.Select(x => { return x.hardpoints.First().transform.position; })]
        };
        return side;
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
        if (!IndependentOptionPlugin.weaponManagerToIndexMap.TryGetValue(weaponManager, out var nameToIndexMap) ||
            !IndependentOptionPlugin.weaponManagerToOriginalSets.TryGetValue(weaponManager, out var originalSets))
        {
            return;
        }

        IndependentOptionPlugin.Logger.LogInfo($"Patching loaded loadout with {__instance.Selected.Count} selected mounts.");

        Loadout newLoadout = new Loadout
        {
            weapons = new List<WeaponMount>(new WeaponMount[weaponManager.hardpointSets.Length])
        };

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