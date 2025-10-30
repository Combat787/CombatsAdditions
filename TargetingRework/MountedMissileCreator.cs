using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;


    public static class MountedMissileCreator
    {
        public static void Modify(
            MountedMissile missile,
            WeaponInfo info,
            int ammo = 1,
            MountedMissile.RailDirection railDirection = MountedMissile.RailDirection.Forward,
            float railLength = 0,
            float railSpeed = 0,
            float railDelay = 0,
            AudioClip deploySound = null,
            float deployVolume = 0.5f,
            float doorOpenDuration = 0.5f,
            BayDoor[] bayDoors = null)
        {
            missile.info = info;
            missile.ammo = ammo;
            SetPrivateField(missile, "railDirection", railDirection);
            SetPrivateField(missile, "railLength", railLength);
            SetPrivateField(missile, "railSpeed", railSpeed);
            SetPrivateField(missile, "railDelay", railDelay);
            SetPrivateField(missile, "deploySound", deploySound);
            SetPrivateField(missile, "deployVolume", deployVolume);
            SetPrivateField(missile, "doorOpenDuration", doorOpenDuration);
            SetPrivateField(missile, "bayDoors", bayDoors ?? new BayDoor[0]);
            SetPrivateField(missile, "railPosition", 0f);
            SetPrivateField(missile, "fired", false);
            SetPrivateField(missile, "railVector", Vector3.zero);
        }

        private static void SetPrivateField(object obj, string fieldName, object value)
        {
            FieldInfo field = obj.GetType().GetField(fieldName,
                BindingFlags.NonPublic | BindingFlags.Instance);
            field?.SetValue(obj, value);
        }
    }