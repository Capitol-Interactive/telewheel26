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
using System.Linq;
using UnityEditor;
using UnityEditor.Build;
using UnityEditor.Build.Reporting;
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// Runs before every player build. Telewheel's UI looks its shaders up by name at runtime
    /// (<c>Shader.Find</c>), and a build only contains a shader that something references or that is
    /// listed in Always Included Shaders, so a UI that works in the editor can come out pink or invisible
    /// on a headset. This makes sure the shaders it needs are listed for the build, and takes out the ones
    /// it added afterwards, so GraphicsSettings.asset is left as it was (<c>BuildTiltBrush.DoBuild</c> also
    /// restores the file, but a build started from Unity's own dialog does not).
    /// </summary>
    public class TelewheelBuildPrep : IPreprocessBuildWithReport, IPostprocessBuildWithReport
    {
        private const string LogPrefix = "[Telewheel build prep]";

        // After UrpBrushShaderWarmup, which also edits the graphics settings.
        public int callbackOrder => 10;

        // The first is the one the UI is drawn with. If it cannot be found, TwGfx falls back to the others.
        private static readonly string[] AlwaysIncluded =
        {
            "Unlit/Color",
            "Sprites/Default",
            "Hidden/Internal-Colored",
            "TextMeshPro/Distance Field",
            "TextMeshPro/Mobile/Distance Field",
        };

        // The shaders this build added, to take out again when it is over.
        private static readonly List<Shader> s_Added = new List<Shader>();

        public void OnPreprocessBuild(BuildReport report)
        {
            EnsureAlwaysIncludedShaders(s_Added);
        }

        public void OnPostprocessBuild(BuildReport report)
        {
            RemoveAdded();
        }

        /// <summary>For people who want the shaders listed for good, in the project settings.</summary>
        [MenuItem("Telewheel/Build/Add UI Shaders To Always Included")]
        public static void AddUiShadersPermanently()
        {
            EnsureAlwaysIncludedShaders(null);
        }

        private static void EnsureAlwaysIncludedShaders(List<Shader> added)
        {
            Object graphicsSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")
                .FirstOrDefault();
            if (graphicsSettings == null)
            {
                throw new BuildFailedException(LogPrefix + " could not load ProjectSettings/GraphicsSettings.asset.");
            }
            var serialized = new SerializedObject(graphicsSettings);
            SerializedProperty list = serialized.FindProperty("m_AlwaysIncludedShaders");
            if (list == null || !list.isArray)
            {
                throw new BuildFailedException(LogPrefix + " could not find GraphicsSettings.m_AlwaysIncludedShaders.");
            }

            bool changed = false;
            foreach (string name in AlwaysIncluded)
            {
                Shader shader = Shader.Find(name);
                if (shader == null)
                {
                    Debug.LogWarning(LogPrefix + " the shader " + name + " was not found; the UI falls back to another.");
                    continue;
                }
                if (Contains(list, shader))
                {
                    continue;
                }
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                if (added != null)
                {
                    added.Add(shader);
                }
                changed = true;
                Debug.Log(LogPrefix + " added " + name + " to Always Included Shaders.");
            }
            if (changed)
            {
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(graphicsSettings);
            }
        }

        private static void RemoveAdded()
        {
            if (s_Added.Count == 0)
            {
                return;
            }
            Object graphicsSettings = AssetDatabase.LoadAllAssetsAtPath("ProjectSettings/GraphicsSettings.asset")
                .FirstOrDefault();
            SerializedProperty list = null;
            SerializedObject serialized = null;
            if (graphicsSettings != null)
            {
                serialized = new SerializedObject(graphicsSettings);
                list = serialized.FindProperty("m_AlwaysIncludedShaders");
            }
            if (list != null && list.isArray)
            {
                foreach (Shader shader in s_Added)
                {
                    for (int i = list.arraySize - 1; i >= 0; i--)
                    {
                        if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                        {
                            // Deleting a non-null object reference only clears it; the second call removes the slot.
                            list.GetArrayElementAtIndex(i).objectReferenceValue = null;
                            list.DeleteArrayElementAtIndex(i);
                            break;
                        }
                    }
                }
                serialized.ApplyModifiedProperties();
            }
            s_Added.Clear();
        }

        private static bool Contains(SerializedProperty list, Shader shader)
        {
            for (int i = 0; i < list.arraySize; i++)
            {
                if (list.GetArrayElementAtIndex(i).objectReferenceValue == shader)
                {
                    return true;
                }
            }
            return false;
        }
    }
}
