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
    /// <summary>A Pass &amp; Play <see cref="MatchMachine"/> seen through the interface the screens use.</summary>
    public sealed class PassAndPlaySession : IMatchSession
    {
        private static readonly int[] NobodyWaiting = new int[0];

        private readonly MatchMachine m_Machine;

        public PassAndPlaySession(MatchMachine machine)
        {
            m_Machine = machine;
            m_Machine.PhaseChanged += OnPhaseChanged;
            m_Machine.TurnExpired += OnTurnExpired;
        }

        public event Action<MatchPhase, MatchPhase> PhaseChanged;

        public event Action TurnExpired;

        public MatchMachine Machine
        {
            get { return m_Machine; }
        }

        public MatchPhase Phase
        {
            get { return m_Machine.Phase; }
        }

        public int Round
        {
            get { return m_Machine.Round; }
        }

        public int RoundCount
        {
            get { return m_Machine.Settings.Rounds; }
        }

        public bool IsFinalRound
        {
            get { return m_Machine.IsFinalRound; }
        }

        public int PlayerCount
        {
            get { return m_Machine.Settings.PlayerCount; }
        }

        public int WheelSegments
        {
            get { return m_Machine.Settings.WheelSegments; }
        }

        public int ActiveSeat
        {
            get { return m_Machine.ActivePlayer; }
        }

        public string NameOf(int seat)
        {
            return m_Machine.Settings.NameOf(seat);
        }

        public ChainPlanner Planner
        {
            get { return m_Machine.Planner; }
        }

        public TurnClock Clock
        {
            get { return m_Machine.Clock; }
        }

        public int TurnIndex
        {
            get { return m_Machine.HasStage ? m_Machine.CurrentStage.Turn : 0; }
        }

        public StageKind TurnKind
        {
            get { return m_Machine.HasStage ? m_Machine.CurrentStage.Kind : StageKind.Draw; }
        }

        public string SpinWord
        {
            get { return m_Machine.SpinWord; }
        }

        public string PromptText
        {
            get { return m_Machine.PromptText; }
        }

        public byte[] PromptDrawing
        {
            get { return m_Machine.PromptDrawing; }
        }

        public int PresentChain
        {
            get { return m_Machine.PresentChain; }
        }

        public int PresentOwner
        {
            get { return m_Machine.Chains[m_Machine.PresentChain].Owner; }
        }

        public int PresentIndex
        {
            get { return m_Machine.PresentIndex; }
        }

        public PresentItem CurrentPresentItem
        {
            get { return m_Machine.CurrentPresentItem; }
        }

        public string PresentedWord
        {
            get { return m_Machine.PresentItems[0].Text; }
        }

        public bool CanAdvancePresent
        {
            get { return true; }
        }

        public bool LastVoteLanded
        {
            get { return m_Machine.LastVoteLanded; }
        }

        public IReadOnlyList<int> Scores
        {
            get { return m_Machine.Scores; }
        }

        public IList<int> Leaders
        {
            get { return m_Machine.Leaders; }
        }

        public bool IsOnline
        {
            get { return false; }
        }

        public bool LocalDone
        {
            get { return false; }
        }

        public IReadOnlyList<int> WaitingFor
        {
            get { return NobodyWaiting; }
        }

        public void Tick(float dt)
        {
            m_Machine.Tick(dt);
        }

        public void ConfirmHandoff()
        {
            m_Machine.ConfirmHandoff();
        }

        public void SkipCountdown()
        {
            m_Machine.SkipCountdown();
        }

        public void CompleteSpin(int segment)
        {
            m_Machine.CompleteSpin(segment);
        }

        public void ConfirmSpin()
        {
            m_Machine.ConfirmSpin();
        }

        public void SubmitDrawing(byte[] drawing)
        {
            m_Machine.SubmitDrawing(drawing);
        }

        public bool SubmitGuess(string text)
        {
            return m_Machine.SubmitGuess(text);
        }

        public void SkipPresent()
        {
            m_Machine.SkipPresent();
        }

        public void CastVote(bool yes)
        {
            // One shared tap for the whole room.
            m_Machine.CastVote(0, yes);
        }

        public void ContinueAfterVote()
        {
            m_Machine.ContinueAfterVote();
        }

        public void ContinueRound()
        {
            m_Machine.ContinueRound();
        }

        public void Dispose()
        {
            m_Machine.PhaseChanged -= OnPhaseChanged;
            m_Machine.TurnExpired -= OnTurnExpired;
        }

        private void OnPhaseChanged(MatchPhase from, MatchPhase to)
        {
            Action<MatchPhase, MatchPhase> handler = PhaseChanged;
            if (handler != null)
            {
                handler(from, to);
            }
        }

        private void OnTurnExpired()
        {
            Action handler = TurnExpired;
            if (handler != null)
            {
                handler();
            }
        }
    }
}
