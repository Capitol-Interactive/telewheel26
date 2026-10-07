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

namespace Telewheel
{
    public enum ClientState
    {
        /// <summary>Hello sent; waiting to be welcomed into the room.</summary>
        Connecting,

        Lobby,
        Playing,

        /// <summary>The host turned this player away (see <see cref="OnlineMatchClient.Rejection"/>).</summary>
        Rejected,

        /// <summary>The match was ended or the host is gone (see <see cref="OnlineMatchClient.EndedBecause"/>).</summary>
        Ended,
    }

    /// <summary>
    /// One player's side of an online match. It sends what the player does to the host and turns the
    /// host's messages into the state the screens read (<see cref="IMatchView"/>). The host decides
    /// everything; this only mirrors it and runs the local clocks for display.
    /// </summary>
    public sealed class OnlineMatchClient : IMatchSession
    {
        private static readonly int[] NobodyWaiting = new int[0];

        private readonly INetClientPort m_Port;
        private readonly PlayerProfile m_Profile;
        private readonly TurnClock m_Clock = new TurnClock();
        private readonly List<string> m_Names = new List<string>();
        private readonly List<int> m_Icons = new List<int>();
        private readonly HashSet<int> m_Gone = new HashSet<int>();

        private int m_LobbyVersion;
        private ClientState m_State = ClientState.Connecting;
        private RejectReason m_Rejection;
        private EndReason m_EndedBecause;
        private int m_LocalSeat = -1;
        private int m_Rounds = 1;
        private ContentFilter m_Filter = ContentFilter.Family;
        private string m_Environment = string.Empty;

        private MatchPhase m_Phase = MatchPhase.Lobby;
        private ChainPlanner m_Planner;
        private int m_Round;
        private string[] m_WheelWords = new string[0];
        private string m_SpinWord;
        private int m_Turn;
        private StageKind m_TurnKind = StageKind.Draw;
        private float m_TurnSeconds;
        private string m_Prompt;
        private byte[] m_PromptDrawing;
        private bool m_TurnTimedOut;
        private int m_PresentChain;
        private int m_PresentIndex;
        private PresentItem m_PresentItem;
        private string m_PresentedWord = string.Empty;
        private bool m_LastLanded;
        private int[] m_Scores = new int[0];
        private bool m_LocalDone;
        private int m_DoneMask;

        public OnlineMatchClient(INetClientPort port, PlayerProfile profile)
        {
            m_Port = port;
            m_Profile = profile ?? new PlayerProfile();
            m_Port.FromHost += OnHostMessage;
            m_Port.HostLost += OnHostLost;
        }

        // ----- Room events -----

        /// <summary>The roster, rules or this player's seat changed (lobby).</summary>
        public event Action LobbyChanged;

        /// <summary>The connection state changed: welcomed, rejected, ended.</summary>
        public event Action StateChanged;

        /// <summary>A player left mid-match (their seat).</summary>
        public event Action<int> PlayerLeft;

        /// <summary>The host picked an environment (its id).</summary>
        public event Action<string> EnvironmentChanged;

        public event Action<MatchPhase, MatchPhase> PhaseChanged;

        public event Action TurnExpired;

        // ----- Room state -----

        public ClientState State
        {
            get { return m_State; }
        }

        /// <summary>Goes up every time the roster or rules change, so a screen can tell it is out of date.</summary>
        public int LobbyVersion
        {
            get { return m_LobbyVersion; }
        }

        public RejectReason Rejection
        {
            get { return m_Rejection; }
        }

        public EndReason EndedBecause
        {
            get { return m_EndedBecause; }
        }

        /// <summary>This player's seat; -1 until the host has welcomed them.</summary>
        public int LocalSeat
        {
            get { return m_LocalSeat; }
        }

        public bool IsHost
        {
            get { return m_LocalSeat == 0; }
        }

        public IReadOnlyList<string> Names
        {
            get { return m_Names; }
        }

        public IReadOnlyList<int> Icons
        {
            get { return m_Icons; }
        }

        public ContentFilter Filter
        {
            get { return m_Filter; }
        }

        public string Environment
        {
            get { return m_Environment; }
        }

        public bool IsGone(int seat)
        {
            return m_Gone.Contains(seat);
        }

        /// <summary>Introduces this player to the room. Call once the connection is up.</summary>
        public void Join()
        {
            m_Port.SendToHost(NetMessage.Hello(m_Profile.Name, m_Profile.Icon, OnlineRoomHost.ProtocolVersion));
        }

        /// <summary>Tells the host about a new name or icon while still in the lobby.</summary>
        public void UpdateProfile(PlayerProfile profile)
        {
            if (profile != null && m_State == ClientState.Lobby)
            {
                m_Profile.Name = profile.Name;
                m_Profile.Icon = profile.Icon;
                Join();
            }
        }

