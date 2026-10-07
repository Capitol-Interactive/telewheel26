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
    public enum StageKind
    {
        Spin,
        Draw,
        Guess,
    }

    /// <summary>One thing one player does: spin for a word, or take a Draw/Guess turn on a chain.</summary>
    public readonly struct Stage
    {
        public readonly StageKind Kind;
        public readonly int Player;

        /// <summary>The chain being worked on. A chain is named after the player who spun its word.</summary>
        public readonly int Chain;

        /// <summary>Index of the Draw/Guess turn within the chain, or -1 for a spin.</summary>
        public readonly int Turn;

        public Stage(StageKind kind, int player, int chain, int turn)
        {
            Kind = kind;
            Player = player;
            Chain = chain;
            Turn = turn;
        }

        public override string ToString()
        {
            return Kind + "(player " + Player + ", chain " + Chain + ", turn " + Turn + ")";
        }
    }

    /// <summary>
    /// Works out who handles which chain on which turn, using the product spec's linear passing:
    /// player i + 1 gets the drawing from player i, wrapping at the end.
    ///
    /// Every player owns one chain (the word they spun). With an even number of players the owner
    /// draws their own word on turn 0 and the turns alternate Draw, Guess, ... With an odd number
    /// the owner does not draw; the word is passed to the next player, who draws it. Either way a
    /// chain ends on a Guess made by the player just before its owner, so the finished chain
    /// returns to the person who spun the word.
    /// </summary>
    public sealed class ChainPlanner
    {
        private readonly int m_PlayerCount;

        public ChainPlanner(int playerCount)
        {
            if (playerCount < 2)
            {
                throw new ArgumentOutOfRangeException("playerCount", "Need at least two players");
            }
            m_PlayerCount = playerCount;
        }

        public int PlayerCount
        {
            get { return m_PlayerCount; }
        }

        public bool IsEven
        {
            get { return m_PlayerCount % 2 == 0; }
        }

        /// <summary>How many Draw/Guess turns each chain has.</summary>
        public int TurnsPerChain
        {
            get { return IsEven ? m_PlayerCount : m_PlayerCount - 1; }
        }

        /// <summary>
        /// Positions between a chain's owner and the player on its first turn: 0 when the owner draws
        /// their own word, 1 when the word is passed on first.
        /// </summary>
        private int StartOffset
        {
            get { return IsEven ? 0 : 1; }
        }

        public StageKind KindOfTurn(int turn)
        {
            CheckTurn(turn);
            return turn % 2 == 0 ? StageKind.Draw : StageKind.Guess;
        }

        /// <summary>The player who takes <paramref name="turn"/> on <paramref name="chain"/>.</summary>
        public int PlayerForTurn(int chain, int turn)
        {
            CheckChain(chain);
            CheckTurn(turn);
            return (chain + StartOffset + turn) % m_PlayerCount;
        }

        /// <summary>The chain that <paramref name="player"/> works on during <paramref name="turn"/>.</summary>
        public int ChainForTurn(int player, int turn)
        {
            CheckPlayer(player);
            CheckTurn(turn);
            int chain = (player - StartOffset - turn) % m_PlayerCount;
            return chain < 0 ? chain + m_PlayerCount : chain;
        }

        /// <summary>The player who makes the last guess of a chain.</summary>
        public int FinalGuesser(int chain)
        {
            return PlayerForTurn(chain, TurnsPerChain - 1);
        }

        /// <summary>
        /// The order a single headset is passed around in Pass &amp; Play. Everyone spins first; with
        /// an even player count each player draws straight after their own spin (it is their word,
        /// so there is nothing to hand over). Then each remaining turn goes around the table in
        /// player order.
        /// </summary>
        public IReadOnlyList<Stage> BuildSequentialSchedule()
        {
            var stages = new List<Stage>();
            for (int player = 0; player < m_PlayerCount; player++)
            {
                stages.Add(new Stage(StageKind.Spin, player, player, -1));
                if (IsEven)
                {
                    stages.Add(new Stage(StageKind.Draw, player, player, 0));
                }
            }
            for (int turn = IsEven ? 1 : 0; turn < TurnsPerChain; turn++)
            {
                for (int player = 0; player < m_PlayerCount; player++)
                {
                    stages.Add(new Stage(KindOfTurn(turn), player, ChainForTurn(player, turn), turn));
                }
            }
            return stages;
        }

        private void CheckPlayer(int player)
        {
            if (player < 0 || player >= m_PlayerCount)
            {
                throw new ArgumentOutOfRangeException("player");
            }
        }

        private void CheckChain(int chain)
        {
            if (chain < 0 || chain >= m_PlayerCount)
            {
                throw new ArgumentOutOfRangeException("chain");
            }
        }

        private void CheckTurn(int turn)
        {
            if (turn < 0 || turn >= TurnsPerChain)
            {
                throw new ArgumentOutOfRangeException("turn");
            }
        }
    }
}
