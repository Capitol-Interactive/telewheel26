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
using System.Text;
using TiltBrush;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Telewheel
{
    /// <summary>
    /// Runs Telewheel inside Open Brush. It waits until the app is ready, puts Open Brush into
    /// game mode (trimmed panels, one brush, repurposed buttons), runs the self-test, and then
    /// drives the game.
    /// </summary>
    public sealed class TwDirector : MonoBehaviour
    {
        private const float ButtonSweepSeconds = 1f;

        public static TwDirector Instance { get; private set; }

        private bool m_SawNonStandardState;
        private bool m_Started;
        private float m_SweepTimer;
        private readonly List<string> m_Log = new List<string>();
        private readonly TwSelfTestReport m_SelfTest = new TwSelfTestReport();
        private bool m_SelfTestRunning;
        private TwDebugOverlay m_Overlay;

        public TwSketchService Sketch { get; private set; }

        public bool Started
        {
            get { return m_Started; }
        }

        public TwSelfTestReport SelfTest
        {
            get { return m_SelfTest; }
        }

        private void Awake()
        {
            Instance = this;
            Sketch = new TwSketchService();
            // Must be in place before Open Brush's own Start(), which runs after this.
            OpenBrushFacade.PrepareConfig();
            m_SawNonStandardState = !OpenBrushFacade.IsStandardState;
            OpenBrushFacade.SubscribeStateChanged(OnAppStateChanged);
            TwHooks.CommandHandler = HandleCommand;
            TwHooks.CommandAvailability = CommandAvailability;
            Log("Telewheel created; waiting for Open Brush to finish loading.");
        }

        private void OnDestroy()
        {
            OpenBrushFacade.UnsubscribeStateChanged(OnAppStateChanged);
            TwHooks.Reset();
            if (Instance == this)
            {
                Instance = null;
            }
        }

        private void OnAppStateChanged(App.AppState oldState, App.AppState newState)
        {
            if (newState != App.AppState.Standard)
            {
                m_SawNonStandardState = true;
            }
        }

        private void Update()
        {
            if (!m_Started)
            {
                // Open Brush reports Standard before it has started loading, so only go once it
                // has left that state and come back.
                if (m_SawNonStandardState && OpenBrushFacade.IsReadyToPlay)
                {
                    StartGame();
                }
                return;
            }

            PollDebugKeys();

            m_SweepTimer += Time.unscaledDeltaTime;
            if (m_SweepTimer >= ButtonSweepSeconds)
            {
                m_SweepTimer = 0f;
                // Open Brush re-shows some buttons when its panels refresh, so keep hiding them.
                TwModeAdapter.HideUnusedButtons(null);
            }
        }

        private void StartGame()
        {
            m_Started = true;
            Log("Open Brush is ready; entering Telewheel mode.");
            OpenBrushFacade.EnterGameMode();
            OpenBrushFacade.SetDrawingAllowed(false);
            var hidden = new List<string>();
            TwModeAdapter.HideUnusedButtons(hidden);
            foreach (string line in hidden)
            {
                Log(line);
            }

            UserConfig.TelewheelConfig settings = OpenBrushFacade.Settings;
            if (settings.DebugOverlay)
            {
                m_Overlay = gameObject.AddComponent<TwDebugOverlay>();
            }
            if (settings.SelfTestOnStart)
            {
                RunSelfTest();
            }
        }

        public void RunSelfTest()
        {
            if (m_SelfTestRunning)
            {
                return;
            }
            m_SelfTestRunning = true;
            StartCoroutine(RunSelfTestRoutine());
        }

        private System.Collections.IEnumerator RunSelfTestRoutine()
        {
            yield return TwSelfTest.Run(m_SelfTest);
            m_SelfTestRunning = false;
        }

        private void PollDebugKeys()
        {
            Keyboard keyboard = Keyboard.current;
            if (keyboard == null)
            {
                return;
            }
            if (keyboard.f9Key.wasPressedThisFrame && m_Overlay != null)
            {
                m_Overlay.Visible = !m_Overlay.Visible;
            }
            if (keyboard.f8Key.wasPressedThisFrame)
            {
                RunSelfTest();
            }
        }

        // ----- Repurposed Open Brush buttons -----

        private bool HandleCommand(SketchControlsScript.GlobalCommands command)
        {
            switch (command)
            {
                case SketchControlsScript.GlobalCommands.Sketchbook:
                    Log("Submit pressed (not wired to a match yet).");
                    return true;
                case SketchControlsScript.GlobalCommands.NewSketch:
                    App.Scene.ClearLayerContents(App.Scene.ActiveCanvas);
                    return true;
                case SketchControlsScript.GlobalCommands.ToggleSettings:
                    Log("System menu pressed (not built yet).");
                    return true;
                default:
                    return false;
            }
        }

        private bool? CommandAvailability(SketchControlsScript.GlobalCommands command)
        {
            switch (command)
            {
                case SketchControlsScript.GlobalCommands.Sketchbook:
                case SketchControlsScript.GlobalCommands.ToggleSettings:
                    return true;
                case SketchControlsScript.GlobalCommands.NewSketch:
                    return OpenBrushFacade.ActiveStrokeCount > 0;
                case SketchControlsScript.GlobalCommands.Undo:
                    return OpenBrushFacade.CanUndo;
                case SketchControlsScript.GlobalCommands.Redo:
                    return SketchMemoryScript.m_Instance.CanRedo();
                default:
                    return null;
            }
        }

        // ----- Status for the overlay and the API -----

        public void Log(string message)
        {
            m_Log.Add(message);
            if (m_Log.Count > 40)
            {
                m_Log.RemoveAt(0);
            }
            Debug.Log("[Telewheel] " + message);
        }

        public IEnumerable<string> OverlayLines()
        {
            yield return "state: " + (m_Started ? "running" : "waiting for Open Brush")
                + "   app: " + App.CurrentState
                + "   tool: " + (OpenBrushFacade.IsReadyToPlay ? OpenBrushFacade.ActiveToolType.ToString() : "-");
            foreach (TwCheck check in m_SelfTest.Checks)
            {
                string color = check.Passed ? "#7CFC00" : "#FF5555";
                yield return "<color=" + color + ">" + (check.Passed ? "PASS" : "FAIL") + "</color> "
                    + check.Name + (string.IsNullOrEmpty(check.Detail) ? string.Empty : "  <i>" + check.Detail + "</i>");
            }
            if (m_SelfTestRunning)
            {
                yield return "self-test running...";
            }
            else if (m_SelfTest.Finished)
            {
                yield return m_SelfTest.FailedCount == 0 ? "self-test: all passed" : "self-test: " + m_SelfTest.FailedCount + " failed";
            }
            int start = Mathf.Max(0, m_Log.Count - 8);
            for (int i = start; i < m_Log.Count; i++)
            {
                yield return "<size=12>" + m_Log[i] + "</size>";
            }
        }

        public string StateText()
        {
            var sb = new StringBuilder();
            sb.Append("started=").Append(m_Started);
            sb.Append(" appState=").Append(App.CurrentState);
            sb.Append(" selfTest=").Append(m_SelfTest.Finished ? (m_SelfTest.FailedCount == 0 ? "passed" : "failed") : "notRun");
            return sb.ToString();
        }
    }
}
