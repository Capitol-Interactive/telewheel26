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
using System.Diagnostics;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Build;
using UnityEngine;
using Debug = UnityEngine.Debug;

namespace Telewheel
{
    /// <summary>
    /// Telewheel > Online: gets the project ready to play online over Photon. The SDK itself is not
    /// in git; <c>Support/bin/setup-photon.py</c> downloads it into Assets/Photon, and this adds the
    /// scripting define symbols Open Brush's multiplayer code is compiled with. The Photon app ids
    /// are typed into Assets/Secrets.asset by hand (see CLAUDE.md) and never committed.
    /// </summary>
    public static class TelewheelPhotonSetup
    {
        // Open Brush's own symbols, and the ones the CI adds when it has the SDK.
        private static readonly string[] OpenBrushDefines =
        {
            "MP_PHOTON", "CROSS_PLATFORM_INPUT", "PHOTON_VOICE_DEFINED",
        };

        // Fusion's installer normally sets these itself; they are added too in case it has not run yet.
        private static readonly string[] FusionDefines =
        {
            "FUSION_WEAVER", "FUSION2", "FUSION_2_1_OR_NEWER",
        };

        private static readonly string[] AndroidOnlyDefines = { "MOBILE_INPUT" };

        private const string SdkMarker = "Assets/Photon/Fusion/Runtime/Fusion.Unity.asmdef";
        private const string SecretsPath = "Assets/Secrets.asset";

        // SecretsConfig.Service values.
        private const int ServicePhotonFusion = 5;
        private const int ServicePhotonVoice = 6;

        [MenuItem("Telewheel/Online/Set Up Photon")]
        public static void SetUp()
        {
            if (!SdkInstalled())
            {
                bool download = EditorUtility.DisplayDialog(
                    "Photon SDK not found",
                    "Online play needs the Photon Fusion and Voice SDK in Assets/Photon, which is not stored "
                    + "in git. Download it now with Support/bin/setup-photon.py?",
                    "Download", "Cancel");
                if (!download || !RunSetupScript())
                {
                    return;
                }
                AssetDatabase.Refresh();
            }
            ApplyDefines(true);
            Debug.Log("[Telewheel] Photon define symbols added. Unity will recompile now.");
            ShowReport("Photon is set up. Unity will recompile.");
        }

        [MenuItem("Telewheel/Online/Check Photon Setup")]
        public static void Check()
        {
            ShowReport("Photon setup");
        }

        [MenuItem("Telewheel/Online/Remove Photon Defines")]
        public static void RemoveDefines()
        {
            ApplyDefines(false);
            Debug.Log("[Telewheel] Photon define symbols removed. Unity will recompile now.");
        }

        private static bool SdkInstalled()
        {
            return File.Exists(Path.Combine(ProjectRoot(), SdkMarker));
        }

        private static string ProjectRoot()
        {
            return Path.GetDirectoryName(Application.dataPath);
        }

        // ----- Define symbols -----

        private static void ApplyDefines(bool add)
        {
            var standalone = new List<string>(OpenBrushDefines);
            var android = new List<string>(OpenBrushDefines);
            android.AddRange(AndroidOnlyDefines);
            if (add)
            {
                standalone.AddRange(FusionDefines);
                android.AddRange(FusionDefines);
            }
            Apply(NamedBuildTarget.Standalone, standalone, add);
            Apply(NamedBuildTarget.Android, android, add);
        }

        // Removing only takes out Open Brush's symbols: Fusion's own installer looks after its own.
        private static void Apply(NamedBuildTarget target, List<string> symbols, bool add)
        {
            string text = PlayerSettings.GetScriptingDefineSymbols(target);
            var current = new List<string>(text.Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            bool changed = false;
            foreach (string symbol in symbols)
            {
                if (add && !current.Contains(symbol))
                {
                    current.Add(symbol);
                    changed = true;
                }
                else if (!add && current.Remove(symbol))
                {
                    changed = true;
                }
            }
            if (changed)
            {
                PlayerSettings.SetScriptingDefineSymbols(target, string.Join(";", current.ToArray()));
            }
        }

        private static bool HasDefines(NamedBuildTarget target, IEnumerable<string> symbols)
        {
            var current = new List<string>(
                PlayerSettings.GetScriptingDefineSymbols(target).Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries));
            foreach (string symbol in symbols)
            {
                if (!current.Contains(symbol))
                {
                    return false;
                }
            }
            return true;
        }

