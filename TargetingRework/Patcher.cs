using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using HarmonyLib;
using UnityEngine;

[HarmonyPatch(typeof(Application), nameof(Application.version), MethodType.Getter)]
public class VersionGetterPatch
{
    static void Postfix(ref string __result)
    {
        __result += $"_{MyPluginInfo.PLUGIN_GUID}-v{MyPluginInfo.PLUGIN_VERSION}";
        WeaponsLoader.Logger.LogWarning($"Updated game version to {__result}");
    }
}
[HarmonyPatch(typeof(Encyclopedia), "AfterLoad")]
public class EncyclopediaWeaponMountPatch
{
    static bool Prefix(Encyclopedia __instance)
    {
        WeaponsLoader.Load();
        __instance.weaponMounts.AddRange(WeaponsLoader.Weapons);
        WeaponsLoader.Logger.LogInfo("Injected Weapons");
        return true;
    }
}


[HarmonyPatch(typeof(SARHSeeker), "Initialize")]
public class SARHSeekerPatcher
{
    static void Postfix(SARHSeeker __instance)
    {
        ((Missile.Warhead)typeof(Missile).GetField("warhead", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(((Missile)typeof(SARHSeeker).GetField("missile", BindingFlags.Instance | BindingFlags.NonPublic).GetValue(__instance)))).Armed = false;

    }
}



[HarmonyPatch(typeof(SARHSeeker), "Initialize")]
public class SARHSeekerSafeTranspiler
{
    static IEnumerable<CodeInstruction> Transpiler(IEnumerable<CodeInstruction> instructions)
    {
        var codes = new List<CodeInstruction>(instructions);

        var getAttachedUnitMethod = AccessTools.Method(typeof(TargetDetector), "GetAttachedUnit");
        var getRandomPartMethod = AccessTools.Method(typeof(Unit), "GetRandomPart");
        WeaponsLoader.Logger.LogInfo($"Looking for GetAttachedUnit method. Found: {getAttachedUnitMethod != null}");
        WeaponsLoader.Logger.LogInfo($"Looking for GetRandomPart method. Found: {getRandomPartMethod != null}");
        for (int i = 0; i < codes.Count; i++)
        {
            if (codes[i].Calls(getAttachedUnitMethod))
            {
                codes[i] = CodeInstruction.Call(typeof(SARHSeekerSafeTranspiler), "SafeGetAttachedUnit");
                WeaponsLoader.Logger.LogInfo("PATCHED GetAttachedUnitMethod call");
            }
            if (codes[i].Calls(getRandomPartMethod))
            {
                codes[i] = CodeInstruction.Call(typeof(SARHSeekerSafeTranspiler), "SafeGetRandomPart");
                WeaponsLoader.Logger.LogInfo("PATCHED GetRandomPart call");
            }
        }
        return codes;
    }

    public static Unit SafeGetAttachedUnit(TargetDetector radar)
    {
        if (radar == null)
        {
            WeaponsLoader.Logger.LogWarning("SafeGetAttachedUnit called on null target, returning null");
            return null;
        }
        return radar.GetAttachedUnit();
    }

    public static Transform SafeGetRandomPart(Unit target)
    {
        if (target == null)
        {
            WeaponsLoader.Logger.LogWarning("GetRandomPart called on null target, returning null");
            return null;
        }
        return target.GetRandomPart();
    }
}

[HarmonyPatch(typeof(Hardpoint), "SpawnMount")]
public class HardpointPatch
{
    static void Postfix(Hardpoint __instance, GameObject ___spawnedPrefab)
    {
        if (!___spawnedPrefab.activeSelf)
        {
            ___spawnedPrefab.SetActive(true);
        }
    }
}
