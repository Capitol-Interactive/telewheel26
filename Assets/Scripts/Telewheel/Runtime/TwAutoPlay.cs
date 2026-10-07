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
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// Bots that play a whole match unattended: they spin, draw squiggles, type guesses and vote.
    /// It is for testing without a headset (--Telewheel.AutoPlay true), and drives the same game
    /// code a person would, so a full run exercises every screen and every Open Brush hook.
    /// </summary>
    public sealed class TwAutoPlay
    {
        private static readonly string[] GuessWords =
        {
            "banana", "a hat", "spatula", "my cat", "boat", "tree house", "a big cloud", "sandwich",
        };

        private readonly TwGame m_Game;
        private readonly TwRandom m_Random;
        private float m_Delay = 1f;
        private int m_Votes;
        private bool m_Finished;

        public TwAutoPlay(TwGame game, int seed)
        {
            m_Game = game;
            m_Random = new TwRandom(seed ^ 0xB07);
        }

        public bool Finished
        {
            get { return m_Finished; }
        }

        public void Tick(float dt)
        {
            if (m_Finished)
            {
                return;
            }
            m_Delay -= dt;
            if (m_Delay > 0f)
            {
                return;
            }
            IMatchSession session = m_Game.Session;
            if (session == null)
            {
                return;
            }
            switch (session.Phase)
            {
                case MatchPhase.Lobby:
                    PlayLobby();
                    break;
                case MatchPhase.Handoff:
                    session.ConfirmHandoff();
                    Pause(0.3f);
                    break;
                case MatchPhase.Spin:
                    PlaySpin(session);
                    break;
                case MatchPhase.Countdown:
                    session.SkipCountdown();
                    Pause(0.3f);
                    break;
                case MatchPhase.Turn:
                    PlayTurn(session);
                    break;
                case MatchPhase.Present:
                    if (session.CanAdvancePresent)
                    {
                        session.SkipPresent();
                    }
                    Pause(0.6f);
                    break;
                case MatchPhase.Vote:
                    session.CastVote(m_Votes++ % 2 == 0);
                    Pause(0.5f);
                    break;
                case MatchPhase.VoteResult:
                    session.ContinueAfterVote();
                    Pause(0.5f);
                    break;
                case MatchPhase.RoundEnd:
                    session.ContinueRound();
                    Pause(0.5f);
                    break;
                case MatchPhase.GameEnd:
                    Finish(session);
                    break;
            }
        }

        // In a practice room the host (this player) starts once the computer players have all arrived.
        private void PlayLobby()
        {
            OnlineSession online = m_Game.Online;
            if (online == null || !online.IsHost || online.Client.State != ClientState.Lobby)
            {
                return;
            }
            if (online.Room.CanStart && online.Client.Names.Count >= MatchSettings.MinPlayers + 2)
            {
                online.Room.Start();
                Pause(1f);
            }
        }

        private void PlaySpin(IMatchSession session)
        {
            if (session.LocalDone)
            {
                return;
            }
            var spin = m_Game.Screen as TwSpinScreen;
            if (spin == null)
            {
                return;
            }
            if (session.SpinWord == null)
            {
                spin.SpinNow();
                return;
            }
            session.ConfirmSpin();
            Pause(0.3f);
        }

        private void PlayTurn(IMatchSession session)
        {
            if (session.LocalDone)
            {
                return;
            }
            if (session.TurnKind == StageKind.Draw)
            {
                if (!m_Game.IsDrawTurn)
                {
                    return;
                }
                int strokes = 2 + m_Random.NextInt(3);
                for (int i = 0; i < strokes; i++)
                {
                    OpenBrushFacade.DrawSyntheticStroke(Squiggle(), RandomColor());
                }
                m_Game.Submit();
                Pause(1f);
            }
            else
            {
                session.SubmitGuess(GuessWords[m_Random.NextInt(GuessWords.Length)]);
                Pause(0.4f);
            }
        }

        private List<Vector3> Squiggle()
        {
            Vector3 c = TwLayout.DrawingCenterMeters;
            var points = new List<Vector3>();
            int count = 4 + m_Random.NextInt(4);
            for (int i = 0; i < count; i++)
            {
                points.Add(c + new Vector3(
                    m_Random.NextRange(-0.4f, 0.4f),
                    m_Random.NextRange(-0.3f, 0.3f),
                    m_Random.NextRange(-0.15f, 0.15f)));
            }
            return points;
        }

        private Color RandomColor()
        {
            uint hue = TwTokens.WheelHues[m_Random.NextInt(TwTokens.WheelHues.Length)];
            return TwGfx.ToColor(hue);
        }

        private void Pause(float seconds)
        {
            m_Delay = seconds;
        }

        private void Finish(IMatchSession session)
        {
            m_Finished = true;
            var scores = new List<string>();
            for (int i = 0; i < session.Scores.Count; i++)
            {
                scores.Add(session.NameOf(i) + " " + session.Scores[i]);
            }
            TwDirector.Instance.Log("AutoPlay finished: " + string.Join(", ", scores.ToArray()));
        }
    }
}
