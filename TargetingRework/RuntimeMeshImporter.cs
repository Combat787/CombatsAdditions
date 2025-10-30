using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using UnityEngine;


public class RuntimeMeshImporter
{
    public static Mesh ImportOBJ(string filePath)
    {
        if (!File.Exists(filePath))
        {
            Debug.LogError($"File not found: {filePath}");
            return null;
        }

        List<Vector3> vertices = new List<Vector3>();
        List<Vector2> uvs = new List<Vector2>();
        List<Vector3> normals = new List<Vector3>();
        List<int> triangles = new List<int>();

        string[] lines = File.ReadAllLines(filePath);

        foreach (string line in lines)
        {
            string[] parts = line.Split(' ');

            if (parts.Length == 0) continue;

            switch (parts[0])
            {
                case "v": // Vertex
                    if (parts.Length >= 4)
                    {
                        float x = ParseFloat(parts[1]);
                        float y = ParseFloat(parts[2]);
                        float z = ParseFloat(parts[3]);
                        vertices.Add(new Vector3(x, y, z));
                    }
                    break;

                case "vt": // UV coordinates
                    if (parts.Length >= 3)
                    {
                        float u = ParseFloat(parts[1]);
                        float v = ParseFloat(parts[2]);
                        uvs.Add(new Vector2(u, v));
                    }
                    break;

                case "vn": // Normals
                    if (parts.Length >= 4)
                    {
                        float x = ParseFloat(parts[1]);
                        float y = ParseFloat(parts[2]);
                        float z = ParseFloat(parts[3]);
                        normals.Add(new Vector3(x, y, z));
                    }
                    break;

                case "f": // Faces
                    if (parts.Length >= 4)
                    {
                        // Simple triangulation for quads
                        int[] indices = new int[parts.Length - 1];

                        for (int i = 1; i < parts.Length; i++)
                        {
                            string[] faceData = parts[i].Split('/');
                            indices[i - 1] = int.Parse(faceData[0]) - 1; // OBJ indices start at 1
                        }

                        // Create triangles
                        for (int i = 1; i < indices.Length - 1; i++)
                        {
                            triangles.Add(indices[0]);
                            triangles.Add(indices[i]);
                            triangles.Add(indices[i + 1]);
                        }
                    }
                    break;
            }
        }

        // Create Unity Mesh
        Mesh mesh = new Mesh();
        mesh.vertices = vertices.ToArray();
        mesh.triangles = triangles.ToArray();

        if (uvs.Count == vertices.Count)
            mesh.uv = uvs.ToArray();

        if (normals.Count == vertices.Count)
            mesh.normals = normals.ToArray();
        else
            mesh.RecalculateNormals();

        mesh.RecalculateBounds();

        return mesh;
    }

    private static float ParseFloat(string value)
    {
        return float.Parse(value, CultureInfo.InvariantCulture);
    }
}