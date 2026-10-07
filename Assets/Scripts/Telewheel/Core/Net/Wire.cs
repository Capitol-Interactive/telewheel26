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
using System.IO;

namespace Telewheel
{
    /// <summary>One frame after unpacking: what it is and what it carries.</summary>
    internal sealed class WireFrame
    {
        public byte Kind;
        public byte[] Payload;
    }

    /// <summary>
    /// The host's connections over a byte link: frames, orders and (de)compresses messages, and tells
    /// each new arrival that this device is the host. A message's sender is whoever the link says sent
    /// it; nothing inside a message can claim otherwise.
    /// </summary>
    public sealed class WireHost : INetHostPort, IDisposable
    {
        private readonly IByteLink m_Link;
        private readonly Dictionary<int, uint> m_SendSequence = new Dictionary<int, uint>();
        private readonly Dictionary<int, SequenceBuffer<WireFrame>> m_Receive =
            new Dictionary<int, SequenceBuffer<WireFrame>>();
        private readonly Dictionary<int, Greeting> m_Unanswered = new Dictionary<int, Greeting>();

        /// <summary>How long to wait for a new arrival to answer before greeting them again.</summary>
        public const float RegreetSeconds = 2f;

        /// <summary>How many more times to greet an arrival who never answers.</summary>
        public const int MaxRegreets = 5;

        public WireHost(IByteLink link)
        {
            m_Link = link;
            m_Link.Received += OnReceived;
            m_Link.PeerJoined += OnPeerJoined;
            m_Link.PeerLeft += OnPeerLeft;
        }

        public event Action<int, NetMessage> FromPeer;

        public event Action<int> PeerLeft;

        public void SendToPeer(int peer, NetMessage message)
        {
            Send(peer, Envelope.KindMessage, NetCodec.Encode(message));
        }

        /// <summary>
        /// Greets again anyone who arrived but has not answered. A transport may drop the first greeting if
        /// the newcomer was not quite ready for it, and without it they would wait for ever.
        /// </summary>
        public void Tick(float dt)
        {
            List<int> due = null;
            foreach (KeyValuePair<int, Greeting> entry in m_Unanswered)
            {
                entry.Value.Waited += dt;
                if (entry.Value.Waited >= RegreetSeconds)
                {
                    if (due == null)
                    {
                        due = new List<int>();
                    }
                    due.Add(entry.Key);
                }
            }
            if (due == null)
            {
                return;
            }
            foreach (int peer in due)
            {
                Greeting greeting = m_Unanswered[peer];
                greeting.Waited = 0f;
                greeting.Repeats++;
                if (greeting.Repeats > MaxRegreets)
                {
                    m_Unanswered.Remove(peer);
                }
                else
                {
                    // The very same frame, with the same sequence number: if the first one did arrive this
                    // is a repeat that gets dropped, and if it did not, the gap is filled.
                    m_Link.Send(peer, greeting.Frame);
                }
            }
        }

        public void Dispose()
        {
            m_Link.Received -= OnReceived;
            m_Link.PeerJoined -= OnPeerJoined;
            m_Link.PeerLeft -= OnPeerLeft;
        }

        private void Send(int peer, byte kind, byte[] payload)
        {
            uint sequence;
            m_SendSequence.TryGetValue(peer, out sequence);
            m_SendSequence[peer] = sequence + 1;
            m_Link.Send(peer, Envelope.Pack(kind, sequence, payload));
        }

        private void OnPeerJoined(int peer)
        {
            // A fresh start for this id, which the transport may have used before.
            m_SendSequence.Remove(peer);
            m_Receive.Remove(peer);
            var greeting = new Greeting { Frame = Envelope.Pack(Envelope.KindHostHello, 0u, null) };
            m_SendSequence[peer] = 1u; // The greeting is number 0 in this peer's sequence.
            m_Unanswered[peer] = greeting;
            m_Link.Send(peer, greeting.Frame);
        }

        private void OnPeerLeft(int peer)
        {
            m_SendSequence.Remove(peer);
            m_Receive.Remove(peer);
            m_Unanswered.Remove(peer);
            Action<int> handler = PeerLeft;
            if (handler != null)
            {
                handler(peer);
            }
        }

