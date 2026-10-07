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

namespace Telewheel
{
    /// <summary>
    /// A computer player that takes a seat in an online room and plays like a slightly absent-minded
    /// person: it spins, "draws" something, guesses, advances the reveal and votes after a short pause.
    /// It goes through the same client and the same messages as a real player, so the practice mode
    /// (and the tests) exercise the real online path.
    /// </summary>
    public sealed class OnlineBot
    {
        private static readonly string[] Guesses =
        {
            "a cat in a hat", "something blue", "a very tall house", "banana", "pizza party",
            "rocket ship", "a funny tree", "the moon", "spaghetti", "a happy cloud",
        };

        private readonly OnlineMatchClient m_Client;
        private readonly TwRandom m_Random;
        private readonly Func<byte[]> m_Drawing;

        private Action m_Pending;
        private float m_Wait;
        private int m_PresentSeen = -1;

        /// <param name="port">The bot's connection to the host.</param>
        /// <param name="profile">The bot's name and icon.</param>
        /// <param name="seed">Makes the bot's pauses and choices repeatable.</param>
        /// <param name="drawing">What the bot hands in as its drawing; null or empty means a blank canvas.</param>
        public OnlineBot(INetClientPort port, PlayerProfile profile, int seed, Func<byte[]> drawing)
            : this(new OnlineMatchClient(port, profile), seed, drawing)
        {
        }

        /// <summary>Plays through a client that already exists (for auto-play in place of the person).</summary>
        public OnlineBot(OnlineMatchClient client, int seed, Func<byte[]> drawing)
        {
            m_Client = client;
            m_Random = new TwRandom(seed);
            m_Drawing = drawing;
            DrawEarliest = 0f;
            DrawLatest = 0.5f;
            m_Client.PhaseChanged += OnPhaseChanged;
        }

        public OnlineMatchClient Client
        {
            get { return m_Client; }
        }

        /// <summary>When true the bot stops acting (as a player who walked away from the headset).</summary>
        public bool Silent { get; set; }

        /// <summary>
        /// When in a draw turn the bot hands its drawing in, as fractions of the turn. Practice bots draw
        /// late, so a person's own drawing is there for them to copy and the person gets a real picture to guess.
        /// </summary>
        public float DrawEarliest { get; set; }

        public float DrawLatest { get; set; }

        public void Join()
        {
            m_Client.Join();
        }

        public void Tick(float dt)
        {
            m_Client.Tick(dt);
            if (Silent)
            {
                m_Pending = null;
                return;
            }
            if (m_Client.Phase == MatchPhase.Present && m_Client.PresentIndex != m_PresentSeen)
            {
                m_PresentSeen = m_Client.PresentIndex;
                if (m_Client.CanAdvancePresent)
                {
                    Plan(2f, 4f, m_Client.SkipPresent);
                }
            }
            if (m_Pending == null)
            {
                return;
            }
            m_Wait -= dt;
            if (m_Wait <= 0f)
            {
                Action action = m_Pending;
                m_Pending = null;
                action();
            }
        }

        private void OnPhaseChanged(MatchPhase from, MatchPhase to)
        {
            m_Pending = null;
            m_PresentSeen = -1;
            switch (to)
            {
                case MatchPhase.Spin:
                    Plan(1f, 3f, SpinThenConfirm);
                    break;
                case MatchPhase.Turn:
                    if (m_Client.TurnKind == StageKind.Draw)
                    {
                        float total = m_Client.Clock.Total;
                        Plan(Math.Max(3f, total * DrawEarliest), Math.Max(4f, total * DrawLatest), SubmitDrawing);
                    }
                    else
                    {
                        Plan(3f, Math.Max(4f, m_Client.Clock.Total * 0.5f), SubmitGuess);
                    }
                    break;
                case MatchPhase.Vote:
                    Plan(1f, 4f, Vote);
                    break;
            }
        }

        private void Plan(float minSeconds, float maxSeconds, Action action)
        {
            m_Wait = m_Random.NextRange(minSeconds, Math.Max(minSeconds, maxSeconds));
            m_Pending = action;
        }

        private void SpinThenConfirm()
        {
            m_Client.CompleteSpin(m_Random.NextInt(m_Client.WheelSegments));
            Plan(1f, 2f, m_Client.ConfirmSpin);
        }

        private void SubmitDrawing()
        {
            m_Client.SubmitDrawing(m_Drawing == null ? null : m_Drawing());
        }

        private void SubmitGuess()
        {
            m_Client.SubmitGuess(Guesses[m_Random.NextInt(Guesses.Length)]);
        }

        private void Vote()
        {
            m_Client.CastVote(m_Random.NextInt(3) != 0);
        }
    }
}
