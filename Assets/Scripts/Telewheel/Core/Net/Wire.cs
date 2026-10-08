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
using System.Security.Cryptography;

namespace Telewheel
{
    /// <summary>One frame after unpacking: what it is and what it carries.</summary>
    internal sealed class WireFrame
    {
        public byte Kind;
        public byte[] Payload;

        public static int SizeOf(WireFrame frame)
        {
            return frame.Payload == null ? 0 : frame.Payload.Length;
        }
    }

    /// <summary>A frame waiting to go out. It is packed once, when the link is first ready for it.</summary>
    internal sealed class Outgoing
    {
        public byte Kind;
        public uint Sequence;
        public byte[] Payload;
        public byte[] Frame;
    }

    /// <summary>
    /// Frames waiting for a link that cannot take them yet (it is not in the room, say), sent in order as
    /// soon as it can. Never more than a few hundred: a link that refuses that many is gone.
    /// </summary>
    internal sealed class Outbox
    {
        public const int MaxFrames = 256;

        private readonly Queue<Outgoing> m_Queue = new Queue<Outgoing>();
        private bool m_Flushing;

        public int Count
        {
            get { return m_Queue.Count; }
        }

        /// <summary>Returns false if the box is full.</summary>
        public bool Add(Outgoing item)
        {
            if (m_Queue.Count >= MaxFrames)
            {
                return false;
            }
            m_Queue.Enqueue(item);
            return true;
        }

        public void Flush(IByteLink link, int peer, ulong token)
        {
            // A link may deliver at once, and the answer may send more: that goes on the end of this loop.
            if (m_Flushing)
            {
                return;
            }
            m_Flushing = true;
            try
            {
                while (m_Queue.Count > 0)
                {
                    int sender = link.LocalPeer;
                    if (sender < 0)
                    {
                        return;
                    }
                    Outgoing next = m_Queue.Peek();
                    if (next.Frame == null)
                    {
                        next.Frame = Envelope.Pack(next.Kind, next.Sequence, sender, token, next.Payload);
                        next.Payload = null;
                    }
                    if (!link.Send(peer, next.Frame))
                    {
                        return;
                    }
                    m_Queue.Dequeue();
                }
            }
            finally
            {
                m_Flushing = false;
            }
        }
    }

    /// <summary>Secrets that prove a frame is from the player it says it is from.</summary>
    internal static class WireTokens
    {
        public static ulong Next()
        {
            var bytes = new byte[8];
            ulong token = 0;
            while (token == 0)
            {
                try
                {
                    using (RandomNumberGenerator random = RandomNumberGenerator.Create())
                    {
                        random.GetBytes(bytes);
                    }
                }
                catch (Exception)
                {
                    // No system source: mix what there is. A room code is the real gate; this is the second.
                    new Random(Guid.NewGuid().GetHashCode() ^ Environment.TickCount).NextBytes(bytes);
                }
                token = BitConverter.ToUInt64(bytes, 0);
            }
            return token;
        }
    }

    /// <summary>
    /// The host's connections over a byte link. Frames are ordered, compressed and retried here.
    ///
    /// The link cannot be trusted to say who sent something (on Photon's Shared mode it names the
    /// receiver), so every frame carries its sender and a secret token. The host gives each new arrival a
    /// token in its greeting, privately, and from then on a frame counts as that player's only if it
    /// carries the right token. Anyone else in the room can see neither the greeting nor the token.
    /// </summary>
    public sealed class WireHost : INetHostPort, IDisposable
    {
        /// <summary>How long to wait for a new arrival to answer before greeting them again.</summary>
        public const float RegreetSeconds = 2f;

        /// <summary>How many more times to greet an arrival who never answers.</summary>
        public const int MaxRegreets = 5;

        /// <summary>How long a player may be missing from the room before they count as gone.</summary>
        public const float AbsentSeconds = 3f;

        private const int MaxHeldFrames = 64;
        private const long MaxHeldBytes = 16L * 1024 * 1024;

        private readonly IByteLink m_Link;
        private readonly Func<ulong> m_Tokens;
        private readonly Dictionary<int, Guest> m_Guests = new Dictionary<int, Guest>();
        private readonly List<int> m_Scratch = new List<int>();
        private int m_Rejected;

        public WireHost(IByteLink link, Func<ulong> tokens = null)
        {
            m_Link = link;
            m_Tokens = tokens ?? WireTokens.Next;
            m_Link.Received += OnReceived;
            m_Link.PeerJoined += OnPeerJoined;
            m_Link.PeerLeft += OnPeerLeft;
        }

        public event Action<int, NetMessage> FromPeer;

        public event Action<int> PeerLeft;

        /// <summary>How many frames were turned away: not frames at all, or not from who they said.</summary>
        public int RejectedFrames
        {
            get { return m_Rejected; }
        }

        public void SendToPeer(int peer, NetMessage message)
        {
            Guest guest;
            if (!m_Guests.TryGetValue(peer, out guest))
            {
                return;
            }
            if (!Enqueue(guest, Envelope.KindMessage, NetCodec.Encode(message)))
            {
                Drop(peer);
                return;
            }
            Flush(guest);
        }

