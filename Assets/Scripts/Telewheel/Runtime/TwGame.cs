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
using TiltBrush;
using UnityEngine;
using UnityEngine.InputSystem;

namespace Telewheel
{
    /// <summary>
    /// Runs a match, Pass &amp; Play or online: shows the right screen for each phase of the
    /// <see cref="IMatchSession"/> and turns the player's actions back into session commands. The
    /// session (the Pass &amp; Play machine, or the online client with a host somewhere) decides what
    /// happens; this class only puts it on screen and into Open Brush.
    /// </summary>
    public sealed class TwGame
    {
        private const float DrawingSizeMeters = 0.9f;
        private const string PracticeCode = PracticeRoom.PracticeCode;

        private readonly TwDirector m_Director;
        private readonly TwSketchService m_Sketch;
        private readonly TwPointer m_Pointer;
        private readonly TwRandom m_UiRandom;
        private readonly TwFloorSquare m_Floor = new TwFloorSquare();
        private readonly TwPracticeBackend m_Practice;
        private readonly Queue<Action> m_Deferred = new Queue<Action>();

        private IMatchSession m_Session;
        private OnlineSession m_Online;
        private MatchSettings m_PendingSettings;
        private TwScreen m_Screen;
        private TwScreen m_Overlay;
        private TwToastScreen m_Toast;
        private byte[] m_LastDrawing;
        private float m_TimeScale = 1f;
        private int m_PresentShown = -1;
        private int m_LastWholeSecond = -1;
        private int m_ConnectToken;
        private bool m_EndingDrawTurn;
        private bool m_TitleConfettiShown;
        private bool m_WaitingShown;
        private bool m_HostEnvironmentApplied;
        private float m_RoomExtrasTimer;

        public TwGame(TwDirector director, TwSketchService sketch, TwPointer pointer, int seed)
        {
            m_Director = director;
            m_Sketch = sketch;
            m_Pointer = pointer;
            m_UiRandom = new TwRandom(seed ^ 0x5EED);
            m_Practice = new TwPracticeBackend(() => m_LastDrawing);
            m_Floor.SetVisible(false);
        }

        /// <summary>The match in progress (or the online room, once it has a client); null on the menus.</summary>
        public IMatchSession Session
        {
            get { return m_Session; }
        }

        /// <summary>The online room, or null when not online.</summary>
        public OnlineSession Online
        {
            get { return m_Online; }
        }

        public bool InMatch
        {
            get { return m_Session != null; }
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
                return m_Session != null && m_Session.Phase == MatchPhase.Turn
                    && m_Session.TurnKind == StageKind.Draw && !m_Session.LocalDone && !m_EndingDrawTurn;
            }
        }

