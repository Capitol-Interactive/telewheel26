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
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using TiltBrush;
using UnityEngine;

namespace Telewheel
{
    public sealed class TwCheck
    {
        public string Name;
        public bool Passed;
        public string Detail;
    }

    public sealed class TwSelfTestReport
    {
        public readonly List<TwCheck> Checks = new List<TwCheck>();
        public bool Finished;

        public int FailedCount
        {
            get { return Checks.Count(c => !c.Passed); }
        }

        public string ToText()
        {
            var sb = new StringBuilder();
            sb.AppendLine("Telewheel self-test: " + (Finished ? (FailedCount == 0 ? "ALL PASSED" : FailedCount + " FAILED") : "running"));
            foreach (TwCheck check in Checks)
            {
                sb.AppendLine((check.Passed ? "PASS  " : "FAIL  ") + check.Name
                    + (string.IsNullOrEmpty(check.Detail) ? string.Empty : " - " + check.Detail));
            }
            return sb.ToString();
        }
    }

    /// <summary>
    /// Exercises every Open Brush hook Telewheel depends on, in the running app, and reports
    /// PASS/FAIL for each. It was written without being able to run Unity, so a failing line here
    /// points straight at the assumption that was wrong.
    /// </summary>
    public static class TwSelfTest
    {
        public static IEnumerator Run(TwSelfTestReport report)
        {
            report.Checks.Clear();
            report.Finished = false;

            Check(report, "App ready", () => OpenBrushFacade.IsReadyToPlay, "App, brush catalog, panels and input are up");
            Check(report, "Hooks registered", () => TwHooks.CommandHandler != null && TwHooks.CommandAvailability != null, null);
            Check(report, "Brush", () =>
            {
                bool ok = OpenBrushFacade.SelectMarkerBrush();
                return ok ? OpenBrushFacade.ActiveBrushName : null;
            }, null);
            Check(report, "Head transform", () => OpenBrushFacade.Head != null, null);
            Check(report, "Pointer ray", () =>
            {
                Ray ray;
                return OpenBrushFacade.TryGetPointerRay(out ray) ? "origin " + ray.origin + " dir " + ray.direction : null;
            }, null);
            Check(report, "Panels", CheckPanels, null);
            Check(report, "Word lists", () =>
            {
                int family = TwWords.Load(ContentFilter.Family).Count;
                int raunchy = TwWords.Load(ContentFilter.Raunchy).Count;
                int need = new MatchSettings().WheelSegments;
                bool ok = family >= need && raunchy >= need;
                return ok ? family + " family, " + raunchy + " raunchy" : "family " + family + ", raunchy " + raunchy + ", need " + need;
            }, null);
            Check(report, "UI shader", () =>
            {
                Shader shader = Shader.Find("Unlit/Color") ?? Shader.Find("Sprites/Default");
                return shader == null ? null : shader.name;
            }, null);
            Check(report, "Default font", () =>
            {
                return TMPro.TMP_Settings.defaultFontAsset == null ? null : TMPro.TMP_Settings.defaultFontAsset.name;
            }, null);

            // Drawing round trip: draw, save as bytes, wipe, then show the bytes on a stage.
            byte[] saved = null;
            Check(report, "Wipe canvas", () =>
            {
                OpenBrushFacade.ClearEverything();
                return OpenBrushFacade.ActiveStrokeCount == 0 && !OpenBrushFacade.CanUndo ? "empty" : null;
            }, null);
            yield return null;

            Check(report, "Draw a stroke", () =>
            {
                OpenBrushFacade.SetDrawingAllowed(true);
                int made = OpenBrushFacade.DrawSyntheticStroke(TestPath(), Color.red);
                return made == 1 && OpenBrushFacade.ActiveStrokeCount == 1 ? "1 stroke, undo " + OpenBrushFacade.CanUndo : null;
            }, null);
            Check(report, "Save drawing as bytes", () =>
            {
                saved = OpenBrushFacade.SerializeActiveStrokes();
                return saved != null && saved.Length > 0 ? saved.Length + " bytes" : null;
            }, null);
            Check(report, "Wipe clears undo", () =>
            {
                OpenBrushFacade.ClearEverything();
                return OpenBrushFacade.ActiveStrokeCount == 0 && !OpenBrushFacade.CanUndo ? "empty" : null;
            }, null);
            yield return null;

            var sketch = new TwSketchService();
            Check(report, "Show saved drawing", () =>
            {
                int shown = sketch.Show(saved, TwLayout.ToUnits(TwLayout.StageCenterMeters), TwLayout.StageSizeMeters);
                return shown == 1 && sketch.IsShowing && sketch.StageStrokeCount == 1
                    ? "1 stroke on the stage" : "shown " + shown + ", on stage " + sketch.StageStrokeCount;
            }, null);
            yield return null;

            Check(report, "Stage is display-only", () =>
            {
                // The shown drawing must not count as the player's drawing, nor be undoable.
                return OpenBrushFacade.ActiveStrokeCount == 0 && !OpenBrushFacade.CanUndo
                    ? "main canvas empty, nothing to undo" : "main " + OpenBrushFacade.ActiveStrokeCount + ", undo " + OpenBrushFacade.CanUndo;
            }, null);
            Check(report, "No stray layers", () =>
            {
                int layers = App.Scene.LayerCanvases.Count();
                return layers == 1 ? "1 stage layer" : "layers: " + layers;
            }, null);
            Check(report, "Hide shown drawing", () =>
            {
                sketch.Clear();
                return !sketch.IsShowing && App.Scene.LayerCanvases.Count() == 0 && OpenBrushFacade.ActiveStrokeCount == 0
                    ? "clean" : "layers " + App.Scene.LayerCanvases.Count();
            }, null);
            yield return null;

            // Leave the app ready for the game.
            OpenBrushFacade.ClearEverything();
            OpenBrushFacade.SetDrawingAllowed(false);

            report.Finished = true;
            string text = report.ToText();
            if (report.FailedCount == 0)
            {
                Debug.Log("[Telewheel] " + text);
            }
            else
            {
                Debug.LogWarning("[Telewheel] " + text);
            }
            WriteReportFile(text);
        }

