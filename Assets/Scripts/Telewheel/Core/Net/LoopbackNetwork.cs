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
    /// An in-memory network for tests and the practice mode: one host and any number of clients in the
    /// same process. Every message is encoded and decoded on the way, as it would be on a real wire, and
    /// nothing is delivered until <see cref="Deliver"/> is called, so the order of events is explicit.
    /// </summary>
    public sealed class LoopbackNetwork
    {
        private const int MaxDeliveriesPerCall = 1000000;

        private readonly Queue<Action> m_Queue = new Queue<Action>();
        private readonly Dictionary<int, ClientEnd> m_Clients = new Dictionary<int, ClientEnd>();
        private readonly HostEnd m_Host;
        private int m_NextPeer = 1;
        private bool m_HostAlive = true;

        public LoopbackNetwork()
        {
            m_Host = new HostEnd(this);
        }

        public INetHostPort Host
        {
            get { return m_Host; }
        }

        /// <summary>Messages waiting to be delivered.</summary>
        public int Pending
        {
            get { return m_Queue.Count; }
        }

        /// <summary>Connects a new player and returns their peer id.</summary>
        public int Connect(out INetClientPort port)
        {
            int peer = m_NextPeer++;
            var end = new ClientEnd(this, peer);
            m_Clients.Add(peer, end);
            port = end;
            return peer;
        }

        /// <summary>A player's connection drops. The host hears about it after the messages already sent.</summary>
        public void Disconnect(int peer)
        {
            ClientEnd end;
            if (!m_Clients.TryGetValue(peer, out end) || !end.Connected)
            {
                return;
            }
            end.Connected = false;
            m_Queue.Enqueue(() => m_Host.RaisePeerLeft(peer));
        }

        /// <summary>The host's connection drops: every connected player is told it is gone.</summary>
        public void LoseHost()
        {
            if (!m_HostAlive)
            {
                return;
            }
            m_HostAlive = false;
            foreach (ClientEnd end in m_Clients.Values)
            {
                ClientEnd target = end;
                m_Queue.Enqueue(() =>
                {
                    if (target.Connected)
                    {
                        target.Connected = false;
                        target.RaiseHostLost();
                    }
                });
            }
        }

        /// <summary>Delivers everything queued, and anything that sends cause, until the network is quiet.</summary>
        public int Deliver()
        {
            int delivered = 0;
            while (m_Queue.Count > 0 && delivered < MaxDeliveriesPerCall)
            {
                m_Queue.Dequeue()();
                delivered++;
            }
            return delivered;
        }

        private sealed class HostEnd : INetHostPort
        {
            private readonly LoopbackNetwork m_Net;

            public HostEnd(LoopbackNetwork net)
            {
                m_Net = net;
            }

            public event Action<int, NetMessage> FromPeer;

            public event Action<int> PeerLeft;

            public void SendToPeer(int peer, NetMessage message)
            {
                ClientEnd end;
                if (!m_Net.m_HostAlive || !m_Net.m_Clients.TryGetValue(peer, out end))
                {
                    return;
                }
                byte[] bytes = NetCodec.Encode(message);
                m_Net.m_Queue.Enqueue(() =>
                {
                    if (end.Connected)
                    {
                        end.RaiseFromHost(NetCodec.Decode(bytes));
                    }
                });
            }

            public void RaiseFromPeer(int peer, NetMessage message)
            {
                Action<int, NetMessage> handler = FromPeer;
                if (handler != null)
                {
                    handler(peer, message);
                }
            }

            public void RaisePeerLeft(int peer)
            {
                Action<int> handler = PeerLeft;
                if (handler != null)
                {
                    handler(peer);
                }
            }
        }

        private sealed class ClientEnd : INetClientPort
        {
            private readonly LoopbackNetwork m_Net;
            private readonly int m_Peer;

            public ClientEnd(LoopbackNetwork net, int peer)
            {
                m_Net = net;
                m_Peer = peer;
                Connected = true;
            }

            public bool Connected { get; set; }

            public event Action<NetMessage> FromHost;

            public event Action HostLost;

            public void SendToHost(NetMessage message)
            {
                if (!Connected || !m_Net.m_HostAlive)
                {
                    return;
                }
                byte[] bytes = NetCodec.Encode(message);
                m_Net.m_Queue.Enqueue(() =>
                {
                    if (Connected && m_Net.m_HostAlive)
                    {
                        m_Net.m_Host.RaiseFromPeer(m_Peer, NetCodec.Decode(bytes));
                    }
                });
            }

            public void RaiseFromHost(NetMessage message)
            {
                Action<NetMessage> handler = FromHost;
                if (handler != null)
                {
                    handler(message);
                }
            }

            public void RaiseHostLost()
            {
                Action handler = HostLost;
                if (handler != null)
                {
                    handler();
                }
            }
        }
    }
}
