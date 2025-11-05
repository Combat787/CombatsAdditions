using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Threading.Tasks;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using MonoMod.Utils;
using NuclearOption.SavedMission;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;

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
        GameObject hardpointSetDropdown = null;
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
                    hardpointSetDropdown = (GameObject)method.Invoke(null, ["HardpointSetSelector", true]);
                }
            }
        }

        if (hardpointSetDropdown == null)
        {
            hardpointSetDropdown = FindGameObjectByExactPath("HardpointSetSelector");
        }

        if (hardpointSetDropdown == null)
        {
            Logger.LogWarning("Could not find HardpointSetDropdown Template");
            return;
        }

        var hardpointSetDropdownTemplate = hardpointSetDropdown.transform.Find("HardpointSetDropdown/Template").gameObject;

        hardpointSetDropdown.AddComponent<WeaponSelectorEvents>();


        var transform = hardpointSetDropdownTemplate.GetComponent<RectTransform>();
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

                    if (AreSidesCompatible(side, targetSet.Item2))
                    {
                        newPrecludingSets.Add((byte)newIndex);
                        Logger.LogInfo($"Mapping preclusion from original set {precludedName} to independent set {targetSet.Item1.name}");
                    }
                }
            }

            hardpointSet.precludingHardpointSets = newPrecludingSets;
        }
    }

    private static bool AreSidesCompatible(Side side1, Side side2)
    {
        if (side1 == null || side2 == null)
            return true;

        string name1 = side1.name;
        string name2 = side2.name;

        if (string.IsNullOrEmpty(name1) || string.IsNullOrEmpty(name2))
            return true;

        string[] parts1 = name1.Split(' ');
        string[] parts2 = name2.Split(' ');

        return parts1.Intersect(parts2).Any() || !DoPartsConflict(parts1, parts2);
    }

    private static bool DoPartsConflict(string[] parts1, string[] parts2)
    {
        string[] leftRight = { "Left", "Right", "Center" };
        string[] frontBack = { "Front", "Back", "Middle" };
        string[] topBottom = { "Top", "Bottom" };

        if (HasConflictInDimension(parts1, parts2, leftRight)) return true;
        if (HasConflictInDimension(parts1, parts2, frontBack)) return true;
        if (HasConflictInDimension(parts1, parts2, topBottom)) return true;

        return false;
    }

    private static bool HasConflictInDimension(string[] parts1, string[] parts2, string[] dimension)
    {
        var dim1 = parts1.FirstOrDefault(p => dimension.Contains(p));
        var dim2 = parts2.FirstOrDefault(p => dimension.Contains(p));

        return dim1 != null && dim2 != null && dim1 != dim2;
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

        public override bool Equals(object obj)
        {
            if (obj is Side other)
            {
                return CompareTo(other) == 0;
            }
            return false;
        }

        public override int GetHashCode()
        {
            return HashCode.Combine(
                Math.Round(position.x / epsilon),
                Math.Round(position.y / epsilon),
                Math.Round(position.z / epsilon)
            );
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


    public static WeaponMount GetMount(List<WeaponMount> legalWeaponsCache, string displayName)
    {
        if (displayName == "Empty")
        {
            return null;
        }
        WeaponMount weaponMount = legalWeaponsCache.Find(obj => obj.mountName == displayName);

        if (weaponMount == null)
        {
            Debug.LogError("Couldn't find mount for displayName " + displayName);
        }
        return weaponMount;
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

public class WeaponSelectorEvents : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private bool hovering = false;
    private GameObject line;
    private RectTransform lineRect;
    private Image lineImage;
    private GameObject label;
    private Image labelImage;
    private Transform hardpoint;
    private Canvas canvas;
    private RectTransform canvasRect;
    private TMP_Dropdown dropdown;
    private WeaponSelector weaponSelector;
    private AircraftSelectionMenu selectionMenu;
    private RectTransform labelRect;
    private RectTransform selectorRect;
    private float targetAlpha = 0f;
    private float currentAlpha = 0f;
    private Color lineColorBase = new Color(0.92f, 0.92f, 0.97f, 1f);
    private Color labelColorBase = new Color(1f, 1f, 1f, 0.75f);

    void Start()
    {
        canvas = GetComponentInParent<Canvas>();
        canvasRect = canvas.GetComponent<RectTransform>();
        selectorRect = GetComponent<RectTransform>();

        line = new GameObject("UILine");
        line.transform.SetParent(canvas.transform, false);
        lineRect = line.AddComponent<RectTransform>();
        lineRect.pivot = new Vector2(0.5f, 0.5f);
        lineImage = line.AddComponent<Image>();

        int w = 64;
        Texture2D t = new Texture2D(w, 1);
        for (int x = 0; x < w; x++)
        {
            float d = Mathf.Clamp01((float)x / (w - 1));
            float a = Mathf.SmoothStep(0f, 1f, d) * Mathf.SmoothStep(0f, 1f, 1f - d);
            t.SetPixel(x, 0, new Color(lineColorBase.r, lineColorBase.g, lineColorBase.b, a));
        }
        t.filterMode = FilterMode.Bilinear;
        t.Apply();
        lineImage.sprite = Sprite.Create(t, new Rect(0, 0, w, 1), new Vector2(0.5f, 0.5f));

        label = new GameObject("HardpointLabel");
        label.transform.SetParent(canvas.transform, false);
        labelRect = label.AddComponent<RectTransform>();
        labelRect.sizeDelta = new Vector2(20f, 20f);
        labelImage = label.AddComponent<Image>();

        int size = 32;
        Texture2D dot = new Texture2D(size, size);
        Vector2 center = new Vector2(size / 2f, size / 2f);
        for (int y = 0; y < size; y++)
        {
            for (int x = 0; x < size; x++)
            {
                float dist = Vector2.Distance(new Vector2(x, y), center) / (size / 2f);
                float a = Mathf.Clamp01(1f - Mathf.Pow(dist, 1.5f)) * 0.65f;
                float v = Mathf.Lerp(0.80f, 1.0f, 1f - dist);
                dot.SetPixel(x, y, new Color(v, v, v, a));
            }
        }
        dot.filterMode = FilterMode.Bilinear;
        dot.Apply();
        labelImage.sprite = Sprite.Create(dot, new Rect(0, 0, size, size), new Vector2(0.5f, 0.5f));

        weaponSelector = GetComponent<WeaponSelector>();
        hardpoint = ReflectionHelper.GetField<HardpointSet>(weaponSelector, "hardpointSet").hardpoints.First().transform;
        selectionMenu = ReflectionHelper.GetField<AircraftSelectionMenu>(weaponSelector, "selectionMenu");
        dropdown = weaponSelector.weaponOptions;

        if (dropdown.template != null)
        {
            Toggle itemToggle = dropdown.template.GetComponentInChildren<Toggle>();
            if (itemToggle != null && itemToggle.GetComponent<WeaponDropdownEvents>() == null)
            {
                itemToggle.gameObject.AddComponent<WeaponDropdownEvents>();
                itemToggle.gameObject.GetComponent<WeaponDropdownEvents>().weaponSelector = weaponSelector;
                itemToggle.gameObject.GetComponent<WeaponDropdownEvents>().selectionMenu = selectionMenu;
            }
        }

        line.SetActive(false);
        label.SetActive(false);
    }

    void Update()
    {
        if (hardpoint != null && (hovering || dropdown.IsExpanded))
        {
            targetAlpha = 1f;

            if (!line.activeSelf)
            {
                line.SetActive(true);
                label.SetActive(true);
            }

            Vector2 hardpointScreenPos = Camera.main.WorldToScreenPoint(hardpoint.position);
            Vector2 selectorScreenPos = RectTransformUtility.WorldToScreenPoint(canvas.worldCamera, selectorRect.position);

            Vector2 hardpointCanvasPos;
            Vector2 selectorCanvasPos;

            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, hardpointScreenPos, canvas.worldCamera, out hardpointCanvasPos);
            RectTransformUtility.ScreenPointToLocalPointInRectangle(canvasRect, selectorScreenPos, canvas.worldCamera, out selectorCanvasPos);

            labelRect.anchoredPosition = hardpointCanvasPos;

            Vector2 diff = hardpointCanvasPos - selectorCanvasPos;
            float length = diff.magnitude;
            float angle = Mathf.Atan2(diff.y, diff.x) * Mathf.Rad2Deg;

            lineRect.anchoredPosition = (hardpointCanvasPos + selectorCanvasPos) * 0.5f;
            lineRect.sizeDelta = new Vector2(length, 4f);
            lineRect.rotation = Quaternion.Euler(0f, 0f, angle);
        }
        else
        {
            targetAlpha = 0f;
        }

        currentAlpha = Mathf.Lerp(currentAlpha, targetAlpha, Time.deltaTime * 12f);

        lineImage.color = new Color(lineColorBase.r, lineColorBase.g, lineColorBase.b, currentAlpha);
        labelImage.color = new Color(labelColorBase.r, labelColorBase.g, labelColorBase.b, currentAlpha);

        if (currentAlpha < 0.01f && line.activeSelf)
        {
            line.SetActive(false);
            label.SetActive(false);
        }
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        hovering = true;
        var mount = weaponSelector != null ? weaponSelector.GetMount() : null;
        if (selectionMenu != null) selectionMenu.DisplayInfo(mount != null ? mount.info : null);
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        hovering = false;
        if (selectionMenu != null) selectionMenu.DisplayInfo(null);
    }

    void OnDestroy()
    {
        if (line != null) Destroy(line);
        if (label != null) Destroy(label);
    }
}

public class WeaponDropdownEvents : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
{
    private TMP_Dropdown dropdown;
    private TMP_Text itemLabel;
    public WeaponSelector weaponSelector;
    public AircraftSelectionMenu selectionMenu;

    void Start()
    {
        dropdown = GetComponentInParent<TMP_Dropdown>();
        itemLabel = transform.Find("Item Label")?.GetComponent<TMP_Text>();
    }

    public void OnPointerEnter(PointerEventData eventData)
    {
        if (dropdown != null)
        {
            int itemIndex = transform.GetSiblingIndex();
            if (itemIndex < dropdown.options.Count)
            {
                var option = dropdown.options[itemIndex];
                selectionMenu.DisplayInfo(IndependentOptionPlugin.GetMount(ReflectionHelper.GetField<List<WeaponMount>>(weaponSelector, "legalWeaponsCache"),option.text).info);
            }
        }
    }

    public void OnPointerExit(PointerEventData eventData)
    {
        selectionMenu.DisplayInfo(null);
    }


}

