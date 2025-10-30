using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;
using static Missile;


public static class MissileCreator
{
    public static void Modify(
        Missile missile,
        WeaponInfo weaponInfo,
        // Physics parameters
        float mass = 100f,
        float finArea = 1f,
        float uprightPreference = 0f,
        float supersonicDrag = 1f,
        AnimationCurve liftCurve = null,
        AnimationCurve dragCurve = null,
        ArmorProperties armorProperties = default,
        Action<ArmorProperties> armorPropertiesAction = null,
        // Motor configuration
        MotorConfig[] motorConfigs = null,
        // Targeting parameters
        PIDFactors pidFactors = default,
        float torque = 100f,
        float maxTurnRate = 3f,
        bool proximityFuse = false,
        // Effects
        Transform effectsTransform = null,
        AudioSource flightSound = null,
        AudioClip nearbyDetonationClip = null,
        float basePitch = 0.5f,
        float pitchRange = 1f,
        float maxPitchSpeed = 340f,
        // Payload
        WarheadConfig warheadConfig = null,
        float blastYield = 1000f,
        float pierceDamage = 100f,
        bool impactFuse = true,
        float impactFuseDelay = 0f,
        // Unit info
        // Fins
        FoldingFinConfig[] foldingFinConfigs = null)
    {
        // Set sync vars (public fields)
        missile.ownerID = PersistentID.None;
        SetPrivateField(missile, "_targetID", -1);
        missile.startingVelocity = Vector3.zero;
        missile.startOffsetFromOwner = Vector3.zero;
        missile.seekerMode = SeekerMode.passive;

        SetPrivateField(missile, "mass", mass);
        SetPrivateField(missile, "finArea", finArea);
        SetPrivateField(missile, "uprightPreference", uprightPreference);
        SetPrivateField(missile, "supersonicDrag", supersonicDrag);
        SetPrivateField(missile, "liftCurve", liftCurve);
        SetPrivateField(missile, "dragCurve", dragCurve);
        armorPropertiesAction.Invoke(armorProperties);
        SetPrivateField(missile, "armorProperties", armorProperties);

        // Create and set motors
        var motors = CreateMotors(motorConfigs);
        SetPrivateField(missile, "motors", motors);

        // Set targeting fields
        SetPrivateField(missile, "PIDFactors", pidFactors);
        SetPrivateField(missile, "torque", torque);
        SetPrivateField(missile, "maxTurnRate", maxTurnRate);
        SetPrivateField(missile, "proximityFuse", proximityFuse);

        // Set effects fields
        SetPrivateField(missile, "effectsTransform", effectsTransform);
        SetPrivateField(missile, "flightSound", flightSound);
        SetPrivateField(missile, "nearbyDetonationClip", nearbyDetonationClip);
        SetPrivateField(missile, "basePitch", basePitch);
        SetPrivateField(missile, "pitchRange", pitchRange);
        SetPrivateField(missile, "maxPitchSpeed", maxPitchSpeed);

        // Create and set warhead
        var warhead = CreateWarhead(warheadConfig);
        SetPrivateField(missile, "warhead", warhead);
        SetPrivateField(missile, "blastYield", blastYield);
        SetPrivateField(missile, "pierceDamage", pierceDamage);
        SetPrivateField(missile, "impactFuse", impactFuse);
        SetPrivateField(missile, "impactFuseDelay", impactFuseDelay);

        // Set unit info
        SetPrivateField(missile, "info", weaponInfo);

        // Create and set folding fins
        var foldingFins = CreateFoldingFins(foldingFinConfigs);
        SetPrivateField(missile, "foldingFins", foldingFins);

        // Initialize other private fields to default values
        SetPrivateField(missile, "hitpoints", 100f);
        SetPrivateField(missile, "engineCurrentThrust", 0f);
        SetPrivateField(missile, "motorStage", 0);
        SetPrivateField(missile, "currentFinArea", 0f);
        SetPrivateField(missile, "throttle", 1f);
        SetPrivateField(missile, "ignition", false);
        SetPrivateField(missile, "tangible", false);
        SetPrivateField(missile, "aimVelocity", false);
    }