        private void OnReceived(int peer, byte[] bytes)
        {
            byte kind;
            uint sequence;
            byte[] payload;
            if (!Envelope.TryUnpack(bytes, out kind, out sequence, out payload) || kind != Envelope.KindMessage)
            {
                return;
            }
            m_Unanswered.Remove(peer); // They have heard us: no need to greet again.
            SequenceBuffer<WireFrame> buffer;
            if (!m_Receive.TryGetValue(peer, out buffer))
            {
                buffer = new SequenceBuffer<WireFrame>();
                m_Receive[peer] = buffer;
            }
            foreach (WireFrame frame in buffer.Accept(sequence, new WireFrame { Kind = kind, Payload = payload }))
            {
                NetMessage message = Decode(frame.Payload);
                Action<int, NetMessage> handler = FromPeer;
                if (message != null && handler != null)
                {
                    handler(peer, message);
                }
            }
        }

        private sealed class Greeting
        {
            public byte[] Frame;
            public float Waited;
            public int Repeats;
        }

        internal static NetMessage Decode(byte[] payload)
        {
            try
            {
                return NetCodec.Decode(payload);
            }
            catch (InvalidDataException)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A player's connection over a byte link. It learns who the host is from the host's greeting,
    /// listens only to that device from then on, and reports when it goes away.
    /// </summary>
    public sealed class WireClient : INetClientPort, IDisposable
    {
        private readonly IByteLink m_Link;
        private readonly Dictionary<int, SequenceBuffer<WireFrame>> m_Receive =
            new Dictionary<int, SequenceBuffer<WireFrame>>();
        private int m_Host = -1;
        private uint m_SendSequence;
        private bool m_Lost;

        public WireClient(IByteLink link)
        {
            m_Link = link;
            m_Link.Received += OnReceived;
            m_Link.PeerLeft += OnPeerLeft;
        }

        public event Action<NetMessage> FromHost;

        public event Action HostLost;

        /// <summary>The host's greeting arrived (its peer id): the player can introduce themselves now.</summary>
        public event Action<int> HostFound;

        /// <summary>The host's peer id, or -1 until it has greeted this player.</summary>
        public int HostPeer
        {
            get { return m_Host; }
        }

        public void SendToHost(NetMessage message)
        {
            if (m_Host < 0 || m_Lost)
            {
                return;
            }
            m_Link.Send(m_Host, Envelope.Pack(Envelope.KindMessage, m_SendSequence++, NetCodec.Encode(message)));
        }

        public void Dispose()
        {
            m_Link.Received -= OnReceived;
            m_Link.PeerLeft -= OnPeerLeft;
        }

        private void OnPeerLeft(int peer)
        {
            m_Receive.Remove(peer);
            if (peer == m_Host && !m_Lost)
            {
                m_Lost = true;
                Action handler = HostLost;
                if (handler != null)
                {
                    handler();
                }
            }
        }

        private void OnReceived(int peer, byte[] bytes)
        {
            byte kind;
            uint sequence;
            byte[] payload;
            if (m_Lost || !Envelope.TryUnpack(bytes, out kind, out sequence, out payload))
            {
                return;
            }
            if (m_Host >= 0 && peer != m_Host)
            {
                return;
            }
            SequenceBuffer<WireFrame> buffer;
            if (!m_Receive.TryGetValue(peer, out buffer))
            {
                buffer = new SequenceBuffer<WireFrame>();
                m_Receive[peer] = buffer;
            }
            foreach (WireFrame frame in buffer.Accept(sequence, new WireFrame { Kind = kind, Payload = payload }))
            {
                Handle(peer, frame);
            }
        }

        private void Handle(int peer, WireFrame frame)
        {
            if (frame.Kind == Envelope.KindHostHello)
            {
                if (m_Host < 0)
                {
                    m_Host = peer;
                    Action<int> found = HostFound;
                    if (found != null)
                    {
                        found(peer);
                    }
                }
                return;
            }
            NetMessage message = WireHost.Decode(frame.Payload);
            Action<NetMessage> handler = FromHost;
            if (m_Host >= 0 && peer == m_Host && message != null && handler != null)
            {
                handler(message);
            }
        }
    }
}
