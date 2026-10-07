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
    /// Makes a whole online room inside the game with computer players, so the online screens and
    /// rules can be played and tested with no network: no Photon, no second headset. The computer
    /// players use the real client path over an in-memory network.
    /// </summary>
    public static class PracticeRoom
    {
        public const string PracticeCode = "PLAY";

        private static readonly string[] BotNames =
        {
            "Bleep", "Bloop", "Zap", "Pixel", "Nova", "Dot", "Fizz",
        };

        /// <summary>
        /// The local player hosts. Computer players join one at a time, and the player starts the match.
        /// </summary>
        public static OnlineSession Host(
            PlayerProfile me, MatchSettings settings, Func<ContentFilter, IList<string>> words,
            int bots, Func<byte[]> drawing, int seed)
        {
            var net = new LoopbackNetwork();
            var room = new OnlineRoomHost(net.Host, settings, me, words);
            var client = new OnlineMatchClient(room.LocalPort, me);
            var session = new OnlineSession(client, room, RoomCode.Generate(new TwRandom(seed))) { IsPractice = true };
            var crew = new Crew(net, bots, seed, drawing);
            session.AddTicker(dt => crew.Tick(dt));
            client.Join();
            return session;
        }

        /// <summary>
        /// A computer player hosts and the local player joins by code. The match starts by itself a few
        /// seconds after everyone has arrived, as a friend hosting it would.
        /// </summary>
        public static OnlineSession Join(
            PlayerProfile me, string code, MatchSettings settings, Func<ContentFilter, IList<string>> words,
            int bots, Func<byte[]> drawing, int seed)
        {
            var net = new LoopbackNetwork();
            var hostProfile = new PlayerProfile(BotNames[0], 0);
            var room = new OnlineRoomHost(net.Host, settings, hostProfile, words);
            var hostBot = new OnlineBot(room.LocalPort, hostProfile, seed, drawing)
            {
                DrawEarliest = 0.55f,
                DrawLatest = 0.85f,
            };
            hostBot.Join();
            INetClientPort port;
            net.Connect(out port);
            var client = new OnlineMatchClient(port, me);
            var session = new OnlineSession(client, null, RoomCode.Normalize(code)) { IsPractice = true };
            var crew = new Crew(net, bots - 1, seed, drawing);
            float sinceFull = 0f;
            session.AddTicker(dt =>
            {
                net.Deliver();
                hostBot.Tick(dt);
                crew.Tick(dt);
                room.Tick(dt);
                if (!room.InMatch && crew.Arrived >= crew.Total && room.Members.Count > crew.Total + 1)
                {
                    sinceFull += dt;
                    if (sinceFull >= 4f)
                    {
                        room.Start();
                    }
                }
                net.Deliver();
            });
            client.Join();
            return session;
        }

        // The computer players: they arrive one by one, then play.
        private sealed class Crew
        {
            private readonly LoopbackNetwork m_Net;
            private readonly int m_Seed;
            private readonly Func<byte[]> m_Drawing;
            private readonly List<OnlineBot> m_Bots = new List<OnlineBot>();
            private float m_Wait = 1.2f;

            public Crew(LoopbackNetwork net, int total, int seed, Func<byte[]> drawing)
            {
                m_Net = net;
                Total = Math.Max(0, total);
                m_Seed = seed;
                m_Drawing = drawing;
            }

            public int Total { get; private set; }

            public int Arrived
            {
                get { return m_Bots.Count; }
            }

            public void Tick(float dt)
            {
                m_Net.Deliver();
                if (m_Bots.Count < Total)
                {
                    m_Wait -= dt;
                    if (m_Wait <= 0f)
                    {
                        m_Wait = 1.2f;
                        AddBot();
                    }
                }
                foreach (OnlineBot bot in m_Bots)
                {
                    bot.Tick(dt);
                }
                m_Net.Deliver();
            }

            private void AddBot()
            {
                INetClientPort port;
                m_Net.Connect(out port);
                int index = m_Bots.Count;
                string name = BotNames[(index + 1) % BotNames.Length];
                var bot = new OnlineBot(port, new PlayerProfile(name, index + 1), m_Seed + 17 * (index + 1), m_Drawing)
                {
                    DrawEarliest = 0.55f,
                    DrawLatest = 0.85f,
                };
                m_Bots.Add(bot);
                bot.Join();
            }
        }
    }
}
