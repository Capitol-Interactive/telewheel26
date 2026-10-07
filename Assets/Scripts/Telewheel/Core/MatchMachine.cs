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
    public enum MatchPhase
    {
        Lobby,

        /// <summary>The headset is changing hands; a "Pass it to X." screen waits for READY.</summary>
        Handoff,

        /// <summary>The active player spins the wheel and sees their secret word.</summary>
        Spin,

        /// <summary>The short "get ready" countdown before a Draw turn.</summary>
        Countdown,

        /// <summary>A timed Draw or Guess turn.</summary>
        Turn,

        /// <summary>The reveal of a finished chain, one item at a time.</summary>
        Present,

        /// <summary>The final guess is on show and the room votes on it.</summary>
        Vote,

        /// <summary>Shows whether the vote landed.</summary>
        VoteResult,

        RoundEnd,
        GameEnd,
    }

    /// <summary>
    /// The rules of a Pass &amp; Play match as a state machine with no Unity dependencies. A single
    /// player holds the headset at a time; <see cref="ChainPlanner"/> decides the order.
    ///
    /// The owner of the machine (the Unity director, a test, or a bot) drives it with the commands
    /// below and calls <see cref="Tick"/> with elapsed time. Everything that needs the engine,
    /// such as capturing the drawing when a turn times out, happens outside and is passed back in.
    /// </summary>
    public sealed class MatchMachine
    {
        private readonly MatchSettings m_Settings;
        private readonly ChainPlanner m_Planner;
        private readonly WordDeck m_Deck;
        private readonly TurnClock m_Clock = new TurnClock();
        private readonly int[] m_Scores;

        private IReadOnlyList<Stage> m_Schedule;
        private int m_StageIndex;
        private ChainState[] m_Chains;
        private MatchPhase m_Phase = MatchPhase.Lobby;
        private int m_Round;
        private List<string> m_WheelWords = new List<string>();
        private string m_SpinWord;
        private bool m_TurnExpired;

        private int m_PresentChain;
        private int m_PresentIndex;
        private List<PresentItem> m_PresentItems = new List<PresentItem>();
        private VoteTally m_Tally;

        public MatchMachine(MatchSettings settings, WordDeck deck)
        {
            settings.Validate();
            m_Settings = settings;
            m_Deck = deck;
            m_Planner = new ChainPlanner(settings.PlayerCount);
            m_Scores = new int[settings.PlayerCount];
        }

        /// <summary>Raised after every phase change with (old, new).</summary>
        public event Action<MatchPhase, MatchPhase> PhaseChanged;

        /// <summary>Raised once when a Draw or Guess turn runs out of time. The owner must then submit.</summary>
        public event Action TurnExpired;

        public MatchSettings Settings
        {
            get { return m_Settings; }
        }

        public ChainPlanner Planner
        {
            get { return m_Planner; }
        }

        public MatchPhase Phase
        {
            get { return m_Phase; }
        }

        /// <summary>1-based round number, or 0 before the match starts.</summary>
        public int Round
        {
            get { return m_Round; }
        }

        public bool IsFinalRound
        {
            get { return m_Round >= m_Settings.Rounds; }
        }

        public IReadOnlyList<int> Scores
        {
            get { return m_Scores; }
        }

        public IReadOnlyList<ChainState> Chains
        {
            get { return m_Chains; }
        }

        public TurnClock Clock
        {
            get { return m_Clock; }
        }

        public bool TimedOut
        {
            get { return m_TurnExpired; }
        }

        // ----- Current stage -----

        public bool HasStage
        {
            get { return m_Schedule != null && m_StageIndex >= 0 && m_StageIndex < m_Schedule.Count; }
        }

        public Stage CurrentStage
        {
            get
            {
                if (!HasStage)
                {
                    throw new InvalidOperationException("No stage is active in phase " + m_Phase);
                }
                return m_Schedule[m_StageIndex];
            }
        }

        /// <summary>Who currently holds the headset, or -1 when nobody does (lobby, reveal, scores).</summary>
        public int ActivePlayer
        {
            get
            {
                switch (m_Phase)
                {
                    case MatchPhase.Handoff:
                    case MatchPhase.Spin:
                    case MatchPhase.Countdown:
                    case MatchPhase.Turn:
                        return CurrentStage.Player;
                    default:
                        return -1;
                }
            }
        }

        /// <summary>The words on the wheel during a Spin.</summary>
        public IReadOnlyList<string> WheelWords
        {
            get { return m_WheelWords; }
        }

        /// <summary>The word the wheel landed on, once the spin has completed (until confirmed).</summary>
        public string SpinWord
        {
            get { return m_SpinWord; }
        }

        /// <summary>
        /// What a Draw turn must draw: the spun word on turn 0, otherwise the previous player's guess.
        /// Null for a Guess turn.
        /// </summary>
        public string PromptText
        {
            get
            {
                if (!InTurnPhase() || CurrentStage.Kind != StageKind.Draw)
                {
                    return null;
                }
                ChainState chain = m_Chains[CurrentStage.Chain];
                return CurrentStage.Turn == 0 ? chain.Word : chain.Entries[CurrentStage.Turn - 1].Text;
            }
        }

        /// <summary>The previous player's drawing, shown on a Guess turn. Null on a Draw turn.</summary>
        public byte[] PromptDrawing
        {
            get
            {
                if (!InTurnPhase() || CurrentStage.Kind != StageKind.Guess)
                {
                    return null;
                }
                return m_Chains[CurrentStage.Chain].Entries[CurrentStage.Turn - 1].Drawing;
            }
        }

        // ----- Present / vote -----

        public int PresentChain
        {
            get { return m_PresentChain; }
        }

        public int PresentIndex
        {
            get { return m_PresentIndex; }
        }

        /// <summary>Items shown before the vote (the final guess is shown by the Vote phase).</summary>
        public IReadOnlyList<PresentItem> PresentItems
        {
            get { return m_PresentItems; }
        }

        public PresentItem CurrentPresentItem
        {
            get
            {
                if (m_Phase == MatchPhase.Present || m_Phase == MatchPhase.Vote)
                {
                    return m_PresentItems[m_PresentIndex];
                }
                return null;
            }
        }

        public VoteTally Tally
        {
            get { return m_Tally; }
        }

        /// <summary>True when the last vote landed (valid in VoteResult).</summary>
        public bool LastVoteLanded
        {
            get { return m_Chains != null && m_PresentChain < m_Chains.Length && m_Chains[m_PresentChain].Landed; }
        }

        /// <summary>Players with the top score. Meaningful in RoundEnd and GameEnd.</summary>
        public IList<int> Leaders
        {
            get
            {
                var leaders = new List<int>();
                int best = int.MinValue;
                for (int i = 0; i < m_Scores.Length; i++)
                {
                    if (m_Scores[i] > best)
                    {
                        best = m_Scores[i];
                        leaders.Clear();
                    }
                    if (m_Scores[i] == best)
                    {
                        leaders.Add(i);
                    }
                }
                return leaders;
            }
        }

        // ----- Commands -----

        /// <summary>Lobby to the first hand-off. Starts round 1.</summary>
        public void Start()
        {
            Require(MatchPhase.Lobby);
            m_Round = 1;
            BuildRound();
        }

        /// <summary>The next player is holding the headset; start their stage.</summary>
        public void ConfirmHandoff()
        {
            Require(MatchPhase.Handoff);
            BeginStage();
        }

        /// <summary>Records which wheel segment the pointer stopped on. The word stays on screen until confirmed.</summary>
        public void CompleteSpin(int segment)
        {
            Require(MatchPhase.Spin);
            if (m_SpinWord != null)
            {
                return;
            }
            if (segment < 0 || segment >= m_WheelWords.Count)
            {
                throw new ArgumentOutOfRangeException("segment");
            }
            m_SpinWord = m_WheelWords[segment];
            m_Deck.Consume(m_SpinWord);
            m_Chains[CurrentStage.Chain].Word = m_SpinWord;
        }

        /// <summary>The player has read their secret word; move on.</summary>
        public void ConfirmSpin()
        {
            Require(MatchPhase.Spin);
            if (m_SpinWord == null)
            {
                throw new InvalidOperationException("The wheel has not stopped yet");
            }
            AdvanceStage();
        }

        /// <summary>Ends the countdown early.</summary>
        public void SkipCountdown()
        {
            Require(MatchPhase.Countdown);
            StartTurnClock();
        }

        /// <summary>Stores the finished drawing for the active Draw turn (empty or null for a blank canvas).</summary>
        public void SubmitDrawing(byte[] drawing)
        {
            Require(MatchPhase.Turn);
            Stage stage = CurrentStage;
            if (stage.Kind != StageKind.Draw)
            {
                throw new InvalidOperationException("The active turn is a guess");
            }
            AddEntry(stage, drawing ?? new byte[0], null);
            AdvanceStage();
        }

        /// <summary>
        /// Stores the typed guess for the active Guess turn. Returns false (and stays put) when the
        /// text is empty and the turn has not timed out; a timed-out empty guess is stored as "...".
        /// </summary>
        public bool SubmitGuess(string text)
        {
            Require(MatchPhase.Turn);
            Stage stage = CurrentStage;
            if (stage.Kind != StageKind.Guess)
            {
                throw new InvalidOperationException("The active turn is a drawing");
            }
            string guess = GuessNormalizer.Normalize(text);
            if (guess.Length == 0)
            {
                if (!m_TurnExpired)
                {
                    return false;
                }
                guess = TwCopy.NoGuess;
            }
            AddEntry(stage, null, guess);
            AdvanceStage();
            return true;
        }

        /// <summary>Moves the reveal on to the next item (the player tapped, or time ran out).</summary>
        public void SkipPresent()
        {
            Require(MatchPhase.Present);
            m_PresentIndex++;
            if (m_PresentIndex >= m_PresentItems.Count - 1)
            {
                m_PresentIndex = m_PresentItems.Count - 1;
                m_Tally = new VoteTally(m_Settings.VoterCount);
                m_Clock.Start(m_Settings.PresentFinalSeconds);
                SetPhase(MatchPhase.Vote);
            }
            else
            {
                m_Clock.Start(m_Settings.PresentItemSeconds);
            }
        }

        /// <summary>Casts a vote on the final guess. With a shared vote the voter index is ignored.</summary>
        public void CastVote(int voter, bool yes)
        {
            Require(MatchPhase.Vote);
            m_Tally.Cast(m_Settings.SharedVote ? 0 : voter, yes);
            if (m_Tally.Complete)
            {
                ResolveVote();
            }
        }

        /// <summary>Moves from the vote result to the next chain or the end of the round.</summary>
        public void ContinueAfterVote()
        {
            Require(MatchPhase.VoteResult);
            m_PresentChain++;
            if (m_PresentChain < m_Chains.Length)
            {
                BeginPresent();
            }
            else
            {
                m_Clock.Stop();
                SetPhase(MatchPhase.RoundEnd);
            }
        }

        /// <summary>From the round scoreboard: start the next round, or finish the game.</summary>
        public void ContinueRound()
        {
            Require(MatchPhase.RoundEnd);
            if (IsFinalRound)
            {
                SetPhase(MatchPhase.GameEnd);
                return;
            }
            m_Round++;
            BuildRound();
        }

        /// <summary>Clears scores and returns to the lobby with the same players and rules.</summary>
        public void Reset()
        {
            for (int i = 0; i < m_Scores.Length; i++)
            {
                m_Scores[i] = 0;
            }
            m_Round = 0;
            m_Schedule = null;
            m_StageIndex = -1;
            m_Chains = null;
            m_SpinWord = null;
            m_TurnExpired = false;
            m_Clock.Stop();
            SetPhase(MatchPhase.Lobby);
        }

        /// <summary>Advances time. Call every frame with the (possibly scaled) frame time.</summary>
        public void Tick(float dt)
        {
            if (!m_Clock.Tick(dt))
            {
                return;
            }
            switch (m_Phase)
            {
                case MatchPhase.Countdown:
                    StartTurnClock();
                    break;
                case MatchPhase.Turn:
                    m_TurnExpired = true;
                    Action handler = TurnExpired;
                    if (handler != null)
                    {
                        handler();
                    }
                    break;
                case MatchPhase.Present:
                    SkipPresent();
                    break;
                case MatchPhase.Vote:
                    ResolveVote();
                    break;
                case MatchPhase.VoteResult:
                    ContinueAfterVote();
                    break;
            }
        }

        // ----- Internals -----

        private bool InTurnPhase()
        {
            return m_Phase == MatchPhase.Turn || m_Phase == MatchPhase.Countdown;
        }

        private void Require(MatchPhase expected)
        {
            if (m_Phase != expected)
            {
                throw new InvalidOperationException(
                    "Expected phase " + expected + " but the match is in " + m_Phase);
            }
        }

        private void SetPhase(MatchPhase next)
        {
            MatchPhase old = m_Phase;
            m_Phase = next;
            Action<MatchPhase, MatchPhase> handler = PhaseChanged;
            if (handler != null)
            {
                handler(old, next);
            }
        }

        private void BuildRound()
        {
            m_Chains = new ChainState[m_Settings.PlayerCount];
            for (int i = 0; i < m_Chains.Length; i++)
            {
                m_Chains[i] = new ChainState { Owner = i };
            }
            m_Schedule = m_Planner.BuildSequentialSchedule();
            m_StageIndex = -1;
            m_SpinWord = null;
            m_TurnExpired = false;
            AdvanceStage();
        }

        private void AdvanceStage()
        {
            m_StageIndex++;
            m_SpinWord = null;
            m_TurnExpired = false;
            if (m_StageIndex >= m_Schedule.Count)
            {
                m_PresentChain = 0;
                BeginPresent();
                return;
            }
            bool newHolder = m_StageIndex == 0
                || m_Schedule[m_StageIndex - 1].Player != m_Schedule[m_StageIndex].Player;
            if (newHolder)
            {
                m_Clock.Stop();
                SetPhase(MatchPhase.Handoff);
            }
            else
            {
                BeginStage();
            }
        }

        private void BeginStage()
        {
            Stage stage = CurrentStage;
            switch (stage.Kind)
            {
                case StageKind.Spin:
                    m_WheelWords = m_Deck.Draw(m_Settings.WheelSegments);
                    m_Clock.Stop();
                    SetPhase(MatchPhase.Spin);
                    break;
                case StageKind.Draw:
                    if (m_Settings.CountdownSeconds > 0)
                    {
                        m_Clock.Start(m_Settings.CountdownSeconds);
                        SetPhase(MatchPhase.Countdown);
                    }
                    else
                    {
                        StartTurnClock();
                    }
                    break;
                default:
                    StartTurnClock();
                    break;
            }
        }

        private void StartTurnClock()
        {
            Stage stage = CurrentStage;
            m_TurnExpired = false;
            m_Clock.Start(stage.Kind == StageKind.Draw ? m_Settings.DrawSeconds : m_Settings.GuessSeconds);
            SetPhase(MatchPhase.Turn);
        }

        private void AddEntry(Stage stage, byte[] drawing, string text)
        {
            m_Chains[stage.Chain].Entries.Add(new ChainEntry
            {
                Player = stage.Player,
                Kind = stage.Kind,
                Drawing = drawing,
                Text = text,
            });
        }

        private void BeginPresent()
        {
            ChainState chain = m_Chains[m_PresentChain];
            m_PresentItems = new List<PresentItem>
            {
                new PresentItem { Kind = PresentItemKind.Word, Player = chain.Owner, Text = chain.Word },
            };
            foreach (ChainEntry entry in chain.Entries)
            {
                m_PresentItems.Add(new PresentItem
                {
                    Kind = entry.Kind == StageKind.Draw ? PresentItemKind.Drawing : PresentItemKind.Guess,
                    Player = entry.Player,
                    Text = entry.Text,
                    Drawing = entry.Drawing,
                });
            }
            m_PresentIndex = 0;
            m_Clock.Start(m_Settings.PresentItemSeconds);
            SetPhase(MatchPhase.Present);
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
            m_Clock.Start(Math.Max(0.01f, m_Settings.VoteResultSeconds));
            SetPhase(MatchPhase.VoteResult);
        }
    }
}