        public string PhaseText
        {
            get
            {
                if (m_Session == null)
                {
                    return m_Screen == null ? "none" : "menu";
                }
                string text = m_Session.Phase.ToString();
                if (m_Session.Phase == MatchPhase.Turn || m_Session.Phase == MatchPhase.Countdown)
                {
                    text += " " + m_Session.TurnKind + " " + (m_Session.TurnIndex + 1);
                }
                return m_Online == null ? text : "online " + text;
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
            SetScreen(new TwMainMenuScreen(ShowSetup, ShowOnlineMenu, ShowSettings));
            if (!m_TitleConfettiShown)
            {
                // Confetti belongs to the title screen and the reveal, and only the first time.
                m_TitleConfettiShown = true;
                TwFx.Confetti(TwUi.PointInFront(1.5f, 0.45f));
            }
        }

        public void ShowSettings()
        {
            Prepare(neutral: true);
            SetScreen(new TwSettingsScreen(ShowMainMenu));
        }

        /// <summary>Opens the system menu on top of whatever is showing, or closes it.</summary>
        public void ToggleSystemMenu()
        {
            if (m_Overlay != null)
            {
                CloseSystemMenu();
                return;
            }
            bool inMatch = m_Session != null || m_Online != null;
            m_Overlay = new TwSystemMenuScreen(
                inMatch, IsDrawTurn, OpenPreferencesFromMenu, TakePhoto, ExitMatchFromMenu, CloseSystemMenu,
                InRealRoom ? VoiceMenuOptions() : null);
        }

        // True in a room over the real network (not the practice room, which has no voice or avatars).
        private bool InRealRoom
        {
            get { return m_Online != null && !m_Online.IsPractice; }
        }

        private static TwVoiceOptions VoiceMenuOptions()
        {
            return new TwVoiceOptions
            {
                MicOn = () => TwPrefs.VoiceEnabled,
                ToggleMic = () => TwPrefs.SetVoiceEnabled(!TwPrefs.VoiceEnabled),
                OthersMuted = () => TwPrefs.MuteOthers,
                ToggleOthers = () => TwPrefs.SetMuteOthers(!TwPrefs.MuteOthers),
            };
        }

        // The other players stand in the same place while everyone draws, so their avatars are hidden
        // for those turns. Avatars also spawn late, and voice joins late, so this repeats.
        private void UpdateRoomExtras(float dt)
        {
            if (!InRealRoom || m_Session == null)
            {
                return;
            }
            m_RoomExtrasTimer -= dt;
            if (m_RoomExtrasTimer > 0f)
            {
                return;
            }
            m_RoomExtrasTimer = 0.5f;
            MatchPhase phase = m_Session.Phase;
            bool working = phase == MatchPhase.Spin || phase == MatchPhase.Countdown || phase == MatchPhase.Turn;
            OpenBrushFacade.SetRemoteAvatarsVisible(!working);
            if (TwPrefs.MuteOthers)
            {
                OpenBrushFacade.MuteOtherPlayers(true);
            }
        }

        private void CloseSystemMenu()
        {
            if (m_Overlay != null)
            {
                m_Overlay.Dispose();
                m_Overlay = null;
            }
        }

        private void OpenPreferencesFromMenu()
        {
            CloseSystemMenu();
            if (m_Session == null && m_Online == null)
            {
                ShowSettings();
            }
            else
            {
                // Mid-match the preferences open over the game rather than replacing it.
                m_Overlay = new TwSettingsScreen(CloseSystemMenu);
            }
        }

        private void TakePhoto()
        {
            CloseSystemMenu();
            if (IsDrawTurn)
            {
                OpenBrushFacade.EnablePhotoTool();
            }
        }

        private void ExitMatchFromMenu()
        {
            CloseSystemMenu();
            ExitMatch();
        }

        public void ShowSetup()
        {
            Prepare(neutral: true);
            MatchSettings settings = m_PendingSettings ?? DefaultSettings();
            if (OpenBrushFacade.Settings.Seed == 0)
            {
                // A fresh seed for each match, so a rematch does not repeat the same words.
                settings.Seed = System.Environment.TickCount;
            }
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

        // ----- Pass & Play -----

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
            var machine = new MatchMachine(settings, new WordDeck(words, settings.Seed));
            AttachSession(new PassAndPlaySession(machine));
            m_Director.Log("Match started: " + settings.PlayerCount + " players, " + settings.Rounds
                + " rounds, " + settings.Filter + " words, seed " + settings.Seed + ".");
            machine.Start();
        }

        // ----- Online -----

        public void ShowOnlineMenu()
        {
            AbandonMatch();
            Prepare(neutral: true);
            ITwOnlineBackend backend = TwOnline.Backend;
            SetScreen(new TwOnlineMenuScreen(new TwOnlineMenuOptions
            {
                Profile = TwPrefs.Profile,
                UnavailableReason = backend.UnavailableReason,
                PracticeVisible = TwOnline.PracticeVisible,
                OnHost = () => HostOnline(backend),
                OnJoin = ShowJoinCode,
                OnProfile = ShowProfile,
                OnPracticeHost = () => HostOnline(m_Practice),
                OnPracticeJoin = () => JoinOnline(m_Practice, PracticeCode),
                OnBack = ShowMainMenu,
            }));
        }

        /// <summary>Hosts a practice room straight away (for unattended runs with --Telewheel.FakeOnline).</summary>
        public void StartPracticeHost()
        {
            HostOnline(m_Practice);
        }

        private void ShowProfile()
        {
            Prepare(neutral: true);
            SetScreen(new TwProfileScreen(TwPrefs.Profile, saved =>
            {
                TwPrefs.SaveProfile(saved);
                ShowOnlineMenu();
            }, ShowOnlineMenu));
        }

        private void ShowJoinCode()
        {
            Prepare(neutral: true);
            SetScreen(new TwJoinCodeScreen(code => JoinOnline(TwOnline.Backend, code), ShowOnlineMenu));
        }

        private void HostOnline(ITwOnlineBackend backend)
        {
            MatchSettings settings = DefaultSettings();
            settings.Seed = System.Environment.TickCount;
            int token = BeginConnecting();
            backend.Host(TwPrefs.Profile, settings,
                session => OnOnlineReady(token, session), message => OnOnlineFailed(token, message));
        }

        private void JoinOnline(ITwOnlineBackend backend, string code)
        {
            int token = BeginConnecting();
            backend.Join(code, TwPrefs.Profile,
                session => OnOnlineReady(token, session), message => OnOnlineFailed(token, message));
        }

        // Shows "Connecting..." with a way out, and returns a token so a late answer to a cancelled try is ignored.
        private int BeginConnecting()
        {
            AbandonMatch();
            Prepare(neutral: true);
            int token = ++m_ConnectToken;
            SetScreen(new TwNoticeScreen("ONLINE", TwCopy.Connecting, TwCopy.Back, () =>
            {
                m_ConnectToken++;
                ShowOnlineMenu();
            }));
            return token;
        }

        private void OnOnlineReady(int token, OnlineSession session)
        {
            if (token != m_ConnectToken)
            {
                session.Leave();
                return;
            }
            m_Online = session;
            OnlineMatchClient client = session.Client;
            AttachSession(client);
            client.StateChanged += OnClientStateChanged;
            client.PlayerLeft += OnPlayerLeft;
            client.EnvironmentChanged += OnEnvironmentChanged;
            m_Director.Log("Online: " + (session.IsHost ? "hosting " : "joining ") + session.JoinCode
                + (session.IsPractice ? " (practice)" : string.Empty) + ".");
            if (client.State == ClientState.Lobby)
            {
                ShowLobby();
            }
        }

        private void OnOnlineFailed(int token, string message)
        {
            if (token != m_ConnectToken)
            {
                return;
            }
            ShowNotice("CAN'T CONNECT", message, ShowOnlineMenu);
        }

        private void ShowLobby()
        {
            Prepare(neutral: true);
            SetScreen(new TwLobbyScreen(
                m_Online, StartOnlineMatch, ExitMatch, TwOnline.PracticeVisible ? (Action)RunLinkTest : null));
        }

        private void StartOnlineMatch()
        {
            if (m_Online == null || !m_Online.IsHost)
            {
                return;
            }
            if (!m_Online.Room.Start())
            {
                ShowToast(m_Online.Room.StartProblem);
            }
        }

        // A developer tool: sends each guest drawings-sized blobs and logs how fast they came back, so
        // the real connection's limits are measured rather than guessed.
        private void RunLinkTest()
        {
            if (m_Online == null || !m_Online.IsHost || m_Online.Room.LinkTestRunning)
            {
                return;
            }
            OnlineRoomHost room = m_Online.Room;
            room.LinkTestChanged -= OnLinkTestChanged;
            room.LinkTestChanged += OnLinkTestChanged;
            room.RunLinkTest(new[] { 5 * 1024, 50 * 1024, 250 * 1024, 500 * 1024 });
            ShowToast(room.LinkTestRunning ? TwCopy.TestingLink : "Nobody else is in the room to test with.");
        }

        private void OnLinkTestChanged()
        {
            if (m_Online == null || m_Online.Room.LinkTestRunning)
            {
                return;
            }
            float slowest = float.MaxValue;
            foreach (LinkTestResult result in m_Online.Room.LinkTestResults)
            {
                m_Director.Log("Link test: " + result.Name + " " + (result.Bytes / 1024) + " KB in "
                    + result.Seconds.ToString("0.00") + " s (" + result.KilobytesPerSecond.ToString("0") + " KB/s)");
                if (result.Bytes >= 250 * 1024 && result.KilobytesPerSecond > 0f)
                {
                    slowest = Mathf.Min(slowest, result.KilobytesPerSecond);
                }
            }
            ShowToast(slowest < float.MaxValue
                ? "Link test done. Slowest big transfer: " + slowest.ToString("0") + " KB/s (see the log)."
                : "Link test done (see the log).");
        }

        private void OnClientStateChanged()
        {
            if (m_Online == null)
            {
                return;
            }
            OnlineMatchClient client = m_Online.Client;
            switch (client.State)
            {
                case ClientState.Lobby:
                    ShowLobby();
                    break;
                case ClientState.Rejected:
                    Defer(() => ShowNotice("CAN'T JOIN", TwCopy.RejectedLine(client.Rejection), ShowOnlineMenu));
                    break;
                case ClientState.Ended:
                    if (client.Phase == MatchPhase.GameEnd)
                    {
                        // The match was over; keep the final scores up and just say the room closed.
                        ShowToast(TwCopy.EndedLine(client.EndedBecause));
                    }
                    else
                    {
                        Defer(() => ShowNotice("MATCH ENDED", TwCopy.EndedLine(client.EndedBecause), ShowOnlineMenu));
                    }
                    break;
            }
        }

        private void OnPlayerLeft(int seat)
        {
            if (m_Online != null)
            {
                ShowToast(TwCopy.PlayerLeftLine(m_Online.Client.NameOf(seat)));
            }
        }

        private void OnEnvironmentChanged(string name)
        {
            // The host's pick, unless this player is in mixed reality (which is personal).
            if (!string.IsNullOrEmpty(name) && !TwPrefs.MixedReality && OpenBrushFacade.ApplyEnvironment(name))
            {
                m_HostEnvironmentApplied = true;
            }
        }

        private void ShowNotice(string title, string message, Action onClose)
        {
            AbandonMatch();
            Prepare(neutral: true);
            SetScreen(new TwNoticeScreen(title, message, TwCopy.Okay, onClose));
        }

        private void ShowToast(string text)
        {
            if (m_Toast != null)
            {
                m_Toast.Dispose();
            }
            m_Toast = new TwToastScreen(text, 4f);
        }

        // Teardown that would pull the session out from under its own callbacks waits for the next frame.
        private void Defer(Action action)
        {
            m_Deferred.Enqueue(action);
        }

        /// <summary>Leaves the match or room (Exit Match) and returns to a menu.</summary>
        public void ExitMatch()
        {
            if (m_Online != null)
            {
                ShowOnlineMenu();
            }
            else
            {
                ShowMainMenu();
            }
        }

        private void AttachSession(IMatchSession session)
        {
            m_Session = session;
            m_WaitingShown = false;
            session.PhaseChanged += OnPhaseChanged;
            session.TurnExpired += OnTurnExpired;
        }

        private void AbandonMatch()
        {
            if (m_Session != null)
            {
                m_Session.PhaseChanged -= OnPhaseChanged;
                m_Session.TurnExpired -= OnTurnExpired;
            }
            if (m_Online != null)
            {
                OnlineMatchClient client = m_Online.Client;
                client.StateChanged -= OnClientStateChanged;
                client.PlayerLeft -= OnPlayerLeft;
                client.EnvironmentChanged -= OnEnvironmentChanged;
                if (m_Online.Room != null)
                {
                    m_Online.Room.LinkTestChanged -= OnLinkTestChanged;
                }
                m_Online.Leave();
                m_Online = null;
                if (m_HostEnvironmentApplied)
                {
                    m_HostEnvironmentApplied = false;
                    TwPrefs.RestoreEnvironment();
                }
            }
            else if (m_Session != null)
            {
                m_Session.Dispose();
            }
            m_Session = null;
            m_EndingDrawTurn = false;
            m_WaitingShown = false;
        }

        // ----- Per frame -----

        public void Update(float dt)
        {
            float scaled = dt * m_TimeScale;
            while (m_Deferred.Count > 0)
            {
                m_Deferred.Dequeue()();
            }
            if (m_Pointer != null)
            {
                // While drawing, the mouse belongs to the brush and the laser stays out of the way.
                m_Pointer.Drawing = IsDrawTurn;
            }
            if (IsDrawTurn && Keyboard.current != null && Keyboard.current.enterKey.wasPressedThisFrame)
            {
                Submit();
            }
            if (m_Online != null)
            {
                m_Online.Tick(scaled);
            }
            else if (m_Session != null)
            {
                m_Session.Tick(scaled);
            }
            if (m_Session != null)
            {
                if (m_Session.Phase == MatchPhase.Present && m_Session.PresentIndex != m_PresentShown)
                {
                    ShowPresentItem();
                }
                PlayClockSounds();
                ShowWaitingWhenDone();
                UpdateRoomExtras(dt);
            }
            if (m_Screen != null)
            {
                m_Screen.Tick(scaled);
            }
            if (m_Overlay != null)
            {
                m_Overlay.Tick(scaled);
            }
            if (m_Toast != null)
            {
                m_Toast.Tick(dt);
                if (m_Toast.Expired)
                {
                    m_Toast.Dispose();
                    m_Toast = null;
                }
            }
        }

        public void Dispose()
        {
            AbandonMatch();
            CloseSystemMenu();
            DisposeScreen();
            if (m_Toast != null)
            {
                m_Toast.Dispose();
                m_Toast = null;
            }
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

        // ----- Session events -----

        private void OnPhaseChanged(MatchPhase from, MatchPhase to)
        {
            m_LastWholeSecond = -1;
            m_WaitingShown = false;
            switch (to)
            {
                case MatchPhase.Handoff:
                    Prepare(neutral: true);
                    ShowHandoff();
                    TwAudio.Play(TwSound.Whoosh, 0.4f);
                    break;
                case MatchPhase.Spin:
                    Prepare(neutral: true);
                    SetScreen(new TwSpinScreen(m_Session, m_UiRandom, OnWheelStopped, OnSpinConfirmed));
                    TwAudio.PlayVoice("vo_spin");
                    break;
                case MatchPhase.Countdown:
                    BeginDrawTurn();
                    TwAudio.PlayVoice("vo_get_ready");
                    break;
                case MatchPhase.Turn:
                    if (m_Session.TurnKind == StageKind.Draw)
                    {
                        if (from != MatchPhase.Countdown)
                        {
                            BeginDrawTurn();
                        }
                        StartDrawing();
                        TwAudio.Play(TwSound.Go, 0.5f);
                    }
                    else
                    {
                        BeginGuessTurn();
                        TwAudio.PlayVoice("vo_guess");
                    }
                    break;
                case MatchPhase.Present:
                    m_PresentShown = -1;
                    Prepare(neutral: true);
                    ShowPresentItem();
                    break;
                case MatchPhase.Vote:
                    Prepare(neutral: true);
                    SetScreen(new TwVoteScreen(m_Session, vote => m_Session.CastVote(vote)));
                    TwAudio.PlayVoice("vo_vote");
                    break;
                case MatchPhase.VoteResult:
                    Prepare(neutral: true);
                    SetScreen(new TwVoteResultScreen(
                        m_Session, m_Session.IsOnline ? null : (Action)m_Session.ContinueAfterVote));
                    if (m_Session.LastVoteLanded)
                    {
                        TwAudio.Play(TwSound.Ding, 0.6f);
                        TwAudio.PlayVoice("vo_nailed");
                        TwFx.Confetti(TwUi.PointInFront(1.6f, 0.35f));
                    }
                    else
                    {
                        TwAudio.Play(TwSound.Buzz, 0.5f);
                        TwAudio.PlayVoice("vo_drifted");
                    }
                    break;
                case MatchPhase.RoundEnd:
                    Prepare(neutral: true);
                    SetScreen(new TwRoundEndScreen(
                        m_Session, m_Session.IsOnline ? null : (Action)m_Session.ContinueRound));
                    TwAudio.Play(TwSound.Fanfare, 0.5f);
                    TwAudio.PlayVoice("vo_round_over");
                    break;
                case MatchPhase.GameEnd:
                    Prepare(neutral: true);
                    SetScreen(m_Session.IsOnline
                        ? new TwGameEndScreen(m_Session, null, null, ExitMatch)
                        : new TwGameEndScreen(m_Session, PlayAgainSame, PlayAgainNew, ShowMainMenu));
                    TwAudio.Play(TwSound.Fanfare, 0.6f);
                    TwAudio.PlayVoice("vo_game_over");
                    TwFx.Confetti(TwUi.PointInFront(1.5f, 0.4f), 140);
                    break;
            }
        }

        // Beeps for the last three seconds of the countdown and ticks for the last five of a turn.
        private void PlayClockSounds()
        {
            TurnClock clock = m_Session.Clock;
            int whole = clock.WholeSeconds;
            if (whole == m_LastWholeSecond)
            {
                return;
            }
            m_LastWholeSecond = whole;
            if (whole <= 0)
            {
                return;
            }
            if (m_Session.Phase == MatchPhase.Countdown && whole <= 3)
            {
                TwAudio.Play(TwSound.Beep, 0.5f);
            }
            else if (m_Session.Phase == MatchPhase.Turn && clock.Warning && whole <= 5)
            {
                TwAudio.Play(TwSound.Tick, 0.5f);
            }
        }

        private void OnTurnExpired()
        {
            if (m_Session == null)
            {
                return;
            }
            if (m_Session.TurnKind == StageKind.Draw)
            {
                EndDrawTurn();
            }
            else
            {
                var guess = m_Screen as TwGuessScreen;
                m_Session.SubmitGuess(guess == null ? string.Empty : guess.TypedText);
            }
        }

        // Online, handing in your part does not change the phase: the room waits for the others.
        private void ShowWaitingWhenDone()
        {
            // Only while there is something to wait for; the result and score screens must stay up.
            bool waitingPhase = m_Session.Phase == MatchPhase.Spin || m_Session.Phase == MatchPhase.Countdown
                || m_Session.Phase == MatchPhase.Turn || m_Session.Phase == MatchPhase.Vote;
            bool done = m_Session.IsOnline && waitingPhase && m_Session.LocalDone && !m_EndingDrawTurn;
            if (!done)
            {
                m_WaitingShown = false;
                return;
            }
            if (m_WaitingShown)
            {
                return;
            }
            m_WaitingShown = true;
            Prepare(neutral: true);
            SetScreen(new TwWaitingScreen(m_Session));
        }

        // ----- Screens for each phase -----

        private void ShowHandoff()
        {
            int next = m_Session.ActiveSeat;
            string round = TwCopy.RoundLabel(m_Session.Round, m_Session.RoundCount);
            SetScreen(new TwHandoffScreen(m_Session.NameOf(next), round, m_Session.ConfirmHandoff));
        }

        private void OnWheelStopped(int segment)
        {
            if (m_Session == null || m_Session.Phase != MatchPhase.Spin)
            {
                return;
            }
            m_Session.CompleteSpin(segment);
            var spin = m_Screen as TwSpinScreen;
            if (spin != null && m_Session.SpinWord != null)
            {
                spin.ShowWord(m_Session.SpinWord);
            }
        }

        private void OnSpinConfirmed()
        {
            if (m_Session != null && m_Session.Phase == MatchPhase.Spin && m_Session.SpinWord != null)
            {
                m_Session.ConfirmSpin();
            }
        }

        private void BeginDrawTurn()
        {
            Prepare(neutral: true);
            OpenBrushFacade.ResetWorldPose();
            OpenBrushFacade.SelectMarkerBrush();
            OpenBrushFacade.SetBrushColor(TwGfx.ToColor(TwTokens.Deep));
            // Choosing a brush switches Open Brush back to its paint tool; lock it again until the countdown ends.
            OpenBrushFacade.SetDrawingAllowed(false);
            m_Floor.PlaceAhead(OpenBrushFacade.Head);
            m_Floor.SetVisible(true);
            SetScreen(new TwDrawScreen(m_Session, Submit));
        }

        private void StartDrawing()
        {
            m_EndingDrawTurn = false;
            OpenBrushFacade.SetDrawingAllowed(true);
            OpenBrushFacade.SetPanelsVisible(true);
        }

        private void EndDrawTurn()
        {
            if (m_EndingDrawTurn || m_Session == null)
            {
                return;
            }
            m_EndingDrawTurn = true;
            m_Director.StartCoroutine(EndDrawTurnRoutine(m_Session, m_Session.TurnIndex));
        }

        private System.Collections.IEnumerator EndDrawTurnRoutine(IMatchSession session, int turn)
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
            // The session may have moved on (the host's clock can run out first), or been left.
            bool stillThisTurn = m_Session == session && session.Phase == MatchPhase.Turn
                && session.TurnKind == StageKind.Draw && session.TurnIndex == turn && !session.LocalDone;
            if (stillThisTurn)
            {
                byte[] drawing = m_Sketch.Capture();
                m_LastDrawing = drawing;
                m_Director.Log("Drawing saved: " + drawing.Length + " bytes.");
                TwFx.Poof(TwUi.PointInFront(1.4f, 0.0f));
                TwAudio.Play(TwSound.Poof, 0.6f);
                // Clear the flag first: online, handing the drawing in does not change the phase.
                m_EndingDrawTurn = false;
                session.SubmitDrawing(drawing);
            }
            m_EndingDrawTurn = false;
        }

        private void BeginGuessTurn()
        {
            Prepare(neutral: true);
            byte[] drawing = m_Session.PromptDrawing;
            Vector3 center = TwUi.PointInFront(2.0f, 0.15f);
            int shown = m_Sketch.Show(drawing, center, 1.0f);
            var guess = new TwGuessScreen(m_Session, OnGuessSubmitted);
            if (shown == 0)
            {
                guess.ShowBlank();
            }
            SetScreen(guess);
        }

        private void OnGuessSubmitted(string text)
        {
            if (m_Session != null && m_Session.Phase == MatchPhase.Turn && m_Session.TurnKind == StageKind.Guess)
            {
                m_Session.SubmitGuess(text);
            }
        }

        private void ShowPresentItem()
        {
            if (m_Session == null || m_Session.Phase != MatchPhase.Present)
            {
                return;
            }
            m_PresentShown = m_Session.PresentIndex;
            TwAudio.Play(TwSound.Pop, 0.5f);
            PresentItem item = m_Session.CurrentPresentItem;
            m_Sketch.Clear();
            if (item.Kind == PresentItemKind.Drawing)
            {
                m_Sketch.Show(item.Drawing, TwUi.PointInFront(1.9f, 0.1f), DrawingSizeMeters);
            }
            SetScreen(new TwPresentScreen(m_Session, m_Session.SkipPresent));
        }

        private void PlayAgainSame()
        {
            var pass = m_Session as PassAndPlaySession;
            if (pass == null)
            {
                return;
            }
            pass.Machine.Reset();
            pass.Machine.Start();
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
            // Every screen but the menus gets a MENU button, since the hand menu is hidden between turns.
            if (screen != null && !IsMenuScreen(screen))
            {
                screen.AddMenuButton(ToggleSystemMenu);
            }
        }

        private static bool IsMenuScreen(TwScreen screen)
        {
            return screen is TwMainMenuScreen || screen is TwSetupScreen || screen is TwSettingsScreen
                || screen is TwOnlineMenuScreen || screen is TwProfileScreen || screen is TwJoinCodeScreen
                || screen is TwLobbyScreen || screen is TwNoticeScreen;
        }

        public bool SystemMenuOpen
        {
            get { return m_Overlay != null; }
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
