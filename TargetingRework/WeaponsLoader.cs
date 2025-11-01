using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using BepInEx;
using BepInEx.Logging;
using HarmonyLib;
using UnityEngine;
using UnityEngine.Audio;
using UnityEngine.Rendering;
using WeaponsFramework;

[BepInPlugin(MyPluginInfo.PLUGIN_GUID, MyPluginInfo.PLUGIN_NAME, MyPluginInfo.PLUGIN_VERSION)]
[BepInProcess("NuclearOption.exe")]
public class WeaponsLoader : BaseUnityPlugin
{
    public static new ManualLogSource Logger;

    public static List<WeaponMount> Weapons = [];
    public static List<Missile> Missiles = [];
    void Awake()
    {
        Logger = base.Logger;
        Logger.LogInfo("WeaponsLoader loaded!");
        var harmony = new Harmony("com.combat.WeaponsLoader");
        harmony.PatchAll();
        Logger.LogInfo("Harmony patches applied!");
    }

    public static string GetPluginFilePath(string fileName)
    {
        return Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location), fileName);
    }
    public static void Load()
    {
        R9Test();
        //KEMTest();
    }

    private static void R9Test()
    {

        Logger.LogInfo("Loading new missile mount.");

        Transform top = null;
        Transform middle = null;
        Transform bottom = null;
        GameObject baseObj = new GameObject("SAM_Radar2_single");
        baseObj.transform.SetParent(null);
        baseObj.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.HideAndDontSave;
        baseObj.SetActive(false);
        GameObjectCreator.Transform(baseObj, (transform) =>
        {
            top = transform;
        });

        MeshRenderer meshRenderer = null;
        var pylon = new GameObject("pylon");
        GameObjectCreator.Mesh(pylon, (renderer, filter) =>
        {
            renderer.material = GameObjectCreator.GetMaterialByName("Missiles1");
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.staticShadowCaster = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            renderer.allowOcclusionWhenDynamic = true;
            filter.mesh = GameObjectCreator.GetMeshByName("launchpylon1");
        });

        GameObjectCreator.Transform(pylon, (transform) =>
        {
            middle = transform;
        });

        var missile = new GameObject("SAM_Radar2Mounted");
        GameObjectCreator.Mesh(missile, (renderer, filter) =>
        {
            renderer.material = GameObjectCreator.GetMaterialByName("Weapons4");
            meshRenderer = renderer;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.staticShadowCaster = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            renderer.allowOcclusionWhenDynamic = true;

            filter.mesh = GameObjectCreator.GetMeshByName("SAM_Radar2");
        });
        GameObjectCreator.AudioSource(missile, (audioSource) =>
        {
            audioSource.clip = Resources.FindObjectsOfTypeAll<AudioClip>()
                .FirstOrDefault(clip => clip.name == "rocketlaunch");

            audioSource.outputAudioMixerGroup = Resources.FindObjectsOfTypeAll<AudioMixerGroup>()
                .FirstOrDefault(group => group.name == "Effects_General");

            audioSource.volume = 0.303f;
            audioSource.pitch = 1f;
            audioSource.loop = false;
            audioSource.mute = false;
            audioSource.bypassEffects = false;
            audioSource.bypassListenerEffects = false;
            audioSource.bypassReverbZones = false;
            audioSource.playOnAwake = false;
            audioSource.priority = 128;

            audioSource.panStereo = 0f;
            audioSource.spatialBlend = 1f;
            audioSource.reverbZoneMix = 1f;

            audioSource.dopplerLevel = 1f;
            audioSource.spread = 0f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 50f;
            audioSource.maxDistance = 500f;
        });

        GameObjectCreator.Transform(missile, (transform) =>
        {
            bottom = transform;
        });

        GameObjectCreator.Collider<CapsuleCollider>(missile, (collider) =>
        {
            collider.radius = 0.2010874f;
            collider.height = 5.116514f;
            collider.direction = 2;
            collider.center = new Vector3(0f, 0f, 0.061958313f);
        });
        GameObjectCreator.LODGroup(missile, (group) =>
        {
            group.fadeMode = LODFadeMode.None;
            group.animateCrossFading = false;
            LOD lod = new LOD
            {
                screenRelativeTransitionHeight = 0.0022398f,
                fadeTransitionWidth = 0.0f,
                renderers = new Renderer[] { meshRenderer }
            };
            group.SetLODs(new LOD[] { lod });
            group.size = 1.63676f;
            group.RecalculateBounds();
            group.enabled = true;
        });

        var missileObject = GameObjectCreator.FindGameObjectByExactPath("SAM_Radar2");
        var componentMissile = missileObject.GetComponent<Missile>();
        var type = componentMissile.GetType();
        var field = componentMissile.GetType().GetField("info", BindingFlags.NonPublic | BindingFlags.Instance);
        WeaponInfo weaponInfo = (WeaponInfo)field?.GetValue(componentMissile);
        MountedMissile mounted = null;

        GameObjectCreator.MountedMissile(missile, (MountedMissile) =>
        {
            mounted = MountedMissile;
            MountedMissileCreator.Modify(
                MountedMissile,
                weaponInfo,
                ammo: 1);
        });

        middle.localPosition = new Vector3(0, -0.15f, 0.15f);
        bottom.localPosition = new Vector3(0, -0.4f, 0.7f);

        var scale = new Vector3(1, 1.6f, 1f);
        middle.localScale = scale;



        bottom.SetParent(middle);
        middle.SetParent(top);
        WeaponMount weaponMount = Scriptable((WeaponMount mount) =>
        {

            mount.jsonKey = "SAM_Radar2_single";
            mount.info = weaponInfo;
            mount.mountName = "R9";
            mount.ammo = 1;
            mount.turret = false;
            mount.missileBay = false;
            mount.radar = false;
            mount.tailHook = false;
            mount.countermeasure = false;
            mount.colorable = false;
            mount.sortWeapons = true;
            mount.GearSafety = true;
            mount.GroundSafety = true;
            mount.GunAmmo = false;
            mount.emptyCost = 0.01f;
            mount.emptyMass = 30;
            mount.mass = 530;
            mount.drag = 1;
            mount.emptyDrag = 0;
            mount.RCS = 0.3f;
            mount.emptyRCS = 0;
            mount.disabled = false;
            mount.dontAutomaticallyAddToEncyclopedia = false;
        });



        WeaponMountToHardpoints(weaponMount, new Dictionary<string, HashSet<int>>
        {
            { "AttackHelo1", new HashSet<int>{2} },
            { "Fighter1", new HashSet<int>{2} },
            { "Multirole1", new HashSet<int>{4,5} }
        });

        weaponMount.prefab = baseObj;
        Weapons.Add(weaponMount);

        Logger.LogInfo($"Created Weapon Mount {weaponMount.name}");
    }
    private static void KEMTest()
    {
        Logger.LogInfo("Loading new missile mount.");

        Transform top = null;
        Transform middle = null;
        Transform bottom = null;
        GameObject baseObj = new GameObject("KEM_Single");
        baseObj.transform.SetParent(null);
        baseObj.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.HideAndDontSave;
        baseObj.SetActive(false);
        GameObjectCreator.Transform(baseObj, (transform) =>
        {
            top = transform;
        });

        MeshRenderer meshRenderer = null;
        var pylon = new GameObject("pylon");
        GameObjectCreator.Mesh(pylon, (renderer, filter) =>
        {
            renderer.material = GameObjectCreator.GetMaterialByName("Missiles1");
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.staticShadowCaster = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            renderer.allowOcclusionWhenDynamic = true;
            filter.mesh = GameObjectCreator.GetMeshByName("launchpylon1");
        });

        GameObjectCreator.Transform(pylon, (transform) =>
        {
            middle = transform;
        });

        var mountedMissile = new GameObject("KEMMounted");
        GameObjectCreator.Mesh(mountedMissile, (renderer, filter) =>
        {
            renderer.material = GameObjectCreator.GetMaterialByName("Missiles2");
            meshRenderer = renderer;
            renderer.shadowCastingMode = ShadowCastingMode.On;
            renderer.receiveShadows = true;
            renderer.staticShadowCaster = false;
            renderer.lightProbeUsage = LightProbeUsage.BlendProbes;
            renderer.reflectionProbeUsage = ReflectionProbeUsage.BlendProbes;
            renderer.motionVectorGenerationMode = MotionVectorGenerationMode.Object;
            renderer.allowOcclusionWhenDynamic = true;

            filter.mesh = RuntimeMeshImporter.ImportOBJ(GetPluginFilePath("KEM.obj"));
        });
        GameObjectCreator.AudioSource(mountedMissile, (audioSource) =>
        {
            audioSource.clip = Resources.FindObjectsOfTypeAll<AudioClip>()
                .FirstOrDefault(clip => clip.name == "rocketlaunch");

            audioSource.outputAudioMixerGroup = Resources.FindObjectsOfTypeAll<AudioMixerGroup>()
                .FirstOrDefault(group => group.name == "Effects_General");

            audioSource.volume = 0.303f;
            audioSource.pitch = 1f;
            audioSource.loop = false;
            audioSource.mute = false;
            audioSource.bypassEffects = false;
            audioSource.bypassListenerEffects = false;
            audioSource.bypassReverbZones = false;
            audioSource.playOnAwake = false;
            audioSource.priority = 128;

            audioSource.panStereo = 0f;
            audioSource.spatialBlend = 1f;
            audioSource.reverbZoneMix = 1f;

            audioSource.dopplerLevel = 1f;
            audioSource.spread = 0f;
            audioSource.rolloffMode = AudioRolloffMode.Linear;
            audioSource.minDistance = 50f;
            audioSource.maxDistance = 500f;
        });

        GameObjectCreator.Transform(mountedMissile, (transform) =>
        {
            bottom = transform;
        });

        GameObjectCreator.Collider<CapsuleCollider>(mountedMissile, (collider) =>
        {
            collider.radius = 0.2010874f;
            collider.height = 5.116514f;
            collider.direction = 2;
            collider.center = new Vector3(0f, 0f, 0.061958313f);
        });
        GameObjectCreator.LODGroup(mountedMissile, (group) =>
        {
            group.fadeMode = LODFadeMode.None;
            group.animateCrossFading = false;
            LOD lod = new LOD
            {
                screenRelativeTransitionHeight = 0.0022398f,
                fadeTransitionWidth = 0.0f,
                renderers = new Renderer[] { meshRenderer }
            };
            group.SetLODs(new LOD[] { lod });
            group.size = 1.63676f;
            group.RecalculateBounds();
            group.enabled = true;
        });





        WeaponInfo weaponInfo = Scriptable((WeaponInfo info) => {
        });


        var scimitar = GameObjectCreator.FindGameObjectByExactPath("AAM4");
        var fireParticlesBooster = scimitar.transform.Find("FireParticlesBooster").gameObject;
        var fireParticlesSustainer = scimitar.transform.Find("FireParticlesSustainer").gameObject;
        var smokeTrailSustainer = scimitar.transform.Find("smokeTrailSustainer").gameObject;
        var nestedSmokeTrailSustainer = scimitar.transform.Find("smokeTrailSustainer/smokeTrailSustainer").gameObject;
        var smokeParticles = scimitar.transform.Find("smokeParticles").gameObject;
        var smokeTrailBooster = scimitar.transform.Find("smokeParticles/smokeTrailBooster").gameObject;


        var missileObject = new GameObject("KEM");
        baseObj.transform.SetParent(null);
        baseObj.hideFlags = HideFlags.DontSaveInEditor | HideFlags.DontSaveInBuild | HideFlags.HideAndDontSave;
        baseObj.SetActive(false);

        GameObjectCreator.Missile(missileObject, (missile) =>
        {

        });

        MountedMissile mounted = null;
        GameObjectCreator.MountedMissile(mountedMissile, (missile) =>
        {
            mounted = missile;
            MountedMissileCreator.Modify(
                missile,
                weaponInfo,
                ammo: 1);
        });

        middle.localPosition = new Vector3(0, -0.15f, 0.15f);
        bottom.localPosition = new Vector3(0, -0.4f, 0.7f);

        var scale = new Vector3(1, 1.6f, 1f);
        middle.localScale = scale;



        bottom.SetParent(middle);
        middle.SetParent(top);
        WeaponMount weaponMount = Scriptable((WeaponMount mount) =>
        {

            mount.jsonKey = "SAM_Radar2_single";
            mount.info = weaponInfo;
            mount.mountName = "R9";
            mount.ammo = 1;
            mount.turret = false;
            mount.missileBay = false;
            mount.radar = false;
            mount.tailHook = false;
            mount.countermeasure = false;
            mount.colorable = false;
            mount.sortWeapons = true;
            mount.GearSafety = true;
            mount.GroundSafety = true;
            mount.GunAmmo = false;
            mount.emptyCost = 0.01f;
            mount.emptyMass = 30;
            mount.mass = 530;
            mount.drag = 1;
            mount.emptyDrag = 0;
            mount.RCS = 0.3f;
            mount.emptyRCS = 0;
            mount.disabled = false;
            mount.dontAutomaticallyAddToEncyclopedia = false;
        });



        WeaponMountToHardpoints(weaponMount, new Dictionary<string, HashSet<int>>
        {
            { "AttackHelo1", new HashSet<int>{2} },
            { "Fighter1", new HashSet<int>{2} },
            { "Multirole1", new HashSet<int>{4,5} }
        });

        weaponMount.prefab = baseObj;
        Weapons.Add(weaponMount);

        Logger.LogInfo($"Created Weapon Mount {weaponMount.name}");
    }

    public static T Scriptable<T>(Action<T> action) where T : ScriptableObject
    {
        var scriptable = ScriptableObject.CreateInstance<T>();
        action.Invoke(scriptable);
        return scriptable;
    }


    public static void WeaponMountToHardpoints(WeaponMount weaponMount, Dictionary<string, HashSet<int>> mounts)
    {
        foreach (var vehicle in mounts)
        {
            var vehicleObj = GameObjectCreator.FindGameObjectByExactPath(vehicle.Key);
            var manager = vehicleObj.GetComponentsInChildren<WeaponManager>(includeInactive: true);

            foreach (WeaponManager weaponManager in manager)
            {
                try
                {
                    foreach (var mount in vehicle.Value)
                    {
                        HardpointSet hardpointSet = weaponManager.hardpointSets[mount];
                        hardpointSet.weaponOptions.Add(weaponMount);
                    }
                }
                catch (Exception ex)
                {
                    Logger.LogError($"Failed to add {weaponMount.name} to {vehicle.Key}: " + ex.Message);
                }
            }
        }
    }
}


