// Copyright 2026 Capitol Interactive LLC
//
// Licensed under the Apache License, Version 2.0 (the "License");
// you may not use this file except in compliance with the License.
// You may obtain a copy of the License at
//
//      http://www.apache.org/licenses/LICENSE-2.0
//
// Unless required by applicable law or agreed to in writing, software
// distributed under the License is distributed on an "AS IS" BASIS,
// WITHOUT WARRANTIES OR CONDITIONS OF ANY KIND, either express or implied.
// See the License for the specific language governing permissions and
// limitations under the License.

using System.Collections.Generic;
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// Flat-colour materials and the few meshes the UI is made of. Everything is double-sided so
    /// a flipped triangle can never make a button invisible, and uses an unlit shader so it looks
    /// the same under any Open Brush lighting.
    /// </summary>
    public static class TwGfx
    {
        private static readonly Dictionary<uint, Material> s_Materials = new Dictionary<uint, Material>();
        private static Shader s_Shader;
        private static bool s_ShaderSearched;

        // In order of preference. A build only has a shader that something references or that is in
        // Always Included Shaders (TelewheelBuildPrep makes sure the first ones are), so there are fallbacks.
        private static readonly string[] ShaderNames =
        {
            "Unlit/Color", "Sprites/Default", "Hidden/Internal-Colored", "UI/Default",
        };

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Materials.Clear();
            s_Shader = null;
            s_ShaderSearched = false;
        }

        /// <summary>The shader the flat materials use, or null if this build has none of the candidates.</summary>
        public static string ShaderName
        {
            get
            {
                FindShader();
                return s_Shader == null ? null : s_Shader.name;
            }
        }

        private static void FindShader()
        {
            if (s_Shader != null || s_ShaderSearched)
            {
                return;
            }
            s_ShaderSearched = true;
            foreach (string name in ShaderNames)
            {
                s_Shader = Shader.Find(name);
                if (s_Shader != null)
                {
                    if (name != ShaderNames[0])
                    {
                        Debug.LogWarning("[Telewheel] " + ShaderNames[0] + " is not in this build; using " + name + ".");
                    }
                    return;
                }
            }
            Debug.LogError("[Telewheel] No unlit shader is available in this build, so the UI cannot be drawn.");
        }

        public static Color ToColor(uint rgb, float alpha = 1f)
        {
            return new Color(
                TwTokens.R(rgb) / 255f, TwTokens.G(rgb) / 255f, TwTokens.B(rgb) / 255f, alpha);
        }

        /// <summary>One shared material per colour.</summary>
        public static Material Flat(uint rgb)
        {
            Material material;
            if (s_Materials.TryGetValue(rgb, out material) && material != null)
            {
                return material;
            }
            FindShader();
            if (s_Shader == null)
            {
                return null; // Logged once by FindShader; every caller only assigns this to a renderer.
            }
            material = new Material(s_Shader) { color = ToColor(rgb) };
            material.name = "Telewheel flat " + rgb.ToString("X6");
            s_Materials[rgb] = material;
            return material;
        }

        // ----- Meshes -----

        /// <summary>A rounded rectangle centred on the origin in the XY plane (metres).</summary>
        public static Mesh RoundedRect(float width, float height, float radius, int cornerSteps = 6)
        {
            radius = Mathf.Min(radius, Mathf.Min(width, height) * 0.5f);
            var outline = new List<Vector2>();
            float hw = width * 0.5f;
            float hh = height * 0.5f;
            // Corners in order, each swept through 90 degrees.
            AddCorner(outline, new Vector2(hw - radius, hh - radius), radius, 0f, cornerSteps);
            AddCorner(outline, new Vector2(-hw + radius, hh - radius), radius, 90f, cornerSteps);
            AddCorner(outline, new Vector2(-hw + radius, -hh + radius), radius, 180f, cornerSteps);
            AddCorner(outline, new Vector2(hw - radius, -hh + radius), radius, 270f, cornerSteps);
            return Fan(Vector2.zero, outline);
        }

        /// <summary>A disc, or the slice of one between two clockwise-from-top angles (degrees).</summary>
        public static Mesh Wedge(float radius, float fromDegrees, float toDegrees, int steps)
        {
            var outline = new List<Vector2>();
            for (int i = 0; i <= steps; i++)
            {
                float t = fromDegrees + (toDegrees - fromDegrees) * i / steps;
                outline.Add(Clockwise(t) * radius);
            }
            return Fan(Vector2.zero, outline, false);
        }

        public static Mesh Disc(float radius, int steps = 32)
        {
            var outline = new List<Vector2>();
            for (int i = 0; i < steps; i++)
            {
                outline.Add(Clockwise(360f * i / steps) * radius);
            }
            return Fan(Vector2.zero, outline, true);
        }

        /// <summary>A triangle for the wheel pointer: tip at the origin, pointing down (towards -Y).</summary>
        public static Mesh Pointer(float width, float height)
        {
            var mesh = new Mesh();
            mesh.vertices = new[]
            {
                new Vector3(0f, 0f, 0f),
                new Vector3(-width * 0.5f, height, 0f),
                new Vector3(width * 0.5f, height, 0f),
            };
            mesh.triangles = new[] { 0, 1, 2, 0, 2, 1 };
            mesh.RecalculateBounds();
            return mesh;
        }

        /// <summary>A direction in the XY plane from an angle measured clockwise from straight up.</summary>
        public static Vector2 Clockwise(float degrees)
        {
            float radians = degrees * Mathf.Deg2Rad;
            return new Vector2(Mathf.Sin(radians), Mathf.Cos(radians));
        }

        private static void AddCorner(List<Vector2> outline, Vector2 center, float radius, float startDegrees, int steps)
        {
            for (int i = 0; i <= steps; i++)
            {
                float a = (startDegrees + 90f * i / steps) * Mathf.Deg2Rad;
                outline.Add(center + new Vector2(Mathf.Cos(a), Mathf.Sin(a)) * radius);
            }
        }

        /// <summary>
        /// A triangle fan from <paramref name="center"/> over an outline, drawn from both sides.
        /// When <paramref name="closed"/> the last outline point joins back to the first.
        /// </summary>
        private static Mesh Fan(Vector2 center, List<Vector2> outline, bool closed = true)
        {
            var vertices = new Vector3[outline.Count + 1];
            vertices[0] = new Vector3(center.x, center.y, 0f);
            for (int i = 0; i < outline.Count; i++)
            {
                vertices[i + 1] = new Vector3(outline[i].x, outline[i].y, 0f);
            }
            int segments = closed ? outline.Count : outline.Count - 1;
            var triangles = new int[segments * 6];
            for (int i = 0; i < segments; i++)
            {
                int a = i + 1;
                int b = (i + 1) % outline.Count + 1;
                int t = i * 6;
                triangles[t] = 0;
                triangles[t + 1] = a;
                triangles[t + 2] = b;
                triangles[t + 3] = 0;
                triangles[t + 4] = b;
                triangles[t + 5] = a;
            }
            var mesh = new Mesh();
            mesh.vertices = vertices;
            mesh.triangles = triangles;
            mesh.RecalculateBounds();
            return mesh;
        }

        // ----- Objects -----

        /// <summary>A child object drawn with a mesh and a flat colour. Returns the object.</summary>
        public static GameObject Shape(Transform parent, string name, Mesh mesh, uint rgb, Vector3 localPosition)
        {
            var go = new GameObject(name);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.AddComponent<MeshFilter>().sharedMesh = mesh;
            MeshRenderer renderer = go.AddComponent<MeshRenderer>();
            renderer.sharedMaterial = Flat(rgb);
            renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
            renderer.receiveShadows = false;
            return go;
        }
    }
}
