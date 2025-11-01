using System.Collections.Generic;
using BepInEx;
using BepInEx.Configuration;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;

[BepInPlugin(PluginGUID, PluginName, PluginVersion)]
public class GravityPlugin : BaseUnityPlugin
{
    private const string PluginGUID = "com.combat.GravityOption";
    private const string PluginName = "Gravity Modifier";
    private const string PluginVersion = "1.0.0";

    private ConfigEntry<float> gravityX;
    private ConfigEntry<float> gravityY;
    private ConfigEntry<float> gravityZ;
    private ConfigEntry<bool> enableMod;

    private Vector3 lastAppliedGravity;
    internal static new ManualLogSource Logger;
    private void Awake()
    {
        Logger = base.Logger;
        enableMod = Config.Bind("General",
            "Enable Mod",
            true,
            "Enable or disable the gravity modifier");

        gravityX = Config.Bind("Gravity Settings",
            "Gravity X",
            0f,
            new ConfigDescription(
                "The X component of gravity in g",
                new AcceptableValueRange<float>(-50f, 50f)));

        gravityY = Config.Bind("Gravity Settings",
            "Gravity Y",
            1f,
            new ConfigDescription(
                "The Y component of gravity in g (default is 1, negative pulls down)",
                new AcceptableValueRange<float>(-50f, 50f)));

        gravityZ = Config.Bind("Gravity Settings",
            "Gravity Z",
            0f,
            new ConfigDescription(
                "The Z component of gravity in g",
                new AcceptableValueRange<float>(-50f, 50f)));

        gravityX.SettingChanged += OnGravityChanged;
        gravityY.SettingChanged += OnGravityChanged;
        gravityZ.SettingChanged += OnGravityChanged;
        enableMod.SettingChanged += OnGravityChanged;

        ApplyGravity();

        Logger.LogInfo($"{PluginName} v{PluginVersion} loaded!");
        Logger.LogInfo($"Initial gravity set to: {Physics.gravity}");
        Logger.LogInfo("Open Configuration Manager (F1) to adjust gravity in real-time!");

        var harmony = new Harmony("com.combat.GravityOption");
        harmony.PatchAll();
    }

    private void OnGravityChanged(object sender, System.EventArgs e)
    {
        ApplyGravity();
        Logger.LogInfo($"Gravity changed to: {Physics.gravity}");
    }

    private void Update()
    {
        if (enableMod.Value)
        {
            Vector3 targetGravity = new(gravityX.Value * -9.81f, gravityY.Value * -9.81f, gravityZ.Value * -9.81f);
            if (Physics.gravity != targetGravity)
            {
                Physics.gravity = targetGravity;
            }
        }
    }

    private void ApplyGravity()
    {
        if (enableMod.Value)
        {
            Vector3 newGravity = new(gravityX.Value * -9.81f, gravityY.Value * -9.81f, gravityZ.Value * -9.81f);
            Physics.gravity = newGravity;
            lastAppliedGravity = newGravity;
        }
        else
        {
            Physics.gravity = new Vector3(0f, -9.81f, 0f);
        }
    }

    private void OnDestroy()
    {
        Physics.gravity = new Vector3(0f, -9.81f, 0f);
        Logger.LogInfo("Gravity reset to default on mod unload");
    }
}

[HarmonyPatch(typeof(Application), "version", MethodType.Getter)]
public class VersionGetterPatch
{
    private static void Postfix(ref string __result)
    {
        __result += "_GravityOption1.0";
        GravityPlugin.Logger.LogWarning("Updated game version to " + __result);
    }
}
