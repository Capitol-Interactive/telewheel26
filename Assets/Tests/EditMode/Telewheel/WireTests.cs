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
using System.IO.Compression;
using System.Linq;
using NUnit.Framework;

namespace Telewheel.Tests
{
    public class WireTests
    {
        // ----- The envelope -----

        private static byte[] RandomBytes(int length, int seed)
        {
            var random = new TwRandom(seed);
            var bytes = new byte[length];
            for (int i = 0; i < length; i++)
            {
                bytes[i] = (byte)random.NextInt(256);
            }
            return bytes;
        }

        [Test]
        public void ASmallPayloadRoundTripsUncompressed()
        {
            byte[] payload = { 1, 2, 3, 4, 5 };
            byte[] frame = Envelope.Pack(Envelope.KindMessage, 7u, payload);
            Assert.AreEqual(6 + payload.Length, frame.Length);
            byte kind;
            uint sequence;
            byte[] back;
            Assert.IsTrue(Envelope.TryUnpack(frame, out kind, out sequence, out back));
            Assert.AreEqual(Envelope.KindMessage, kind);
            Assert.AreEqual(7u, sequence);
            CollectionAssert.AreEqual(payload, back);
        }

        [Test]
        public void ABigRepetitivePayloadIsCompressed()
        {
            byte[] payload = Enumerable.Repeat((byte)42, 200000).ToArray();
            byte[] frame = Envelope.Pack(Envelope.KindMessage, 1u, payload);
            Assert.Less(frame.Length, payload.Length / 10, "repetitive data should shrink a lot");
            byte kind;
            uint sequence;
            byte[] back;
            Assert.IsTrue(Envelope.TryUnpack(frame, out kind, out sequence, out back));
            CollectionAssert.AreEqual(payload, back);
        }

        [Test]
        public void RandomDataIsLeftAloneBecauseCompressionWouldNotHelp()
        {
            byte[] payload = RandomBytes(5000, 3);
            byte[] frame = Envelope.Pack(Envelope.KindMessage, 1u, payload);
            Assert.AreEqual(6 + payload.Length, frame.Length);
        }

        [Test]
        public void TheSequenceNumberKeepsAllItsBits()
        {
            byte kind;
            uint sequence;
            byte[] back;
            foreach (uint value in new[] { 0u, 1u, 255u, 256u, 65536u, 0x01020304u, uint.MaxValue })
            {
                Assert.IsTrue(Envelope.TryUnpack(Envelope.Pack(Envelope.KindHostHello, value, null), out kind, out sequence, out back));
                Assert.AreEqual(value, sequence);
                Assert.AreEqual(0, back.Length);
            }
        }

        [Test]
        public void GarbageIsRefusedNotThrown()
        {
            byte kind;
            uint sequence;
            byte[] back;
            Assert.IsFalse(Envelope.TryUnpack(null, out kind, out sequence, out back));
            Assert.IsFalse(Envelope.TryUnpack(new byte[0], out kind, out sequence, out back));
            Assert.IsFalse(Envelope.TryUnpack(new byte[] { 1, 0, 0 }, out kind, out sequence, out back), "too short");
            Assert.IsFalse(Envelope.TryUnpack(new byte[] { 99, 0, 0, 0, 0, 0 }, out kind, out sequence, out back), "unknown kind");
            Assert.IsFalse(Envelope.TryUnpack(new byte[] { 1, 0x80, 0, 0, 0, 0 }, out kind, out sequence, out back), "unknown flag");
            Assert.IsFalse(
                Envelope.TryUnpack(
                    new byte[] { 1, 1, 0, 0, 0, 0, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF, 0xFF }, out kind, out sequence, out back),
                "flagged compressed but is not");
        }