        /// <summary>
        /// Call every frame. Sends what the link could not take before, greets again anyone who arrived
        /// but has not answered (a transport may drop the first greeting if the newcomer was not quite
        /// ready), and notices players who have vanished without the link saying so.
        /// </summary>
        public void Tick(float dt)
        {
            m_Scratch.Clear();
            m_Scratch.AddRange(m_Guests.Keys);
            for (int i = 0; i < m_Scratch.Count; i++)
            {
                int peer = m_Scratch[i];
                Guest guest;
                if (!m_Guests.TryGetValue(peer, out guest))
                {
                    continue;
                }
                if (m_Link.IsPresent(peer))
                {
                    guest.Absent = 0f;
                }
                else
                {
                    guest.Absent += dt;
                    if (guest.Absent >= AbsentSeconds)
                    {
                        Drop(peer);
                        continue;
                    }
                }
                Flush(guest);
                if (!guest.Answered)
                {
                    Regreet(guest, dt);
                }
            }
        }

        public void Dispose()
        {
            m_Link.Received -= OnReceived;
            m_Link.PeerJoined -= OnPeerJoined;
            m_Link.PeerLeft -= OnPeerLeft;
        }

        private void Regreet(Guest guest, float dt)
        {
            guest.Waited += dt;
            if (guest.Waited < RegreetSeconds)
            {
                return;
            }
            guest.Waited = 0f;
            guest.Repeats++;
            if (guest.Repeats > MaxRegreets)
            {
                guest.Answered = true; // Given up: they are in the room but are not playing Telewheel.
                return;
            }
            // The very same frame, with the same sequence number: if the first one did arrive this is a
            // repeat that gets dropped, and if it did not, the gap is filled. (If it has not even left yet,
            // the outbox is still holding it.)
            if (guest.Greeting.Frame != null && guest.Outbox.Count == 0)
            {
                m_Link.Send(guest.Peer, guest.Greeting.Frame);
            }
        }

        private bool Enqueue(Guest guest, byte kind, byte[] payload)
        {
            var item = new Outgoing { Kind = kind, Sequence = guest.SendSequence++, Payload = payload };
            if (kind == Envelope.KindHostHello)
            {
                guest.Greeting = item;
            }
            return guest.Outbox.Add(item);
        }

        private void Flush(Guest guest)
        {
            guest.Outbox.Flush(m_Link, guest.Peer, guest.Token);
        }

        private void OnPeerJoined(int peer)
        {
            if (m_Guests.ContainsKey(peer))
            {
                // The transport reuses ids, and a leave may have gone unnoticed: that was somebody else.
                Drop(peer);
            }
            var guest = new Guest
            {
                Peer = peer,
                Token = m_Tokens(),
                Receive = new SequenceBuffer<WireFrame>(MaxHeldFrames, MaxHeldBytes, WireFrame.SizeOf),
            };
            m_Guests[peer] = guest;
            Enqueue(guest, Envelope.KindHostHello, null);
            Flush(guest);
        }

        private void OnPeerLeft(int peer)
        {
            Drop(peer);
        }

        private void Drop(int peer)
        {
            if (!m_Guests.Remove(peer))
            {
                return;
            }
            Action<int> handler = PeerLeft;
            if (handler != null)
            {
                handler(peer);
            }
        }

        private void OnReceived(byte[] bytes)
        {
            byte kind;
            uint sequence;
            int sender;
            ulong token;
            byte[] payload;
            if (!Envelope.TryUnpack(bytes, out kind, out sequence, out sender, out token, out payload)
                || kind != Envelope.KindMessage)
            {
                m_Rejected++;
                return;
            }
            Guest guest;
            if (!m_Guests.TryGetValue(sender, out guest) || token != guest.Token)
            {
                m_Rejected++;
                return;
            }
            guest.Answered = true; // They have heard us: no need to greet again.
            foreach (WireFrame frame in guest.Receive.Accept(sequence, new WireFrame { Kind = kind, Payload = payload }))
            {
                Guest current;
                if (!m_Guests.TryGetValue(sender, out current) || current != guest)
                {
                    return; // They left while we were handing their messages on.
                }
                NetMessage message = Decode(frame.Payload);
                Action<int, NetMessage> handler = FromPeer;
                if (message != null && handler != null)
                {
                    handler(sender, message);
                }
            }
        }

        private sealed class Guest
        {
            public int Peer;
            public ulong Token;
            public uint SendSequence;
            public Outbox Outbox = new Outbox();
            public Outgoing Greeting;
            public SequenceBuffer<WireFrame> Receive;
            public bool Answered;
            public float Waited;
            public int Repeats;
            public float Absent;
        }