    public class MotorConfig
    {
        public float thrust = 1000f;
        public float burnTime = 10f;
        public float fuelMass = 50f;
        public float delayTimer = 0f;
        public ParticleSystem[] particleSystems = new ParticleSystem[0];
        public TrailEmitter[] trailEmitters = new TrailEmitter[0];
        public AudioSource[] audioSources = new AudioSource[0];
        public AudioSource startupSource = null;
        public Light[] lights = new Light[0];
        public GameObject[] destructEffects = new GameObject[0];
    }

    [Serializable]
    public class WarheadConfig
    {
        public bool armed = true;
        public GameObject airEffect = null;
        public GameObject armorEffect = null;
        public GameObject terrainEffect = null;
        public GameObject waterSurfaceEffect = null;
        public GameObject underwaterEffect = null;
        public GameObject fizzleEffect = null;
    }

    [Serializable]
    public class FoldingFinConfig
    {
        public Transform fin = null;
        public Vector3 foldAngle = Vector3.zero;
        public Vector3 deployAngle = Vector3.zero;
        public float deploySpeed = 1f;
    }

    private static object[] CreateMotors(MotorConfig[] motorConfigs)
    {
        if (motorConfigs == null || motorConfigs.Length == 0)
        {
            return new object[0];
        }

        Type motorType = typeof(Missile).GetNestedType("Motor", BindingFlags.NonPublic);
        object[] motors = new object[motorConfigs.Length];

        for (int i = 0; i < motorConfigs.Length; i++)
        {
            var motor = Activator.CreateInstance(motorType);
            var config = motorConfigs[i];

            SetPrivateField(motor, "thrust", config.thrust);
            SetPrivateField(motor, "burnTime", config.burnTime);
            SetPrivateField(motor, "fuelMass", config.fuelMass);
            SetPrivateField(motor, "delayTimer", config.delayTimer);
            SetPrivateField(motor, "particleSystems", config.particleSystems);
            SetPrivateField(motor, "trailEmitters", config.trailEmitters);
            SetPrivateField(motor, "audioSources", config.audioSources);
            SetPrivateField(motor, "startupSource", config.startupSource);
            SetPrivateField(motor, "lights", config.lights);
            SetPrivateField(motor, "destructEffects", config.destructEffects);
            SetPrivateField(motor, "activated", false);

            motors[i] = motor;
        }

        return motors;
    }

    private static object CreateWarhead(WarheadConfig warheadConfig)
    {
        object warhead = new Missile.Warhead();

        if (warheadConfig != null)
        {
            SetPrivateField(warhead, "Armed", warheadConfig.armed);
            SetPrivateField(warhead, "airEffect", warheadConfig.airEffect);
            SetPrivateField(warhead, "armorEffect", warheadConfig.armorEffect);
            SetPrivateField(warhead, "terrainEffect", warheadConfig.terrainEffect);
            SetPrivateField(warhead, "waterSurfaceEffect", warheadConfig.waterSurfaceEffect);
            SetPrivateField(warhead, "underwaterEffect", warheadConfig.underwaterEffect);
            SetPrivateField(warhead, "fizzleEffect", warheadConfig.fizzleEffect);
        }
        else
        {
            SetPrivateField(warhead, "Armed", true);
        }

        SetPrivateField(warhead, "detonated", false);

        return warhead;
    }

    private static object[] CreateFoldingFins(FoldingFinConfig[] finConfigs)
    {
        if (finConfigs == null || finConfigs.Length == 0)
        {
            return new object[0];
        }

        object[] fins = new object[finConfigs.Length];

        for (int i = 0; i < finConfigs.Length; i++)
        {
            var fin = new Missile.FoldingFin();
            var config = finConfigs[i];

            SetPrivateField(fin, "fin", config.fin);
            SetPrivateField(fin, "foldAngle", config.foldAngle);
            SetPrivateField(fin, "deployAngle", config.deployAngle);
            SetPrivateField(fin, "deploySpeed", config.deploySpeed);
            SetPrivateField(fin, "deployedAmount", 0f);

            fins[i] = fin;
        }

        return fins;
    }


    private static void SetPrivateField(object obj, string fieldName, object value)
    {
        Type type = obj.GetType();
        FieldInfo field = type.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);

        if (field == null)
        {
            Type baseType = type.BaseType;
            while (baseType != null && field == null)
            {
                field = baseType.GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.Public);
                baseType = baseType.BaseType;
            }
        }

        if (field != null)
        {
            field.SetValue(obj, value);
        }
        else
        {
            Debug.LogWarning($"Field '{fieldName}' not found in type '{type.Name}'");
        }
    }
}