        public void Dispose()
        {
            m_Port.FromHost -= OnHostMessage;
            m_Port.HostLost -= OnHostLost;
        }

        // ----- IMatchView -----

        public MatchPhase Phase
        {
            get { return m_Phase; }
        }

        public int Round
        {
            get { return m_Round; }
        }

        public int RoundCount
        {
            get { return m_Rounds; }
        }

        public bool IsFinalRound
        {
            get { return m_Round >= m_Rounds; }
        }

        public int PlayerCount
        {
            get { return m_Names.Count; }
        }

        public int WheelSegments
        {
            get { return m_WheelWords.Length > 0 ? m_WheelWords.Length : new MatchSettings().WheelSegments; }
        }

        public int ActiveSeat
        {
            get { return m_LocalSeat; }
        }

        public string NameOf(int seat)
        {
            return seat >= 0 && seat < m_Names.Count ? m_Names[seat] : TwCopy.DefaultPlayerName(seat);
        }

        public int IconOf(int seat)
        {
            return seat >= 0 && seat < m_Icons.Count ? m_Icons[seat] : 0;
        }

        public ChainPlanner Planner
        {
            get { return m_Planner; }
        }

        public TurnClock Clock
        {
            get { return m_Clock; }
        }

        public int TurnIndex
        {
            get { return m_Turn; }
        }

        public StageKind TurnKind
        {
            get { return m_TurnKind; }
        }

        public string SpinWord
        {
            get { return m_SpinWord; }
        }

        /// <summary>The words on this player's wheel, for the current spin.</summary>
        public IReadOnlyList<string> WheelWords
        {
            get { return m_WheelWords; }
        }

        public string PromptText
        {
            get { return InTurnPhase && m_TurnKind == StageKind.Draw ? m_Prompt : null; }
        }

        public byte[] PromptDrawing
        {
            get { return InTurnPhase && m_TurnKind == StageKind.Guess ? m_PromptDrawing : null; }
        }

        public int PresentChain
        {
            get { return m_PresentChain; }
        }

        /// <summary>Chain i belongs to seat i.</summary>
        public int PresentOwner
        {
            get { return m_PresentChain; }
        }

        public int PresentIndex
        {
            get { return m_PresentIndex; }
        }

        public PresentItem CurrentPresentItem
        {
            get { return m_Phase == MatchPhase.Present || m_Phase == MatchPhase.Vote ? m_PresentItem : null; }
        }

        public string PresentedWord
        {
            get { return m_PresentedWord; }
        }

        public bool CanAdvancePresent
        {
            get { return m_Phase == MatchPhase.Present && m_PresentChain == m_LocalSeat; }
        }

        public bool LastVoteLanded
        {
            get { return m_LastLanded; }
        }

        public IReadOnlyList<int> Scores
        {
            get { return m_Scores; }
        }

        public IList<int> Leaders
        {
            get { return ScoreMath.Leaders(m_Scores); }
        }

        public bool IsOnline
        {
            get { return true; }
        }

        public bool LocalDone
        {
            get { return m_LocalDone; }
        }

        public IReadOnlyList<int> WaitingFor
        {
            get
            {
                bool waiting = m_Phase == MatchPhase.Spin || m_Phase == MatchPhase.Countdown
                    || m_Phase == MatchPhase.Turn || m_Phase == MatchPhase.Vote;
                if (!waiting)
                {
                    return NobodyWaiting;
                }
                var seats = new List<int>();
                for (int seat = 0; seat < m_Names.Count; seat++)
                {
                    if (!m_Gone.Contains(seat) && !IsDone(seat))
                    {
                        seats.Add(seat);
                    }
                }
                return seats;
            }
        }

        // ----- Time -----

        public void Tick(float dt)
        {
            if (!m_Clock.Tick(dt))
            {
                return;
            }
            if (m_Phase == MatchPhase.Countdown)
            {
                m_Clock.Start(m_TurnSeconds);
                EnterPhase(MatchPhase.Turn);
            }
            else if (m_Phase == MatchPhase.Turn && !m_LocalDone)
            {
                m_TurnTimedOut = true;
                Action handler = TurnExpired;
                if (handler != null)
                {
                    handler();
                }
            }
        }

        // ----- What the player does -----

        public void ConfirmHandoff()
        {
            // Nobody hands a headset over online.
        }

        public void SkipCountdown()
        {
            // The host keeps the clock, and everyone's countdown runs together.
        }

