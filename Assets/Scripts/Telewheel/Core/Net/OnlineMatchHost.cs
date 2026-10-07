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
    public enum OnlinePhase
    {
        Lobby,
        Spin,
        Turn,
        Present,
        Vote,
        VoteResult,
        RoundEnd,
        GameEnd,
    }

    /// <summary>
    /// The host's side of an online match: everyone plays at once, and the host is the only one who
    /// decides what happens. It has no networking in it. Players' messages go in through
    /// <see cref="Receive"/> and the host's replies come out through <see cref="Send"/>, so any
    /// transport (Photon, a test harness) can carry them.
    ///
    /// The rules are the same as Pass &amp; Play (see <see cref="ChainPlanner"/>), except that in each
    /// turn every player works on a different chain at the same time, and the host moves on when
    /// everyone has submitted or the clock runs out. A player who leaves is played by the host:
    /// blank drawings, empty guesses and no votes, so one person leaving never stalls the room.
    /// </summary>
    public sealed class OnlineMatchHost
    {
        /// <summary>Sent to every player.</summary>
        public const int Everyone = -1;

        private const int MaxMessagesPerPump = 10000;

        private readonly MatchSettings m_Settings;
        private readonly WordDeck m_Deck;
        private readonly List<string> m_Names = new List<string>();
        private readonly List<bool> m_Gone = new List<bool>();
        private readonly Queue<KeyValuePair<int, NetMessage>> m_Inbox = new Queue<KeyValuePair<int, NetMessage>>();
        private readonly TurnClock m_Clock = new TurnClock();

        private ChainPlanner m_Planner;
        private int[] m_Scores;
        private ChainState[] m_Chains;
        private OnlinePhase m_Phase = OnlinePhase.Lobby;
        private int m_Round;

        private List<string>[] m_Candidates;
        private bool[] m_Spun;
        private bool[] m_Ready;

        private int m_Turn;
        private bool[] m_Submitted;

        private int m_PresentChain;
        private int m_PresentIndex;
        private List<PresentItem> m_Items;
        private VoteTally m_Tally;

        public OnlineMatchHost(MatchSettings settings, WordDeck deck)
        {
            m_Settings = settings;
            m_Deck = deck;
            settings.SharedVote = false; // Online, every player votes.
        }

        /// <summary>Raised for every message the host sends: (player, message), or <see cref="Everyone"/>.</summary>
        public event Action<int, NetMessage> Send;

        public OnlinePhase Phase
        {
            get { return m_Phase; }
        }

        public int Round
        {
            get { return m_Round; }
        }

        public int Turn
        {
            get { return m_Turn; }
        }

        public int PlayerCount
        {
            get { return m_Names.Count; }
        }

        public MatchSettings Settings
        {
            get { return m_Settings; }
        }

        public IReadOnlyList<int> Scores
        {
            get { return m_Scores; }
        }

        public IReadOnlyList<ChainState> Chains
        {
            get { return m_Chains; }
        }

        public ChainPlanner Planner
        {
            get { return m_Planner; }
        }

        public TurnClock Clock
        {
            get { return m_Clock; }
        }

        public string NameOf(int player)
        {
            return player >= 0 && player < m_Names.Count ? m_Names[player] : TwCopy.DefaultPlayerName(player);
        }

        public bool IsGone(int player)
        {
            return player >= 0 && player < m_Gone.Count && m_Gone[player];
        }

        // ----- Lobby -----

        /// <summary>Seats a player and returns their seat number. Only before the match starts.</summary>
        public int AddPlayer(string name)
        {
            if (m_Phase != OnlinePhase.Lobby)
            {
                throw new InvalidOperationException("The match has already started");
            }
            if (m_Names.Count >= MatchSettings.MaxPlayers)
            {
                throw new InvalidOperationException("The room is full");
            }
            string clean = GuessNormalizer.Normalize(name);
            m_Names.Add(clean.Length == 0 ? TwCopy.DefaultPlayerName(m_Names.Count) : clean);
            m_Gone.Add(false);
            return m_Names.Count - 1;
        }

        /// <summary>
        /// A player left. Before the match their seat is removed (later seats shift down); during
        /// it they stay in the chain order and the host plays their turns.
        /// </summary>
        public void RemovePlayer(int seat)
        {
            if (seat < 0 || seat >= m_Names.Count)
            {
                return;
            }
            if (m_Phase == OnlinePhase.Lobby)
            {
                m_Names.RemoveAt(seat);
                m_Gone.RemoveAt(seat);
                return;
            }
            m_Gone[seat] = true;
            AutoPlayGonePlayers();
        }

        public void Start()
        {
            if (m_Phase != OnlinePhase.Lobby)
            {
                throw new InvalidOperationException("The match has already started");
            }
            m_Settings.PlayerCount = m_Names.Count;
            m_Settings.PlayerNames = m_Names.ToArray();
            m_Settings.Validate();
            m_Planner = new ChainPlanner(m_Names.Count);
            m_Scores = new int[m_Names.Count];
            m_Round = 1;
            BeginRound();
        }

        // ----- Input and time -----

        /// <summary>Queues a message from a player. It is handled on the next <see cref="Tick"/> or <see cref="Pump"/>.</summary>
        public void Receive(int fromPlayer, NetMessage message)
        {
            if (message != null)
            {
                m_Inbox.Enqueue(new KeyValuePair<int, NetMessage>(fromPlayer, message));
            }
        }

        /// <summary>Handles everything queued. Replies that arrive meanwhile are handled too.</summary>
        public void Pump()
        {
            int handled = 0;
            while (m_Inbox.Count > 0 && handled++ < MaxMessagesPerPump)
            {
                KeyValuePair<int, NetMessage> item = m_Inbox.Dequeue();
                Handle(item.Key, item.Value);
            }
        }

        public void Tick(float dt)
        {
            Pump();
            if (m_Clock.Tick(dt))
            {
                OnClockExpired();
            }
            Pump();
        }

        // ----- Handling players' messages -----

        private void Handle(int from, NetMessage message)
        {
            if (m_Phase == OnlinePhase.Lobby || from < 0 || from >= m_Names.Count || m_Gone[from])
            {
                return;
            }
            switch (message.Kind)
            {
                case NetKind.SpinResult:
                    if (m_Phase == OnlinePhase.Spin)
                    {
                        Spin(from, message.A);
                    }
                    break;
                case NetKind.Ready:
                    if (m_Phase == OnlinePhase.Spin && m_Spun[from])
                    {
                        m_Ready[from] = true;
                        CheckSpinDone();
                    }
                    break;
                case NetKind.SubmitDrawing:
                    if (m_Phase == OnlinePhase.Turn && m_Planner.KindOfTurn(m_Turn) == StageKind.Draw)
                    {
                        Submit(from, message.Data ?? new byte[0], null);
                    }
                    break;
                case NetKind.SubmitGuess:
                    if (m_Phase == OnlinePhase.Turn && m_Planner.KindOfTurn(m_Turn) == StageKind.Guess)
                    {
                        string guess = GuessNormalizer.Normalize(message.Text);
                        Submit(from, null, guess.Length == 0 ? TwCopy.NoGuess : guess);
                    }
                    break;
                case NetKind.Vote:
                    if (m_Phase == OnlinePhase.Vote)
                    {
                        m_Tally.Cast(from, message.A != 0);
                        if (m_Tally.Complete)
                        {
                            ResolveVote();
                        }
                    }
                    break;
                case NetKind.Advance:
                    if (m_Phase == OnlinePhase.Present && from == m_Chains[m_PresentChain].Owner)
                    {
                        AdvancePresent();
                    }
                    break;
            }
        }

        // ----- Spin -----

        private void BeginRound()
        {
            m_Chains = new ChainState[m_Names.Count];
            for (int i = 0; i < m_Chains.Length; i++)
            {
                m_Chains[i] = new ChainState { Owner = i };
            }
            m_Candidates = new List<string>[m_Names.Count];
            m_Spun = new bool[m_Names.Count];
            m_Ready = new bool[m_Names.Count];
            SetPhase(OnlinePhase.Spin);
            m_Clock.Start(m_Settings.SpinSeconds);
            for (int p = 0; p < m_Names.Count; p++)
            {
                m_Candidates[p] = m_Deck.Draw(m_Settings.WheelSegments);
                SendTo(p, NetMessage.WheelWords(m_Candidates[p].ToArray()));
            }
            AutoPlayGonePlayers();
        }

        private void Spin(int player, int segment)
        {
            if (m_Spun[player])
            {
                return;
            }
            int index = Math.Max(0, Math.Min(m_Candidates[player].Count - 1, segment));
            string word = m_Candidates[player][index];
            m_Chains[player].Word = word;
            m_Deck.Consume(word);
            m_Spun[player] = true;
        }

        private void CheckSpinDone()
        {
            for (int p = 0; p < m_Names.Count; p++)
            {
                if (!m_Ready[p])
                {
                    return;
                }
            }
            BeginTurn(0);
        }

        // ----- Turns -----

        private void BeginTurn(int turn)
        {
            m_Turn = turn;
            m_Submitted = new bool[m_Names.Count];
            StageKind kind = m_Planner.KindOfTurn(turn);
            float countdown = kind == StageKind.Draw ? m_Settings.CountdownSeconds : 0f;
            float seconds = kind == StageKind.Draw ? m_Settings.DrawSeconds : m_Settings.GuessSeconds;
            SetPhase(OnlinePhase.Turn);
            m_Clock.Start(countdown + seconds + m_Settings.TurnGraceSeconds);
            for (int p = 0; p < m_Names.Count; p++)
            {
                ChainState chain = m_Chains[m_Planner.ChainForTurn(p, turn)];
                string prompt = null;
                byte[] promptDrawing = null;
                if (kind == StageKind.Draw)
                {
                    prompt = turn == 0 ? chain.Word : chain.Entries[turn - 1].Text;
                }
                else
                {
                    promptDrawing = chain.Entries[turn - 1].Drawing;
                }
                SendTo(p, NetMessage.TurnStart(turn, kind, countdown, seconds, prompt, promptDrawing));
            }
            AutoPlayGonePlayers();
        }

        private void Submit(int player, byte[] drawing, string text)
        {
            if (m_Submitted[player])
            {
                return;
            }
            m_Submitted[player] = true;
            int chain = m_Planner.ChainForTurn(player, m_Turn);
            m_Chains[chain].Entries.Add(new ChainEntry
            {
                Player = player,
                Kind = m_Planner.KindOfTurn(m_Turn),
                Drawing = drawing,
                Text = text,
            });
            for (int p = 0; p < m_Submitted.Length; p++)
            {
                if (!m_Submitted[p])
                {
                    return;
                }
            }
            EndTurn();
        }

        private void EndTurn()
        {
            if (m_Turn + 1 < m_Planner.TurnsPerChain)
            {
                BeginTurn(m_Turn + 1);
            }
            else
            {
                m_PresentChain = 0;
                BeginChainPresent();
            }
        }

        // ----- Reveal and vote -----

        private void BeginChainPresent()
        {
            ChainState chain = m_Chains[m_PresentChain];
            m_Items = new List<PresentItem>
            {
                new PresentItem { Kind = PresentItemKind.Word, Player = chain.Owner, Text = chain.Word },
            };
            foreach (ChainEntry entry in chain.Entries)
            {
                m_Items.Add(new PresentItem
                {
                    Kind = entry.Kind == StageKind.Draw ? PresentItemKind.Drawing : PresentItemKind.Guess,
                    Player = entry.Player,
                    Text = entry.Text,
                    Drawing = entry.Drawing,
                });
            }
            m_PresentIndex = 0;
            SetPhase(OnlinePhase.Present);
            ShowPresentItem();
        }

        private void ShowPresentItem()
        {
            PresentItem item = m_Items[m_PresentIndex];
            m_Clock.Start(m_Settings.PresentItemSeconds);
            SendTo(Everyone, NetMessage.PresentItem(
                m_PresentChain, m_PresentIndex, m_Items.Count, item.Kind, item.Player, item.Text, item.Drawing,
                m_Settings.PresentItemSeconds));
        }

        private void AdvancePresent()
        {
            m_PresentIndex++;
            if (m_PresentIndex >= m_Items.Count - 1)
            {
                BeginVote();
            }
            else
            {
                ShowPresentItem();
            }
        }

        private void BeginVote()
        {
            PresentItem last = m_Items[m_Items.Count - 1];
            m_Tally = new VoteTally(m_Names.Count);
            SetPhase(OnlinePhase.Vote);
            m_Clock.Start(m_Settings.PresentFinalSeconds);
            SendTo(Everyone, NetMessage.VoteOpen(
                m_PresentChain, last.Player, last.Text, m_Items[0].Text, m_Settings.PresentFinalSeconds));
            for (int p = 0; p < m_Names.Count; p++)
            {
                if (m_Gone[p])
                {
                    m_Tally.Cast(p, false);
                }
            }
            if (m_Tally.Complete)
            {
                ResolveVote();
            }
        }

        private void ResolveVote()
        {
            ChainState chain = m_Chains[m_PresentChain];
            chain.VoteResolved = true;
            chain.Landed = m_Tally.Landed;
            if (chain.Landed)
            {
                m_Scores[chain.Owner] += 1;
            }
            SetPhase(OnlinePhase.VoteResult);
            m_Clock.Start(Math.Max(0.01f, m_Settings.VoteResultSeconds));
            SendTo(Everyone, NetMessage.VoteResult(m_PresentChain, chain.Landed, m_Scores.Clone() as int[]));
        }

        private void NextChain()
        {
            m_PresentChain++;
            if (m_PresentChain < m_Chains.Length)
            {
                BeginChainPresent();
                return;
            }
            bool isFinal = m_Round >= m_Settings.Rounds;
            SetPhase(OnlinePhase.RoundEnd);
            m_Clock.Start(Math.Max(0.01f, m_Settings.RoundEndSeconds));
            SendTo(Everyone, NetMessage.RoundEnd(m_Round, isFinal, m_Scores.Clone() as int[]));
        }

        private void NextRoundOrEnd()
        {
            if (m_Round >= m_Settings.Rounds)
            {
                m_Clock.Stop();
                SetPhase(OnlinePhase.GameEnd);
                SendTo(Everyone, NetMessage.GameEnd(m_Scores.Clone() as int[]));
                return;
            }
            m_Round++;
            BeginRound();
        }

        // ----- Clock and departures -----

        private void OnClockExpired()
        {
            switch (m_Phase)
            {
                case OnlinePhase.Spin:
                    for (int p = 0; p < m_Names.Count; p++)
                    {
                        Spin(p, 0);
                        m_Ready[p] = true;
                    }
                    CheckSpinDone();
                    break;
                case OnlinePhase.Turn:
                    FillMissingSubmissions();
                    break;
                case OnlinePhase.Present:
                    AdvancePresent();
                    break;
                case OnlinePhase.Vote:
                    ResolveVote();
                    break;
                case OnlinePhase.VoteResult:
                    NextChain();
                    break;
                case OnlinePhase.RoundEnd:
                    NextRoundOrEnd();
                    break;
            }
        }

        // Anyone who has not handed in a drawing or guess gets a blank one, so the chain stays whole.
        private void FillMissingSubmissions()
        {
            StageKind kind = m_Planner.KindOfTurn(m_Turn);
            int turn = m_Turn;
            for (int p = 0; p < m_Names.Count && m_Phase == OnlinePhase.Turn && m_Turn == turn; p++)
            {
                if (!m_Submitted[p])
                {
                    Submit(p, kind == StageKind.Draw ? new byte[0] : null, kind == StageKind.Guess ? TwCopy.NoGuess : null);
                }
            }
        }

        // Plays the turns of anyone who has left, using blank drawings and empty guesses.
        private void AutoPlayGonePlayers()
        {
            if (m_Phase == OnlinePhase.Spin)
            {
                for (int p = 0; p < m_Names.Count; p++)
                {
                    if (m_Gone[p])
                    {
                        Spin(p, 0);
                        m_Ready[p] = true;
                    }
                }
                CheckSpinDone();
            }
            else if (m_Phase == OnlinePhase.Turn)
            {
                StageKind kind = m_Planner.KindOfTurn(m_Turn);
                int turn = m_Turn;
                for (int p = 0; p < m_Names.Count && m_Phase == OnlinePhase.Turn && m_Turn == turn; p++)
                {
                    if (m_Gone[p] && !m_Submitted[p])
                    {
                        Submit(p, kind == StageKind.Draw ? new byte[0] : null, kind == StageKind.Guess ? TwCopy.NoGuess : null);
                    }
                }
            }
            else if (m_Phase == OnlinePhase.Vote && m_Tally != null)
            {
                for (int p = 0; p < m_Names.Count; p++)
                {
                    if (m_Gone[p])
                    {
                        m_Tally.Cast(p, false);
                    }
                }
                if (m_Tally.Complete)
                {
                    ResolveVote();
                }
            }
        }

        private void SetPhase(OnlinePhase phase)
        {
            m_Phase = phase;
            SendTo(Everyone, NetMessage.PhaseChanged(phase, m_Round));
        }

        private void SendTo(int player, NetMessage message)
        {
            if (player >= 0 && player < m_Gone.Count && m_Gone[player])
            {
                return;
            }
            Action<int, NetMessage> handler = Send;
            if (handler != null)
            {
                handler(player, message);
            }
        }
    }
}