        [Test]
        public void ATinyFrameCannotUnpackIntoAHugePayload()
        {
            byte[] zeros = new byte[Envelope.MaxPayloadBytes + 1000000];
            byte[] packed;
            using (var output = new MemoryStream())
            {
                using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, true))
                {
                    brotli.Write(zeros, 0, zeros.Length);
                }
                packed = output.ToArray();
            }
            Assert.Less(packed.Length, 10000, "the attack frame is tiny next to the 9 MB it claims");
            var frame = new byte[6 + packed.Length];
            frame[0] = Envelope.KindMessage;
            frame[1] = 1;
            Buffer.BlockCopy(packed, 0, frame, 6, packed.Length);
            byte kind;
            uint sequence;
            byte[] back;
            Assert.IsFalse(Envelope.TryUnpack(frame, out kind, out sequence, out back));
        }

        // ----- Putting things back in order -----

        [Test]
        public void InOrderItemsPassStraightThrough()
        {
            var buffer = new SequenceBuffer<string>();
            CollectionAssert.AreEqual(new[] { "a" }, buffer.Accept(0, "a"));
            CollectionAssert.AreEqual(new[] { "b" }, buffer.Accept(1, "b"));
            Assert.AreEqual(2u, buffer.Next);
        }

        [Test]
        public void EarlyItemsWaitForTheOnesBeforeThem()
        {
            var buffer = new SequenceBuffer<string>();
            Assert.IsEmpty(buffer.Accept(2, "c"));
            Assert.IsEmpty(buffer.Accept(1, "b"));
            Assert.AreEqual(2, buffer.HeldCount);
            CollectionAssert.AreEqual(new[] { "a", "b", "c" }, buffer.Accept(0, "a"));
            Assert.AreEqual(0, buffer.HeldCount);
        }

        [Test]
        public void RepeatsAndOldItemsAreDropped()
        {
            var buffer = new SequenceBuffer<string>();
            buffer.Accept(0, "a");
            Assert.IsEmpty(buffer.Accept(0, "again"), "already delivered");
            buffer.Accept(2, "c");
            Assert.IsEmpty(buffer.Accept(2, "c again"), "already waiting");
            CollectionAssert.AreEqual(new[] { "b", "c" }, buffer.Accept(1, "b"));
        }

        [Test]
        public void AGapThatNeverFillsIsGivenUpOn()
        {
            var buffer = new SequenceBuffer<int>(4);
            var delivered = new List<int>();
            for (uint i = 1; i <= 6; i++)
            {
                delivered.AddRange(buffer.Accept(i, (int)i)); // Item 0 never comes.
            }
            CollectionAssert.AreEqual(new[] { 1, 2, 3, 4, 5, 6 }, delivered);
            Assert.AreEqual(0, buffer.HeldCount);
        }

        // ----- A link that shuffles and repeats -----

        private sealed class ShufflingNet
        {
            private readonly Random m_Random;
            private readonly List<Action> m_Pending = new List<Action>();
            private readonly double m_DuplicateChance;

            public ShufflingNet(int seed, double duplicateChance = 0.15)
            {
                m_Random = new Random(seed);
                m_DuplicateChance = duplicateChance;
            }

            public Node AddNode(int id)
            {
                return new Node(this, id);
            }

            public void Connect(Node guest, Node host)
            {
                host.RaisePeerJoined(guest.Id);
            }

            public void Disconnect(Node leaver, params Node[] others)
            {
                Deliver();
                foreach (Node other in others)
                {
                    other.RaisePeerLeft(leaver.Id);
                }
            }

            public void Enqueue(Node from, int to, Node target, byte[] bytes)
            {
                Action deliver = () => target.RaiseReceived(from.Id, (byte[])bytes.Clone());
                m_Pending.Add(deliver);
                if (m_Random.NextDouble() < m_DuplicateChance)
                {
                    m_Pending.Add(deliver);
                }
            }

            // Items come out of the first few places in the queue in any order: late, early, repeated.
            public void Deliver()
            {
                int guard = 0;
                while (m_Pending.Count > 0 && guard++ < 1000000)
                {
                    int index = m_Random.Next(Math.Min(m_Pending.Count, 8));
                    Action next = m_Pending[index];
                    m_Pending.RemoveAt(index);
                    next();
                }
            }
        }

        private sealed class Node : IByteLink
        {
            private readonly ShufflingNet m_Net;
            private readonly Dictionary<int, Node> m_Peers = new Dictionary<int, Node>();

            public Node(ShufflingNet net, int id)
            {
                m_Net = net;
                Id = id;
            }

            public int Id { get; private set; }

            public event Action<int, byte[]> Received;

            public event Action<int> PeerJoined;

            public event Action<int> PeerLeft;

            public void Know(Node other)
            {
                m_Peers[other.Id] = other;
            }

            public void Send(int peer, byte[] bytes)
            {
                Node target;
                if (m_Peers.TryGetValue(peer, out target))
                {
                    m_Net.Enqueue(this, peer, target, bytes);
                }
            }

            public void RaiseReceived(int from, byte[] bytes)
            {
                Action<int, byte[]> handler = Received;
                if (handler != null)
                {
                    handler(from, bytes);
                }
            }

            public void RaisePeerJoined(int peer)
            {
                Action<int> handler = PeerJoined;
                if (handler != null)
                {
                    handler(peer);
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

        private sealed class WireRoom
        {
            public readonly ShufflingNet Net;
            public readonly Node HostNode;
            public readonly OnlineRoomHost Room;
            public readonly List<OnlineBot> Bots = new List<OnlineBot>();
            public readonly List<Node> Nodes = new List<Node>();
            public readonly List<WireClient> Clients = new List<WireClient>();

            public WireRoom(int players, int seed, double duplicateChance = 0.15)
            {
                Net = new ShufflingNet(seed, duplicateChance);
                HostNode = Net.AddNode(100);
                var words = new List<string>();
                for (int i = 0; i < 80; i++)
                {
                    words.Add("word" + i);
                }
                Room = new OnlineRoomHost(
                    new WireHost(HostNode), new MatchSettings { Rounds = 1, Seed = seed },
                    new PlayerProfile("Host", 0), filter => words);
                var hostBot = new OnlineBot(Room.LocalPort, new PlayerProfile("Host", 0), seed, DrawingFor(0));
                hostBot.Join();
                Bots.Add(hostBot);
                for (int i = 1; i < players; i++)
                {
                    AddGuest(i, seed);
                }
                Step(0f);
            }

            public void AddGuest(int index, int seed)
            {
                Node node = Net.AddNode(index);
                node.Know(HostNode);
                HostNode.Know(node);
                var client = new WireClient(node);
                var bot = new OnlineBot(client, new PlayerProfile("P" + (index + 1), index), seed + index, DrawingFor(index));
                client.HostFound += peer => bot.Join();
                Nodes.Add(node);
                Clients.Add(client);
                Bots.Add(bot);
                Net.Connect(node, HostNode);
            }

            private static Func<byte[]> DrawingFor(int index)
            {
                // Some compressible drawings and some that are not, so both paths are used.
                return () => index % 2 == 0
                    ? Enumerable.Repeat((byte)index, 4000).ToArray()
                    : RandomBytes(2500, index);
            }

            public void Step(float dt)
            {
                Net.Deliver();
                Room.Tick(dt);
                foreach (OnlineBot bot in Bots)
                {
                    bot.Tick(dt);
                }
                Net.Deliver();
                Room.Tick(0f);
            }

            public bool RunUntil(Func<bool> done, float maxSeconds = 4000f)
            {
                for (float t = 0f; t < maxSeconds && !done(); t += 0.25f)
                {
                    Step(0.25f);
                }
                return done();
            }
        }

        [Test]
        public void GuestsLearnWhoTheHostIsFromItsGreeting()
        {
            var room = new WireRoom(3, 1);
            foreach (WireClient client in room.Clients)
            {
                Assert.AreEqual(100, client.HostPeer);
            }
            // Hellos arrive in any order over this link, so seats can be too; the host is always first.
            Assert.AreEqual("Host", room.Bots[1].Client.Names[0]);
            CollectionAssert.AreEquivalent(new[] { "Host", "P2", "P3" }, room.Bots[1].Client.Names.ToArray());
        }

        [TestCase(2, 11)]
        [TestCase(4, 12)]
        [TestCase(8, 13)]
        public void AWholeMatchSurvivesAShufflingRepeatingLink(int players, int seed)
        {
            var room = new WireRoom(players, seed);
            Assert.IsTrue(room.Room.Start(), room.Room.StartProblem);
            Assert.IsTrue(room.RunUntil(() => room.Bots.All(b => b.Client.Phase == MatchPhase.GameEnd)),
                "the match did not finish over the shuffling link");
            int[] scores = room.Room.Match.Scores.ToArray();
            foreach (OnlineBot bot in room.Bots)
            {
                CollectionAssert.AreEqual(scores, bot.Client.Scores.ToArray());
            }
            foreach (ChainState chain in room.Room.Match.Chains)
            {
                Assert.AreEqual(room.Room.Match.Planner.TurnsPerChain, chain.Entries.Count);
            }
        }

        [Test]
        public void TheHostGoingAwayEndsEveryGuestsMatch()
        {
            var room = new WireRoom(3, 5);
            room.Room.Start();
            room.Step(0f);
            room.Net.Disconnect(room.HostNode, room.Nodes.ToArray());
            room.Step(0f);
            foreach (OnlineBot bot in room.Bots.Skip(1))
            {
                Assert.AreEqual(ClientState.Ended, bot.Client.State);
                Assert.AreEqual(EndReason.HostEnded, bot.Client.EndedBecause);
            }
        }

        [Test]
        public void AGuestLeavingIsSeenByTheHost()
        {
            var room = new WireRoom(4, 6);
            room.Net.Disconnect(room.Nodes[0], room.HostNode);
            room.Step(0f);
            Assert.AreEqual(3, room.Room.Members.Count);
            CollectionAssert.AreEquivalent(new[] { "Host", "P3", "P4" }, room.Bots[0].Client.Names.ToArray());
        }

        [Test]
        public void OnceTheHostIsKnownOtherSendersAreIgnored()
        {
            var room = new WireRoom(2, 7);
            Node stranger = room.Net.AddNode(55);
            stranger.Know(room.Nodes[0]);
            room.Nodes[0].Know(stranger);
            int before = room.Bots[1].Client.LobbyVersion;
            // A well-formed LobbyState, but not from the host.
            byte[] forged = Envelope.Pack(
                Envelope.KindMessage, 0u,
                NetCodec.Encode(NetMessage.LobbyState(1, 5, ContentFilter.Raunchy, new[] { "X", "Y" }, new[] { 0, 0 }, "")));
            stranger.Send(room.Nodes[0].Id, forged);
            room.Step(0f);
            Assert.AreEqual(before, room.Bots[1].Client.LobbyVersion);
            Assert.AreNotEqual(5, room.Bots[1].Client.RoundCount);
        }

        [Test]
        public void GarbageOnTheLinkIsIgnored()
        {
            var room = new WireRoom(2, 8);
            room.HostNode.RaiseReceived(1, new byte[] { 1, 2, 3 });
            room.HostNode.RaiseReceived(1, Envelope.Pack(Envelope.KindMessage, 5u, new byte[] { 200, 1, 2 }));
            room.Nodes[0].RaiseReceived(100, new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
            room.Step(0f);
            Assert.AreEqual(2, room.Room.Members.Count);
            Assert.AreEqual(ClientState.Lobby, room.Bots[1].Client.State);
        }
    }
}
