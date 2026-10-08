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

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// Telewheel > Build: makes a local Android build a Telewheel Quest build, the way the CI's
    /// "Android Meta Quest" row is. That row adds the compile symbol <c>USE_QUEST_PACKAGE_NAME</c>, which
    /// gives the build Telewheel's application id, the Quest manifest entries (passthrough, hand tracking,
    /// no all-files storage permission), the OpenXR features Quest needs, and app-private storage. The
    /// symbol is kept in your local project settings only; CI refuses it in the committed ones.
    /// </summary>
    public static class TelewheelQuestSetup
    {
        private const string QuestDefine = "USE_QUEST_PACKAGE_NAME";

        [MenuItem("Telewheel/Build/Set Up Quest Build")]
        public static void SetUp()
        {
            Apply(true);
            Debug.Log("[Telewheel] Quest build symbol added for Android. Unity will recompile once the Android build target is active.");
            Report("Quest build is set up.");
        }

        [MenuItem("Telewheel/Build/Remove Quest Build Setup")]
        public static void Remove()
        {
            Apply(false);
            Debug.Log("[Telewheel] Quest build symbol removed for Android.");
        }

        [MenuItem("Telewheel/Build/Check Quest Setup")]
        public static void Check()
        {
            Report("Quest build setup");
        }

        private static void Apply(bool add)
        {
            var current = new List<string>(Symbols());
            bool has = current.Contains(QuestDefine);
            if (add && !has)
            {
                current.Add(QuestDefine);
            }
            else if (!add && has)
            {
                current.Remove(QuestDefine);
            }
            else
            {
                return;
            }
            PlayerSettings.SetScriptingDefineSymbols(NamedBuildTarget.Android, string.Join(";", current.ToArray()));
        }

        private static string[] Symbols()
        {
            return PlayerSettings.GetScriptingDefineSymbols(NamedBuildTarget.Android)
                .Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static void Report(string title)
        {
            bool defined = Array.IndexOf(Symbols(), QuestDefine) >= 0;
            string text =
                "Quest symbol (" + QuestDefine + "): " + (defined ? "set" : "NOT set (Telewheel > Build > Set Up Quest Build)") + "\n"
                + "Active build target: " + EditorUserBuildSettings.activeBuildTarget + "\n\n"
                + "Switch the build target to Android (File > Build Profiles) so Unity compiles with the symbol, "
                + "then build with Open Brush > Build > Do Build (OpenXR, IL2CPP). The id is "
                + "com.capitolinteractive.telewheel, so it installs next to Open Brush.";
            Debug.Log("[Telewheel] " + title + "\n" + text);
            EditorUtility.DisplayDialog(title, text, "OK");
        }
    }
}