        public void CompleteSpin(int segment)
        {
            if (m_Phase != MatchPhase.Spin || m_SpinWord != null || m_WheelWords.Length == 0)
            {
                return;
            }
            int index = Math.Max(0, Math.Min(m_WheelWords.Length - 1, segment));
            m_SpinWord = m_WheelWords[index];
            m_Port.SendToHost(NetMessage.SpinResult(index));
        }

        public void ConfirmSpin()
        {
            if (m_Phase != MatchPhase.Spin || m_SpinWord == null || m_LocalDone)
            {
                return;
            }
            MarkLocalDone();
            m_Port.SendToHost(NetMessage.Ready());
        }

        public void SubmitDrawing(byte[] drawing)
        {
            if (m_Phase != MatchPhase.Turn || m_TurnKind != StageKind.Draw || m_LocalDone)
            {
                return;
            }
            MarkLocalDone();
            m_Port.SendToHost(NetMessage.SubmitDrawing(drawing ?? new byte[0]));
        }

        public bool SubmitGuess(string text)
        {
            if (m_Phase != MatchPhase.Turn || m_TurnKind != StageKind.Guess || m_LocalDone)
            {
                return false;
            }
            string guess = GuessNormalizer.Normalize(text);
            if (guess.Length == 0)
            {
                if (!m_TurnTimedOut)
                {
                    return false;
                }
                guess = TwCopy.NoGuess;
            }
            MarkLocalDone();
            m_Port.SendToHost(NetMessage.SubmitGuess(guess));
            return true;
        }

        public void SkipPresent()
        {
            if (CanAdvancePresent)
            {
                m_Port.SendToHost(NetMessage.Advance());
            }
        }

        public void CastVote(bool yes)
        {
            if (m_Phase != MatchPhase.Vote || m_LocalDone)
            {
                return;
            }
            MarkLocalDone();
            m_Port.SendToHost(NetMessage.Vote(yes));
        }

        public void ContinueAfterVote()
        {
            // The host moves on by itself.
        }

        public void ContinueRound()
        {
            // The host moves on by itself.
        }

        // ----- What the host says -----

        private void OnHostMessage(NetMessage message)
        {
            if (message == null || m_State == ClientState.Rejected || m_State == ClientState.Ended)
            {
                return;
            }
            switch (message.Kind)
            {
                case NetKind.LobbyState:
                    ApplyLobby(message);
                    break;
                case NetKind.Rejected:
                    m_Rejection = (RejectReason)message.A;
                    SetState(ClientState.Rejected);
                    break;
                case NetKind.Environment:
                    m_Environment = message.Text ?? string.Empty;
                    RaiseEnvironment();
                    break;
                case NetKind.PlayerLeft:
                    m_Gone.Add(message.A);
                    RaisePlayerLeft(message.A);
                    break;
                case NetKind.Progress:
                    m_DoneMask = message.A;
                    break;
                case NetKind.MatchEnded:
                    End((EndReason)message.A);
                    break;
                case NetKind.PhaseChanged:
                    if (message.B > 0)
                    {
                        m_Round = message.B;
                    }
                    break;
                case NetKind.WheelWords:
                    OnWheelWords(message);
                    break;
                case NetKind.TurnStart:
                    OnTurnStart(message);
                    break;
                case NetKind.PresentItem:
                    OnPresentItem(message);
                    break;
                case NetKind.VoteOpen:
                    OnVoteOpen(message);
                    break;
                case NetKind.VoteResult:
                    m_PresentChain = message.A;
                    m_LastLanded = message.B != 0;
                    SetScores(message.Numbers);
                    m_Clock.Stop();
                    EnterPhase(MatchPhase.VoteResult);
                    break;
                case NetKind.RoundEnd:
                    m_Round = message.A;
                    SetScores(message.Numbers);
                    m_Clock.Stop();
                    EnterPhase(MatchPhase.RoundEnd);
                    break;
                case NetKind.GameEnd:
                    SetScores(message.Numbers);
                    m_Clock.Stop();
                    EnterPhase(MatchPhase.GameEnd);
                    break;
            }
        }

        private void OnHostLost()
        {
            End(EndReason.HostEnded);
        }

        private void End(EndReason reason)
        {
            if (m_State == ClientState.Ended)
            {
                return;
            }
            m_EndedBecause = reason;
            m_Clock.Stop();
            SetState(ClientState.Ended);
        }