        // ----- Report -----

        private static void ShowReport(string title)
        {
            var report = new StringBuilder();
            bool sdk = SdkInstalled();
            report.AppendLine("Photon SDK in Assets/Photon: " + (sdk ? "yes" : "NO (run Support/bin/setup-photon.py)"));
            report.AppendLine("Define symbols (PC):   " + (HasDefines(NamedBuildTarget.Standalone, OpenBrushDefines) ? "set" : "MISSING"));
            report.AppendLine("Define symbols (Quest): " + (HasDefines(NamedBuildTarget.Android, OpenBrushDefines) ? "set" : "MISSING"));
            AppendSecrets(report);
            Debug.Log("[Telewheel] " + title + "\n" + report);
            EditorUtility.DisplayDialog(title, report.ToString(), "OK");
        }

        private static void AppendSecrets(StringBuilder report)
        {
            var secrets = AssetDatabase.LoadAssetAtPath<SecretsConfig>(SecretsPath);
            if (secrets == null)
            {
                report.AppendLine("Assets/Secrets.asset: MISSING (Create > Secrets Config, see CLAUDE.md)");
                return;
            }
            report.AppendLine("Assets/Secrets.asset: found");
            report.AppendLine("  Fusion app id: " + (HasClientId(secrets, ServicePhotonFusion) ? "set" : "MISSING"));
            report.AppendLine("  Voice app id:  " + (HasClientId(secrets, ServicePhotonVoice) ? "set" : "MISSING"));
        }

        private static bool HasClientId(SecretsConfig secrets, int service)
        {
            if (secrets.Secrets == null)
            {
                return false;
            }
            foreach (SecretsConfig.ServiceAuthData data in secrets.Secrets)
            {
                if (data != null && (int)data.Service == service && !string.IsNullOrEmpty(data.ClientId))
                {
                    return true;
                }
            }
            return false;
        }

        // ----- Running the download script -----

        private static bool RunSetupScript()
        {
            string script = Path.Combine(ProjectRoot(), "Support", "bin", "setup-photon.py");
            if (!File.Exists(script))
            {
                Debug.LogError("[Telewheel] Support/bin/setup-photon.py is missing.");
                return false;
            }
            // Windows has the "py" launcher; elsewhere it is python3 (or python).
            string[] launchers = Application.platform == RuntimePlatform.WindowsEditor
                ? new[] { "py", "python", "python3" }
                : new[] { "python3", "python" };
            foreach (string launcher in launchers)
            {
                int result = TryRun(launcher, "\"" + script + "\" fetch");
                if (result == 0)
                {
                    return true;
                }
                if (result > 0)
                {
                    Debug.LogError("[Telewheel] The Photon download failed (see the messages above).");
                    return false;
                }
            }
            Debug.LogError("[Telewheel] Python was not found. Install Python 3, or run setup-photon.py yourself.");
            return false;
        }

        // Returns the exit code, or -1 if the program could not be started.
        private static int TryRun(string program, string arguments)
        {
            try
            {
                EditorUtility.DisplayProgressBar("Photon SDK", "Downloading the Photon SDK...", 0.5f);
                var info = new ProcessStartInfo(program, arguments)
                {
                    WorkingDirectory = ProjectRoot(),
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                };
                using (Process process = Process.Start(info))
                {
                    string output = process.StandardOutput.ReadToEnd();
                    string errors = process.StandardError.ReadToEnd();
                    process.WaitForExit();
                    Debug.Log("[Telewheel] setup-photon.py:\n" + output + errors);
                    return process.ExitCode;
                }
            }
            catch (System.ComponentModel.Win32Exception)
            {
                return -1;
            }
            finally
            {
                EditorUtility.ClearProgressBar();
            }
        }
    }
}