        private static string CheckPanels()
        {
            var found = new List<string>();
            foreach (BasePanel panel in OpenBrushFacade.TelewheelPanels())
            {
                found.Add(panel.Type.ToString());
            }
            bool hasColor = found.Contains(BasePanel.PanelType.Color.ToString());
            bool hasTools = found.Contains(BasePanel.PanelType.ToolsBasic.ToString())
                || found.Contains(BasePanel.PanelType.ToolsBasicMobile.ToString());
            bool hasAdmin = found.Contains(BasePanel.PanelType.AdminPanel.ToString())
                || found.Contains(BasePanel.PanelType.AdminPanelMobile.ToString());
            string list = string.Join(", ", found.ToArray());
            return hasColor && hasTools && hasAdmin ? list : null;
        }

        private static List<Vector3> TestPath()
        {
            // A small zigzag around the centre of the drawing area, in metres.
            Vector3 c = TwLayout.DrawingCenterMeters;
            return new List<Vector3>
            {
                c + new Vector3(-0.2f, -0.1f, 0f),
                c + new Vector3(-0.1f, 0.1f, 0f),
                c + new Vector3(0f, -0.1f, 0.05f),
                c + new Vector3(0.1f, 0.1f, 0f),
                c + new Vector3(0.2f, -0.1f, 0f),
            };
        }

        /// <summary>
        /// Runs one check. The body returns a detail string or true for a pass, and null or false
        /// for a failure; an exception is a failure and its message is the detail.
        /// </summary>
        private static void Check(TwSelfTestReport report, string name, Func<object> body, string passDetail)
        {
            var check = new TwCheck { Name = name };
            try
            {
                object result = body();
                check.Passed = result != null && !(result is bool && !(bool)result);
                check.Detail = result is string ? (string)result : passDetail;
            }
            catch (Exception e)
            {
                check.Passed = false;
                check.Detail = e.GetType().Name + ": " + e.Message;
            }
            report.Checks.Add(check);
        }

        private static void Check(TwSelfTestReport report, string name, Func<bool> body, string passDetail)
        {
            Check(report, name, () => (object)body(), passDetail);
        }

        private static void WriteReportFile(string text)
        {
            try
            {
                string path = Path.Combine(Application.persistentDataPath, "telewheel-selftest.txt");
                File.WriteAllText(path, text);
                Debug.Log("[Telewheel] Self-test report written to " + path);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[Telewheel] Could not write the self-test report: " + e.Message);
            }
        }
    }
}
