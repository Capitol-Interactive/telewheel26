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
    /// A room this player is in: their client, and the room host when they are the host. It also
    /// carries whatever has to run alongside (the practice network and its bots, a transport's
    /// pump), so the game ticks one thing and leaves with one call.
    /// </summary>
    public sealed class OnlineSession : IDisposable
    {
        private readonly List<Action<float>> m_Tickers = new List<Action<float>>();
        private readonly List<Action> m_Cleanups = new List<Action>();
        private bool m_Left;

        public OnlineSession(OnlineMatchClient client, OnlineRoomHost room, string joinCode)
        {
            Client = client;
            Room = room;
            JoinCode = joinCode ?? string.Empty;
        }

        public OnlineMatchClient Client { get; private set; }

        /// <summary>The room host; null when this player joined someone else's room.</summary>
        public OnlineRoomHost Room { get; private set; }

        public string JoinCode { get; private set; }

        public bool IsHost
        {
            get { return Room != null; }
        }

        /// <summary>True for the local practice room with computer players (no network at all).</summary>
        public bool IsPractice { get; set; }

        public bool Left
        {
            get { return m_Left; }
        }

        /// <summary>Something to run every frame before the room and client (a transport pump, bots).</summary>
        public void AddTicker(Action<float> ticker)
        {
            m_Tickers.Add(ticker);
        }

        public void AddCleanup(Action cleanup)
        {
            m_Cleanups.Add(cleanup);
        }

        public void Tick(float dt)
        {
            if (m_Left)
            {
                return;
            }
            for (int i = 0; i < m_Tickers.Count; i++)
            {
                m_Tickers[i](dt);
            }
            if (m_Left)
            {
                // Something a ticker did (a callback) left the room.
                return;
            }
            if (Room != null)
            {
                Room.Tick(dt);
            }
            Client.Tick(dt);
        }

        /// <summary>Leaves the room. A host leaving ends it for everyone.</summary>
        public void Leave()
        {
            if (m_Left)
            {
                return;
            }
            m_Left = true;
            if (Room != null)
            {
                Room.Close(EndReason.HostEnded);
            }
            Client.Dispose();
            foreach (Action cleanup in m_Cleanups)
            {
                cleanup();
            }
            m_Cleanups.Clear();
            m_Tickers.Clear();
        }

        public void Dispose()
        {
            Leave();
        }
    }
}
