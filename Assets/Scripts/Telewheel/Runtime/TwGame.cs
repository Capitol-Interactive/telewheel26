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
using TiltBrush;
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// Runs a Pass &amp; Play match: shows the right screen for each phase of the
    /// <see cref="MatchMachine"/> and turns the player's actions back into machine commands. The
    /// machine decides what happens; this class only puts it on screen and into Open Brush.
    /// </summary>
    public sealed class TwGame
    {
        private const float DrawingSizeMeters = 0.9f;

        private readonly TwDirector m_Director;
        private readonly TwSketchService m_Sketch;
        private readonly TwRandom m_UiRandom;
        private readonly TwFloorSquare m_Floor = new TwFloorSquare();

        private MatchMachine m_Machine;
        private MatchSettings m_PendingSettings;
        private TwScreen m_Screen;
        private float m_TimeScale = 1f;
        private int m_PresentShown = -1;
        private bool m_EndingDrawTurn;

        public TwGame(TwDirector director, TwSketchService sketch, int seed)
        {
            m_Director = director;
            m_Sketch = sketch;
            m_UiRandom = new TwRandom(seed ^ 0x5EED);
            m_Floor.SetVisible(false);
        }

        public MatchMachine Machine
        {
            get { return m_Machine; }
        }

        public TwScreen Screen
        {
            get { return m_Screen; }
        }

        /// <summary>True while it is somebody's turn to draw and Open Brush's tools are live.</summary>
        public bool IsDrawTurn
        {
            get
            {
                return m_Machine != null && m_Machine.Phase == MatchPhase.Turn
                    && m_Machine.CurrentStage.Kind == StageKind.Draw && !m_EndingDrawTurn;
            }
        }

        public string PhaseText
        {
            get
            {
                if (m_Machine == null)
                {
                    return m_Screen == null ? "none" : "menu";
                }
                return m_Machine.Phase.ToString() + (m_Machine.HasStage ? " " + m_Machine.CurrentStage : string.Empty);
            }
        }

        public void SetTimeScale(float scale)
        {
            m_TimeScale = Mathf.Max(0.1f, scale);
        }

        // ----- Menus -----

        public void ShowMainMenu()
        {
            AbandonMatch();
            Prepare(neutral: true);
            SetScreen(new TwMainMenuScreen(ShowSetup, () => m_Director.Log("Settings screen is not built yet.")));
        }

        public void ShowSetup()
        {
            Prepare(neutral: true);
            MatchSettings settings = m_PendingSettings ?? DefaultSettings();
            m_PendingSettings = settings;
            SetScreen(new TwSetupScreen(settings, StartMatch, ShowMainMenu));
        }

        /// <summary>The rules to start from, taken from the config (--Telewheel.Players and friends).</summary>
        public static MatchSettings DefaultSettings()
        {
            UserConfig.TelewheelConfig config = OpenBrushFacade.Settings;
            return new MatchSettings
            {
                PlayerCount = Mathf.Clamp(config.Players, MatchSettings.MinPlayers, MatchSettings.MaxPlayers),
                Rounds = Mathf.Clamp(config.Rounds, MatchSettings.MinRounds, MatchSettings.MaxRounds),
                Seed = config.Seed != 0 ? config.Seed : System.Environment.TickCount,
            };
        }

        public void StartMatch(MatchSettings settings)
        {
            AbandonMatch();
            List<string> words = TwWords.Load(settings.Filter);
            if (words.Count < settings.WheelSegments)
            {
                m_Director.Log("The " + settings.Filter + " word list has only " + words.Count
                    + " words; using the family list.");
                words = TwWords.Load(ContentFilter.Family);
            }
            if (words.Count < settings.WheelSegments)
            {
                m_Director.Log("Not enough words to start a match.");
                ShowMainMenu();
                return;
            }
            m_PendingSettings = settings;
            m_PresentShown = -1;
            m_Machine = new MatchMachine(settings, new WordDeck(words, settings.Seed));
            m_Machine.PhaseChanged += OnPhaseChanged;
            m_Machine.TurnExpired += OnTurnExpired;
            m_Director.Log("Match started: " + settings.PlayerCount + " players, " + settings.Rounds
                + " rounds, " + settings.Filter + " words, seed " + settings.Seed + ".");
            m_Machine.Start();
        }

        /// <summary>Leaves the match (Exit Match) and returns to the main menu.</summary>
        public void ExitMatch()
        {
            ShowMainMenu();
        }

        private void AbandonMatch()
        {
            if (m_Machine == null)
            {
                return;
            }
            m_Machine.PhaseChanged -= OnPhaseChanged;
            m_Machine.TurnExpired -= OnTurnExpired;
            m_Machine = null;
            m_EndingDrawTurn = false;
        }

        // ----- Per frame -----

        public void Update(float dt)
        {
            float scaled = dt * m_TimeScale;
            if (m_Machine != null)
            {
                m_Machine.Tick(scaled);
                if (m_Machine != null && m_Machine.Phase == MatchPhase.Present
                    && m_Machine.PresentIndex != m_PresentShown)
                {
                    ShowPresentItem();
                }
            }
            if (m_Screen != null)
            {
                m_Screen.Tick(scaled);
            }
        }

        public void Dispose()
        {
            AbandonMatch();
            DisposeScreen();
            m_Floor.Destroy();
        }

        // ----- Buttons that Open Brush's own panels are repurposed for -----

        /// <summary>The hand-menu Submit button (the old Sketchbook button).</summary>
        public void Submit()
        {
            if (IsDrawTurn)
            {
                EndDrawTurn();
            }
        }

        /// <summary>The hand-menu Clear Sketch button: wipes the drawing, undoably.</summary>
        public void ClearDrawing()
        {
            if (IsDrawTurn && OpenBrushFacade.ActiveStrokeCount > 0)
            {
                App.Scene.ClearLayerContents(App.Scene.ActiveCanvas);
            }
        }

        // ----- Machine events -----

        private void OnPhaseChanged(MatchPhase from, MatchPhase to)
        {
            switch (to)
            {
                case MatchPhase.Handoff:
                    Prepare(neutral: true);
                    ShowHandoff();
                    break;
                case MatchPhase.Spin:
                    Prepare(neutral: true);
                    SetScreen(new TwSpinScreen(m_Machine, m_UiRandom, OnWheelStopped, OnSpinConfirmed));
                    break;
                case MatchPhase.Countdown:
                    BeginDrawTurn();
                    break;
                case MatchPhase.Turn:
                    if (m_Machine.CurrentStage.Kind == StageKind.Draw)
                    {
                        if (from != MatchPhase.Countdown)
                        {
                            BeginDrawTurn();
                        }
                        StartDrawing();
                    }
                    else
                    {
                        BeginGuessTurn();
                    }
                    break;
                case MatchPhase.Present:
                    m_PresentShown = -1;
                    Prepare(neutral: true);
                    ShowPresentItem();
                    break;
                case MatchPhase.Vote:
                    Prepare(neutral: true);
                    SetScreen(new TwVoteScreen(m_Machine, vote => m_Machine.CastVote(0, vote)));
                    break;
                case MatchPhase.VoteResult:
                    Prepare(neutral: true);
                    SetScreen(new TwVoteResultScreen(m_Machine, m_Machine.ContinueAfterVote));
                    break;
                case MatchPhase.RoundEnd:
                    Prepare(neutral: true);
                    SetScreen(new TwRoundEndScreen(m_Machine, m_Machine.ContinueRound));
                    break;
                case MatchPhase.GameEnd:
                    Prepare(neutral: true);
                    SetScreen(new TwGameEndScreen(m_Machine, PlayAgainSame, PlayAgainNew, ShowMainMenu));
                    break;
            }
        }

        private void OnTurnExpired()
        {
            if (m_Machine == null)
            {
                return;
            }
            if (m_Machine.CurrentStage.Kind == StageKind.Draw)
            {
                EndDrawTurn();
            }
            else
            {
                var guess = m_Screen as TwGuessScreen;
                m_Machine.SubmitGuess(guess == null ? string.Empty : guess.TypedText);
            }
        }

        // ----- Screens for each phase -----

        private void ShowHandoff()
        {
            int next = m_Machine.ActivePlayer;
            string round = TwCopy.RoundLabel(m_Machine.Round, m_Machine.Settings.Rounds);
            SetScreen(new TwHandoffScreen(m_Machine.Settings.NameOf(next), round, m_Machine.ConfirmHandoff));
        }

        private void OnWheelStopped(int segment)
        {
            if (m_Machine == null || m_Machine.Phase != MatchPhase.Spin)
            {
                return;
            }
            m_Machine.CompleteSpin(segment);
            var spin = m_Screen as TwSpinScreen;
            if (spin != null)
            {
                spin.ShowWord(m_Machine.SpinWord);
            }
        }

        private void OnSpinConfirmed()
        {
            if (m_Machine != null && m_Machine.Phase == MatchPhase.Spin && m_Machine.SpinWord != null)
            {
                m_Machine.ConfirmSpin();
            }
        }

        private void BeginDrawTurn()
        {
            Prepare(neutral: true);
            OpenBrushFacade.ResetWorldPose();
            OpenBrushFacade.SelectMarkerBrush();
            OpenBrushFacade.SetBrushColor(TwGfx.ToColor(TwTokens.Deep));
            m_Floor.PlaceAhead(OpenBrushFacade.Head);
            m_Floor.SetVisible(true);
            SetScreen(new TwDrawScreen(m_Machine, Submit));
        }

        private void StartDrawing()
        {
            m_EndingDrawTurn = false;
            OpenBrushFacade.SetDrawingAllowed(true);
            OpenBrushFacade.SetPanelsVisible(true);
        }

        private void EndDrawTurn()
        {
            if (m_EndingDrawTurn || m_Machine == null)
            {
                return;
            }
            m_EndingDrawTurn = true;
            m_Director.StartCoroutine(EndDrawTurnRoutine());
        }

        private System.Collections.IEnumerator EndDrawTurnRoutine()
        {
            // Stop painting, let any stroke in progress finish, then save the drawing.
            OpenBrushFacade.SetDrawingAllowed(false);
            OpenBrushFacade.EatPaintInput();
            float waited = 0f;
            while (OpenBrushFacade.IsStrokeInProgress && waited < 1f)
            {
                waited += Time.unscaledDeltaTime;
                yield return null;
            }
            if (m_Machine != null && m_Machine.Phase == MatchPhase.Turn)
            {
                byte[] drawing = m_Sketch.Capture();
                m_Director.Log("Drawing saved: " + drawing.Length + " bytes.");
                m_Machine.SubmitDrawing(drawing);
            }
            m_EndingDrawTurn = false;
        }

        private void BeginGuessTurn()
        {
            Prepare(neutral: true);
            byte[] drawing = m_Machine.PromptDrawing;
            Vector3 center = TwUi.PointInFront(2.0f, 0.15f);
            int shown = m_Sketch.Show(drawing, center, 1.0f);
            var guess = new TwGuessScreen(m_Machine, OnGuessSubmitted);
            if (shown == 0)
            {
                guess.ShowBlank();
            }
            SetScreen(guess);
        }

        private void OnGuessSubmitted(string text)
        {
            if (m_Machine != null && m_Machine.Phase == MatchPhase.Turn
                && m_Machine.CurrentStage.Kind == StageKind.Guess)
            {
                m_Machine.SubmitGuess(text);
            }
        }

        private void ShowPresentItem()
        {
            if (m_Machine == null || m_Machine.Phase != MatchPhase.Present)
            {
                return;
            }
            m_PresentShown = m_Machine.PresentIndex;
            PresentItem item = m_Machine.CurrentPresentItem;
            m_Sketch.Clear();
            if (item.Kind == PresentItemKind.Drawing)
            {
                m_Sketch.Show(item.Drawing, TwUi.PointInFront(1.9f, 0.1f), DrawingSizeMeters);
            }
            SetScreen(new TwPresentScreen(m_Machine, m_Machine.SkipPresent));
        }

        private void PlayAgainSame()
        {
            if (m_Machine == null)
            {
                return;
            }
            m_Machine.Reset();
            m_Machine.Start();
        }

        private void PlayAgainNew()
        {
            AbandonMatch();
            ShowSetup();
        }

        // ----- Plumbing -----

        /// <summary>
        /// Gets Open Brush out of the way between turns: no drawing, no hand menu, no shown
        /// drawing, no floor square.
        /// </summary>
        private void Prepare(bool neutral)
        {
            if (!neutral)
            {
                return;
            }
            m_EndingDrawTurn = false;
            m_Sketch.Clear();
            OpenBrushFacade.SetDrawingAllowed(false);
            OpenBrushFacade.SetPanelsVisible(false);
            m_Floor.SetVisible(false);
        }

        private void SetScreen(TwScreen screen)
        {
            DisposeScreen();
            m_Screen = screen;
        }

        private void DisposeScreen()
        {
            if (m_Screen != null)
            {
                m_Screen.Dispose();
                m_Screen = null;
            }
        }
    }
}