        private void ApplyLobby(NetMessage message)
        {
            if (m_State == ClientState.Playing)
            {
                return;
            }
            m_LobbyVersion++;
            m_LocalSeat = message.A;
            m_Rounds = Math.Max(MatchSettings.MinRounds, Math.Min(MatchSettings.MaxRounds, message.B));
            m_Filter = (ContentFilter)message.C;
            m_Names.Clear();
            m_Icons.Clear();
            if (message.Words != null)
            {
                for (int i = 0; i < message.Words.Length; i++)
                {
                    m_Names.Add(message.Words[i] ?? TwCopy.DefaultPlayerName(i));
                    int icon = message.Numbers != null && i < message.Numbers.Length ? message.Numbers[i] : 0;
                    m_Icons.Add(PlayerProfile.CleanIcon(icon));
                }
            }
            string environment = message.Text ?? string.Empty;
            bool environmentChanged = environment != m_Environment;
            m_Environment = environment;
            bool firstWelcome = m_State == ClientState.Connecting;
            if (firstWelcome)
            {
                SetState(ClientState.Lobby);
            }
            Action handler = LobbyChanged;
            if (handler != null)
            {
                handler();
            }
            if (environmentChanged)
            {
                RaiseEnvironment();
            }
        }

        private void OnWheelWords(NetMessage message)
        {
            StartPlaying();
            m_WheelWords = message.Words ?? new string[0];
            m_SpinWord = null;
            ResetStep();
            m_Clock.Stop();
            EnterPhase(MatchPhase.Spin);
        }

        private void OnTurnStart(NetMessage message)
        {
            StartPlaying();
            m_Turn = message.A;
            m_TurnKind = (StageKind)message.B;
            m_TurnSeconds = message.Y;
            m_Prompt = message.Text;
            m_PromptDrawing = message.Data;
            m_TurnTimedOut = false;
            ResetStep();
            if (message.X > 0f)
            {
                m_Clock.Start(message.X);
                EnterPhase(MatchPhase.Countdown);
            }
            else
            {
                m_Clock.Start(m_TurnSeconds);
                EnterPhase(MatchPhase.Turn);
            }
        }

        private void OnPresentItem(NetMessage message)
        {
            StartPlaying();
            m_PresentChain = message.A;
            m_PresentIndex = message.B;
            m_PresentItem = new PresentItem
            {
                Kind = (PresentItemKind)message.C,
                Player = message.D,
                Text = message.Text,
                Drawing = message.Data,
            };
            m_Clock.Start(message.X);
            if (m_Phase == MatchPhase.Present)
            {
                // The next item of the same chain: the screens notice the new PresentIndex.
                return;
            }
            ResetStep();
            EnterPhase(MatchPhase.Present);
        }

        private void OnVoteOpen(NetMessage message)
        {
            StartPlaying();
            m_PresentChain = message.A;
            m_PresentItem = new PresentItem { Kind = PresentItemKind.Guess, Player = message.D, Text = message.Text };
            m_PresentedWord = message.Words != null && message.Words.Length > 0 ? message.Words[0] : string.Empty;
            m_Clock.Start(message.X);
            ResetStep();
            EnterPhase(MatchPhase.Vote);
        }

        // ----- Helpers -----

        private bool InTurnPhase
        {
            get { return m_Phase == MatchPhase.Countdown || m_Phase == MatchPhase.Turn; }
        }

        private void StartPlaying()
        {
            if (m_State == ClientState.Playing)
            {
                return;
            }
            if (m_Planner == null || m_Planner.PlayerCount != m_Names.Count)
            {
                m_Planner = new ChainPlanner(Math.Max(MatchSettings.MinPlayers, m_Names.Count));
            }
            SetState(ClientState.Playing);
        }

        private void ResetStep()
        {
            m_LocalDone = false;
            m_DoneMask = 0;
        }

        private void MarkLocalDone()
        {
            m_LocalDone = true;
            if (m_LocalSeat >= 0 && m_LocalSeat < 31)
            {
                m_DoneMask |= 1 << m_LocalSeat;
            }
        }

        private bool IsDone(int seat)
        {
            return seat >= 0 && seat < 31 && (m_DoneMask & (1 << seat)) != 0;
        }

        private void SetScores(int[] scores)
        {
            m_Scores = scores == null ? new int[m_Names.Count] : (int[])scores.Clone();
        }

        private void EnterPhase(MatchPhase to)
        {
            MatchPhase from = m_Phase;
            m_Phase = to;
            Action<MatchPhase, MatchPhase> handler = PhaseChanged;
            if (handler != null)
            {
                handler(from, to);
            }
        }

        private void SetState(ClientState state)
        {
            if (m_State == state)
            {
                return;
            }
            m_State = state;
            Action handler = StateChanged;
            if (handler != null)
            {
                handler();
            }
        }

        private void RaisePlayerLeft(int seat)
        {
            Action<int> handler = PlayerLeft;
            if (handler != null)
            {
                handler(seat);
            }
        }

        private void RaiseEnvironment()
        {
            Action<string> handler = EnvironmentChanged;
            if (handler != null)
            {
                handler(m_Environment);
            }
        }
    }
}
