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
    /// on a headset. This makes sure the shaders it needs are listed. <c>BuildTiltBrush.DoBuild</c> puts
    /// GraphicsSettings.asset back afterwards, so the committed file does not change.
    /// </summary>
    public class TelewheelBuildPrep : IPreprocessBuildWithReport
    {
        private const string LogPrefix = "[Telewheel build prep]";

        // After UrpBrushShaderWarmup, which also edits the graphics settings.
        public int callbackOrder => 10;

        // The first is the one the UI is drawn with; a build without it fails rather than shipping a blank UI.
        private static readonly string[] AlwaysIncluded =
        {
            "Unlit/Color",
            "Sprites/Default",
            "Hidden/Internal-Colored",
            "TextMeshPro/Distance Field",
            "TextMeshPro/Mobile/Distance Field",
        };

        public void OnPreprocessBuild(BuildReport report)
        {
            EnsureAlwaysIncludedShaders();
        }

        [MenuItem("Telewheel/Build/Add UI Shaders To Always Included")]
        public static void EnsureAlwaysIncludedShaders()
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
                    if (name == AlwaysIncluded[0])
                    {
                        throw new BuildFailedException(
                            LogPrefix + " the shader " + name + " was not found, so Telewheel's UI would be blank.");
                    }
                    Debug.LogWarning(LogPrefix + " the shader " + name + " was not found; the UI falls back to another.");
                    continue;
                }
                if (Contains(list, shader))
                {
                    continue;
                }
                list.InsertArrayElementAtIndex(list.arraySize);
                list.GetArrayElementAtIndex(list.arraySize - 1).objectReferenceValue = shader;
                changed = true;
                Debug.Log(LogPrefix + " added " + name + " to Always Included Shaders.");
            }
            if (changed)
            {
                serialized.ApplyModifiedProperties();
                EditorUtility.SetDirty(graphicsSettings);
            }
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
