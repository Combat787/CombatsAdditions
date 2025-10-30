using System;
using System.Linq;
using UnityEngine;

public static class GameObjectCreator
{
    public static void Transform(GameObject obj, Action<Transform> action)
    {
        var transform = obj.GetComponent<Transform>();

        action.Invoke(transform);
    }
    public static void Mesh(GameObject obj, Action<MeshRenderer, MeshFilter> action)
    {
        var meshFilter = obj.AddComponent<MeshFilter>();
        var meshRenderer = obj.AddComponent<MeshRenderer>();

        action.Invoke(meshRenderer, meshFilter);
    }

    public static void Collider<T>(GameObject obj, Action<T> action) where T : Collider
    {
        var collider = obj.AddComponent<T>();
        action.Invoke(collider);
    }

    public static void LODGroup(GameObject obj, Action<LODGroup> action)
    {

        var lodGroup = obj.AddComponent<LODGroup>();
        action.Invoke(lodGroup);
    }

    public static void AudioSource(GameObject obj, Action<AudioSource> action)
    {
        var audioSource = obj.AddComponent<AudioSource>();
        action.Invoke(audioSource);
    }

    public static void MountedMissile(GameObject obj, Action<MountedMissile> action)
    {
        var mountedMissile = obj.AddComponent<MountedMissile>();
        action.Invoke(mountedMissile);
    }
    public static void Missile(GameObject obj, Action<Missile> action)
    {
        var missile = obj.AddComponent<Missile>();
        action.Invoke(missile);
    }
    public static Mesh GetMeshByName(string name)
    {
        var mesh = Resources.FindObjectsOfTypeAll<Mesh>().FirstOrDefault(m => m.name == name);
        if (mesh != null)
            return mesh;

        WeaponsLoader.Logger.LogWarning($"Mesh '{name}' not found in loaded assets!");
        return null;
    }

    public static Material GetMaterialByName(string name)
    {
        var material = Resources.FindObjectsOfTypeAll<Material>().FirstOrDefault(m => m.name == name);
        if (material != null)
            return material;

        WeaponsLoader.Logger.LogWarning($"Material '{name}' not found in loaded assets!");
        return null;
    }

    public static GameObject FindGameObjectByExactPath(string path)
    {
        return Resources.FindObjectsOfTypeAll<GameObject>().FirstOrDefault(go => GetFullPath(go.transform) == path);
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

}
