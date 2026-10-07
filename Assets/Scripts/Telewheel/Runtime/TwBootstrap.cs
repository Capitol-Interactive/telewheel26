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

using UnityEngine;
using UnityEngine.SceneManagement;

namespace Telewheel
{
    /// <summary>
    /// Starts Telewheel without any change to Main.unity. Builds load Loading.unity first and Main
    /// afterwards, so this watches every scene load and creates the director once Open Brush's App
    /// exists.
    /// </summary>
    public static class TwBootstrap
    {
        private static GameObject s_Root;

        // Domain reload is off in this project, so statics and event subscriptions survive between
        // Play sessions in the editor. Reset them before anything else runs.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            s_Root = null;
            TwHooks.Reset();
        }

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.AfterSceneLoad)]
        private static void Init()
        {
            SceneManager.sceneLoaded -= OnSceneLoaded;
            SceneManager.sceneLoaded += OnSceneLoaded;
            TrySpawn();
        }

        private static void OnSceneLoaded(Scene scene, LoadSceneMode mode)
        {
            TrySpawn();
        }

        private static void TrySpawn()
        {
            if (s_Root != null)
            {
                return;
            }
            if (!OpenBrushFacade.ConfigLoaded || !OpenBrushFacade.Enabled)
            {
                return;
            }
            s_Root = new GameObject("Telewheel");
            // The new object lands in the active scene, which may still be the loading scene.
            SceneManager.MoveGameObjectToScene(s_Root, OpenBrushFacade.AppScene);
            s_Root.AddComponent<TwDirector>();
        }
    }
}
