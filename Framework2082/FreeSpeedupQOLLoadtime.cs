using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
namespace CombatsAdditions;

[BepInDependency("com.offiry.qol")]
[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
public class FreeSpeedupQOLLoadtime : BaseUnityPlugin
{
    internal static new ManualLogSource Logger;
    internal static Dictionary<string, List<GameObject>> rootObjectCache = null;
    private void Awake()
    {
        Logger = base.Logger;
        var harmony = new Harmony("FreeSpeedupQOLLoadtime");
        harmony.PatchAll();
        Logger.LogInfo($"FreeSpeedupQOLLoadtime is Loaded");
    }
}
[HarmonyPatch(typeof(QOLPlugin), "FindGameObjectByExactPath")]
public class QOLPatcher
{
    [HarmonyPrefix]
    static bool Prefix(ref GameObject __result, ref readonly Dictionary<string, GameObject> ____pathCache, string path, bool checkCache)
    {
        __result = FindByPath(____pathCache, path, checkCache, false);
        return false;
    }
    private static GameObject FindByPath(Dictionary<string, GameObject> ____pathCache, string path, bool checkCache, bool retrywithrecache)
    {
        if (checkCache && ____pathCache.TryGetValue(path, out var cachedObj))
        {
            if (cachedObj != null)
            {
                return cachedObj;
            }
            ____pathCache.Remove(path);
        }

        if (FreeSpeedupQOLLoadtime.rootObjectCache == null || retrywithrecache)
        {
            FreeSpeedupQOLLoadtime.Logger.LogInfo(path);
            Recache();
        }

        string[] parts = path.Split('/');
        if (parts.Length == 0)
        {
            FreeSpeedupQOLLoadtime.Logger.LogDebug($"FindGOByEP search for {path} returned null (empty path).");
            return null;
        }

        if (!FreeSpeedupQOLLoadtime.rootObjectCache.TryGetValue(parts[0], out var rootCandidates))
        {
            Recache();
            if (!FreeSpeedupQOLLoadtime.rootObjectCache.TryGetValue(parts[0], out rootCandidates))
            {
                FreeSpeedupQOLLoadtime.Logger.LogDebug($"FindGOByEP search for {path} returned null (no root match).");
                return null;
            }
        }

        foreach (var rootCandidate in rootCandidates)
        {
            GameObject current = rootCandidate;
            bool pathMatched = true;

            for (int i = 1; i < parts.Length; i++)
            {
                var foundTransform = current.transform.Find(parts[i]);
                if (foundTransform == null)
                {
                    pathMatched = false;
                    break;
                }
                current = foundTransform.gameObject;
            }

            if (pathMatched)
            {
                ____pathCache[path] = current;
                return current;
            }
        }

        FreeSpeedupQOLLoadtime.Logger.LogDebug($"FindGOByEP search for {path} returned null.");
        return null;
    }

    public static int misses = 0;
    public static long totalmillis = 0;
    public static void Recache()
    {
        var watch = Stopwatch.StartNew();
        FreeSpeedupQOLLoadtime.rootObjectCache = Resources.FindObjectsOfTypeAll<Transform>()
            .Where(x => x.parent == null)
            .Select(x => x.gameObject)
            .GroupBy(go => go.name)
            .ToDictionary(g => g.Key, g => g.ToList());
        misses++;
        watch.Stop();
        totalmillis += watch.ElapsedMilliseconds;
    }
}



[HarmonyPatch(typeof(QOLPlugin), "DuplicatePrefab")]
public class DuplicatePrefabPatcher
{
    [HarmonyPostfix]
    public static void Postfix(GameObject __result, string originalName, string newName)
    {
        if (__result == null) return;
        if (!FreeSpeedupQOLLoadtime.rootObjectCache.TryGetValue(__result.name, out var gameObjects))
        {
            FreeSpeedupQOLLoadtime.rootObjectCache.Add(__result.name, [__result]);
        }
        else
        {
            gameObjects.Add(__result);
        }
    }
}