        // Whatever arrives from the network is untrusted, and a decoder can fail in more ways than one.
        internal static NetMessage Decode(byte[] payload)
        {
            try
            {
                return NetCodec.Decode(payload);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>
    /// A player's connection over a byte link. It learns who the host is, and its own secret token, from
    /// the host's greeting; listens only to frames that carry that host and that token; and reports when
    /// the host goes away.
    /// </summary>
    public sealed class WireClient : INetClientPort, IDisposable
    {
        /// <summary>How long the host may be missing from the room before it counts as gone.</summary>
        public const float AbsentSeconds = 3f;

        private const int MaxHeldFrames = 64;
        private const long MaxHeldBytes = 16L * 1024 * 1024;
        private const int MaxUnanswered = 16;

        private readonly IByteLink m_Link;
        private readonly Outbox m_Outbox = new Outbox();
        private readonly List<Outgoing> m_Unanswered = new List<Outgoing>();
        private SequenceBuffer<WireFrame> m_Receive;
        private int m_Host = -1;
        private ulong m_Token;
        private uint m_SendSequence;
        private bool m_Answered;
        private bool m_Lost;
        private float m_Absent;
        private int m_Rejected;

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

        /// <summary>How many frames were turned away: not frames at all, or not from the host.</summary>
        public int RejectedFrames
        {
            get { return m_Rejected; }
        }

        public void SendToHost(NetMessage message)
        {
            if (m_Host < 0 || m_Lost)
            {
                return;
            }
            var item = new Outgoing
            {
                Kind = Envelope.KindMessage,
                Sequence = m_SendSequence++,
                Payload = NetCodec.Encode(message),
            };
            if (!m_Answered && m_Unanswered.Count < MaxUnanswered)
            {
                m_Unanswered.Add(item);
            }
            if (!m_Outbox.Add(item))
            {
                Lose();
                return;
            }
            m_Outbox.Flush(m_Link, m_Host, m_Token);
        }

        /// <summary>
        /// Call every frame. Sends what the link could not take before and notices a host that has
        /// vanished without the link saying so.
        /// </summary>
        public void Tick(float dt)
        {
            if (m_Host < 0 || m_Lost)
            {
                return;
            }
            if (m_Link.IsPresent(m_Host))
            {
                m_Absent = 0f;
            }
            else
            {
                m_Absent += dt;
                if (m_Absent >= AbsentSeconds)
                {
                    Lose();
                    return;
                }
            }
            m_Outbox.Flush(m_Link, m_Host, m_Token);
        }

        public void Dispose()
        {
            m_Link.Received -= OnReceived;
            m_Link.PeerLeft -= OnPeerLeft;
        }

        private void OnPeerLeft(int peer)
        {
            if (peer == m_Host)
            {
                Lose();
            }
        }

        private void Lose()
        {
            if (m_Lost)
            {
                return;
            }
            m_Lost = true;
            Action handler = HostLost;
            if (handler != null)
            {
                handler();
            }
        }

        private void OnReceived(byte[] bytes)
        {
            byte kind;
            uint sequence;
            int sender;
            ulong token;
            byte[] payload;
            if (m_Lost)
            {
                return;
            }
            if (!Envelope.TryUnpack(bytes, out kind, out sequence, out sender, out token, out payload))
            {
                m_Rejected++;
                return;
            }
            if (m_Host < 0)
            {
                // The first greeting wins. Only the host has a secret to give, but a stranger in the room
                // who greets first would be believed; the room code is what keeps strangers out.
                if (kind != Envelope.KindHostHello || sequence != 0u || sender < 0 || token == 0)
                {
                    m_Rejected++;
                    return;
                }
                m_Host = sender;
                m_Token = token;
                m_Receive = new SequenceBuffer<WireFrame>(MaxHeldFrames, MaxHeldBytes, WireFrame.SizeOf);
            }
            else if (sender != m_Host || token != m_Token)
            {
                m_Rejected++;
                return;
            }
            else if (kind == Envelope.KindHostHello)
            {
                // Greeted again: the host has not heard from us, so what we sent first never reached it.
                // The same frames again (not new ones: the host is waiting for number 0).
                if (!m_Answered)
                {
                    Resend();
                }
                return;
            }
            foreach (WireFrame frame in m_Receive.Accept(sequence, new WireFrame { Kind = kind, Payload = payload }))
            {
                if (m_Lost)
                {
                    return;
                }
                Handle(frame);
            }
        }

        private void Resend()
        {
            foreach (Outgoing item in m_Unanswered)
            {
                if (item.Frame != null)
                {
                    m_Link.Send(m_Host, item.Frame);
                }
            }
        }

        private void Handle(WireFrame frame)
        {
            if (frame.Kind == Envelope.KindHostHello)
            {
                Action<int> found = HostFound;
                if (found != null)
                {
                    found(m_Host);
                }
                return;
            }
            if (!m_Answered)
            {
                m_Answered = true;
                m_Unanswered.Clear();
            }
            NetMessage message = WireHost.Decode(frame.Payload);
            Action<NetMessage> handler = FromHost;
            if (message != null && handler != null)
            {
                handler(message);
            }
        }
    }
}
