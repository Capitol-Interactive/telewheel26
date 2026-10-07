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
    /// <summary>
    /// What a screen needs to know about the match, whether it is Pass &amp; Play on one headset or
    /// online with a host somewhere else. The draw, guess, reveal and vote screens only read this.
    /// </summary>
    public interface IMatchView
    {
        MatchPhase Phase { get; }

        /// <summary>1-based round number.</summary>
        int Round { get; }

        int RoundCount { get; }
        bool IsFinalRound { get; }
        int PlayerCount { get; }
        int WheelSegments { get; }

        /// <summary>The player at this headset right now, or -1 when nobody in particular is.</summary>
        int ActiveSeat { get; }

        string NameOf(int seat);

        ChainPlanner Planner { get; }
        TurnClock Clock { get; }

        /// <summary>The Draw or Guess turn in progress (0-based, same for every player online).</summary>
        int TurnIndex { get; }

        StageKind TurnKind { get; }

        /// <summary>The word the wheel landed on, once it has stopped (until confirmed).</summary>
        string SpinWord { get; }

        /// <summary>What to draw this turn: the spun word or the previous guess. Null on a guess turn.</summary>
        string PromptText { get; }

        /// <summary>The drawing to guess this turn. Null on a draw turn.</summary>
        byte[] PromptDrawing { get; }

        /// <summary>Which chain is being revealed or voted on, and who owns it.</summary>
        int PresentChain { get; }

        int PresentOwner { get; }

        /// <summary>Item 0 is the spun word; item i is the chain's turn i - 1.</summary>
        int PresentIndex { get; }

        PresentItem CurrentPresentItem { get; }

        /// <summary>The original word of the chain being voted on.</summary>
        string PresentedWord { get; }

        /// <summary>True when this player may move the reveal on (online only the chain owner can).</summary>
        bool CanAdvancePresent { get; }

        bool LastVoteLanded { get; }
        IReadOnlyList<int> Scores { get; }
        IList<int> Leaders { get; }

        /// <summary>True when the match is played over a network, so other people may still be working.</summary>
        bool IsOnline { get; }

        /// <summary>This player has finished the current step and is waiting for the others.</summary>
        bool LocalDone { get; }

        /// <summary>Seats that have not finished the current step (empty when nobody is being waited for).</summary>
        IReadOnlyList<int> WaitingFor { get; }
    }

    /// <summary>A match a player can act in: the view plus the things the player can do.</summary>
    public interface IMatchSession : IMatchView, IDisposable
    {
        event Action<MatchPhase, MatchPhase> PhaseChanged;

        /// <summary>The turn's clock ran out before this player handed anything in.</summary>
        event Action TurnExpired;

        /// <summary>Advances time. Call every frame with the (possibly scaled) frame time.</summary>
        void Tick(float dt);

        void ConfirmHandoff();

        /// <summary>Ends the draw countdown early (Pass &amp; Play only; online the host keeps the clock).</summary>
        void SkipCountdown();

        void CompleteSpin(int segment);
        void ConfirmSpin();
        void SubmitDrawing(byte[] drawing);

        /// <summary>Returns false (and stays put) when the text is empty and the turn has not timed out.</summary>
        bool SubmitGuess(string text);

        void SkipPresent();
        void CastVote(bool yes);
        void ContinueAfterVote();
        void ContinueRound();
    }

    public static class ScoreMath
    {
        /// <summary>The seats with the top score (more than one on a tie).</summary>
        public static IList<int> Leaders(IReadOnlyList<int> scores)
        {
            var leaders = new List<int>();
            int best = int.MinValue;
            for (int i = 0; i < scores.Count; i++)
            {
                if (scores[i] > best)
                {
                    best = scores[i];
                    leaders.Clear();
                }
                if (scores[i] == best)
                {
                    leaders.Add(i);
                }
            }
            return leaders;
        }
    }
}
