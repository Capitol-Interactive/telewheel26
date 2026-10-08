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

        private static byte[] Header(byte kind, byte flags, int extra = 0)
        {
            var frame = new byte[Envelope.HeaderBytes + extra];
            frame[0] = kind;
            frame[1] = flags;
            return frame;
        }

        // ----- The envelope -----

        [Test]
        public void ASmallPayloadRoundTripsUncompressed()
        {
            byte[] payload = { 1, 2, 3, 4, 5 };
            byte[] frame = Envelope.Pack(Envelope.KindMessage, 7u, 3, 99UL, payload);
            Assert.AreEqual(Envelope.HeaderBytes + payload.Length, frame.Length);
            byte kind;
            uint sequence;
            int sender;
            ulong token;
            byte[] back;
            Assert.IsTrue(Envelope.TryUnpack(frame, out kind, out sequence, out sender, out token, out back));
            Assert.AreEqual(Envelope.KindMessage, kind);
            Assert.AreEqual(7u, sequence);
            Assert.AreEqual(3, sender);
            Assert.AreEqual(99UL, token);
            CollectionAssert.AreEqual(payload, back);
        }

        [Test]
        public void ABigRepetitivePayloadIsCompressed()
        {
            byte[] payload = Enumerable.Repeat((byte)42, 200000).ToArray();
            byte[] frame = Envelope.Pack(Envelope.KindMessage, 1u, 1, 1UL, payload);
            Assert.Less(frame.Length, payload.Length / 10, "repetitive data should shrink a lot");
            byte kind;
            uint sequence;
            int sender;
            ulong token;
            byte[] back;
            Assert.IsTrue(Envelope.TryUnpack(frame, out kind, out sequence, out sender, out token, out back));
            CollectionAssert.AreEqual(payload, back);
        }

        [Test]
        public void RandomDataIsLeftAloneBecauseCompressionWouldNotHelp()
        {
            byte[] payload = RandomBytes(5000, 3);
            byte[] frame = Envelope.Pack(Envelope.KindMessage, 1u, 1, 1UL, payload);
            Assert.AreEqual(Envelope.HeaderBytes + payload.Length, frame.Length);
        }

        [Test]
        public void TheSequenceSenderAndTokenKeepAllTheirBits()
        {
            byte kind;
            uint sequence;
            int sender;
            ulong token;
            byte[] back;
            foreach (uint value in new[] { 0u, 1u, 255u, 256u, 65536u, 0x01020304u, uint.MaxValue })
            {
                Assert.IsTrue(Envelope.TryUnpack(
                    Envelope.Pack(Envelope.KindHostHello, value, 0, 0UL, null), out kind, out sequence, out sender, out token, out back));
                Assert.AreEqual(value, sequence);
                Assert.AreEqual(0, back.Length);
            }
            foreach (int id in new[] { 0, 1, 255, 70000, -1, int.MaxValue, int.MinValue })
            {
                Assert.IsTrue(Envelope.TryUnpack(
                    Envelope.Pack(Envelope.KindMessage, 0u, id, 5UL, null), out kind, out sequence, out sender, out token, out back));
                Assert.AreEqual(id, sender);
            }
            foreach (ulong secret in new[] { 0UL, 1UL, 0x0102030405060708UL, 0xFFFFFFFF00000000UL, ulong.MaxValue })
            {
                Assert.IsTrue(Envelope.TryUnpack(
                    Envelope.Pack(Envelope.KindMessage, 0u, 4, secret, null), out kind, out sequence, out sender, out token, out back));
                Assert.AreEqual(secret, token);
            }
        }

        [Test]
        public void GarbageIsRefusedNotThrown()
        {
            byte kind;
            uint sequence;
            int sender;
            ulong token;
            byte[] back;
            Assert.IsFalse(Envelope.TryUnpack(null, out kind, out sequence, out sender, out token, out back));
            Assert.IsFalse(Envelope.TryUnpack(new byte[0], out kind, out sequence, out sender, out token, out back));
            Assert.IsFalse(Envelope.TryUnpack(new byte[] { 1, 0, 0 }, out kind, out sequence, out sender, out token, out back), "too short");
            Assert.IsFalse(Envelope.TryUnpack(Header(99, 0), out kind, out sequence, out sender, out token, out back), "unknown kind");
            Assert.IsFalse(Envelope.TryUnpack(Header(1, 0x80), out kind, out sequence, out sender, out token, out back), "unknown flag");
            byte[] notCompressed = Header(1, 1, 8);
            for (int i = Envelope.HeaderBytes; i < notCompressed.Length; i++)
            {
                notCompressed[i] = 0xFF;
            }
            Assert.IsFalse(
                Envelope.TryUnpack(notCompressed, out kind, out sequence, out sender, out token, out back),
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
            byte[] frame = Header(Envelope.KindMessage, 1, packed.Length);
            Buffer.BlockCopy(packed, 0, frame, Envelope.HeaderBytes, packed.Length);
            byte kind;
            uint sequence;
            int sender;
            ulong token;
            byte[] back;
            Assert.IsFalse(Envelope.TryUnpack(frame, out kind, out sequence, out sender, out token, out back));
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

        [Test]
        public void TooManyWaitingBytesGiveUpOnTheGapToo()
        {
            var buffer = new SequenceBuffer<byte[]>(1000, 100, item => item.Length);
            var delivered = new List<byte[]>();
            for (uint i = 1; i <= 3; i++)
            {
                delivered.AddRange(buffer.Accept(i, new byte[30])); // 90 bytes waiting for item 0.
            }
            Assert.AreEqual(0, delivered.Count);
            Assert.AreEqual(90L, buffer.HeldBytes);
            delivered.AddRange(buffer.Accept(4, new byte[30])); // 120 bytes: past the limit.
            Assert.AreEqual(4, delivered.Count);
            Assert.AreEqual(0L, buffer.HeldBytes);
        }

        // ----- A link that can be told what to do -----

        private sealed class FakeLink : IByteLink
        {
            public readonly List<KeyValuePair<int, byte[]>> Sent = new List<KeyValuePair<int, byte[]>>();
            public readonly HashSet<int> Present = new HashSet<int>();
            public bool Accept = true;
            public int Local = 1;

            public event Action<byte[]> Received;

            public event Action<int> PeerJoined;

            public event Action<int> PeerLeft;

            public int LocalPeer
            {
                get { return Local; }
            }

            public bool IsPresent(int peer)
            {
                return Present.Contains(peer);
            }

            public bool Send(int peer, byte[] bytes)
            {
                if (!Accept)
                {
                    return false;
                }
                Sent.Add(new KeyValuePair<int, byte[]>(peer, bytes));
                return true;
            }

            public void Receive(byte[] bytes)
            {
                Received(bytes);
            }

            public void Join(int peer)
            {
                Present.Add(peer);
                PeerJoined(peer);
            }

            public void Leave(int peer)
            {
                Present.Remove(peer);
                PeerLeft(peer);
            }
        }

        private sealed class Frame
        {
            public byte Kind;
            public uint Sequence;
            public int Sender;
            public ulong Token;
            public byte[] Payload;
            public int To;
        }

        private static Frame Open(KeyValuePair<int, byte[]> sent)
        {
            var frame = new Frame { To = sent.Key };
            Assert.IsTrue(Envelope.TryUnpack(
                sent.Value, out frame.Kind, out frame.Sequence, out frame.Sender, out frame.Token, out frame.Payload));
            return frame;
        }

        private static byte[] Hello(uint sequence, int sender, ulong token)
        {
            return Envelope.Pack(
                Envelope.KindMessage, sequence, sender, token, NetCodec.Encode(NetMessage.Hello("Zed", 2, OnlineRoomHost.ProtocolVersion)));
        }

        private static Func<ulong> Tokens(params ulong[] values)
        {
            int next = 0;
            return () => values[next++];
        }

        // ----- The host, one guest at a time -----

        private sealed class HostRig
        {
            public readonly FakeLink Link = new FakeLink();
            public readonly WireHost Wire;
            public readonly List<KeyValuePair<int, NetMessage>> Heard = new List<KeyValuePair<int, NetMessage>>();
            public readonly List<int> Left = new List<int>();

            public HostRig(params ulong[] tokens)
            {
                Wire = new WireHost(Link, Tokens(tokens));
                Wire.FromPeer += (peer, message) => Heard.Add(new KeyValuePair<int, NetMessage>(peer, message));
                Wire.PeerLeft += peer => Left.Add(peer);
            }
        }

        [Test]
        public void TheGreetingCarriesTheHostsIdAndTheGuestsSecret()
        {
            var rig = new HostRig(42UL);
            rig.Link.Join(7);
            Assert.AreEqual(1, rig.Link.Sent.Count);
            Frame greeting = Open(rig.Link.Sent[0]);
            Assert.AreEqual(7, greeting.To);
            Assert.AreEqual(Envelope.KindHostHello, greeting.Kind);
            Assert.AreEqual(0u, greeting.Sequence);
            Assert.AreEqual(1, greeting.Sender);
            Assert.AreEqual(42UL, greeting.Token);
        }

        [Test]
        public void AFrameCountsAsAGuestsOnlyWithTheirSecret()
        {
            var rig = new HostRig(42UL);
            rig.Link.Join(7);
            rig.Link.Receive(Hello(0u, 7, 41UL));
            Assert.AreEqual(0, rig.Heard.Count, "wrong secret");
            Assert.AreEqual(1, rig.Wire.RejectedFrames);
            rig.Link.Receive(Hello(0u, 7, 0UL));
            Assert.AreEqual(0, rig.Heard.Count, "no secret");
            rig.Link.Receive(Hello(0u, 7, 42UL));
            Assert.AreEqual(1, rig.Heard.Count);
            Assert.AreEqual(7, rig.Heard[0].Key);
            Assert.AreEqual(NetKind.Hello, rig.Heard[0].Value.Kind);
        }

        [Test]
        public void AGuestCannotSpeakForAnother()
        {
            var rig = new HostRig(42UL, 43UL);
            rig.Link.Join(7);
            rig.Link.Join(8);
            rig.Link.Receive(Hello(0u, 8, 42UL)); // Guest 7's secret, claiming to be guest 8.
            Assert.AreEqual(0, rig.Heard.Count);
            rig.Link.Receive(Hello(0u, 9, 42UL)); // Nobody in the room has that id.
            Assert.AreEqual(0, rig.Heard.Count);
            rig.Link.Receive(Hello(0u, 8, 43UL));
            Assert.AreEqual(8, rig.Heard.Single().Key);
        }

        [Test]
        public void AFormerOccupantsSecretStopsWorkingWhenTheIdIsReused()
        {
            var rig = new HostRig(42UL, 43UL);
            rig.Link.Join(7);
            rig.Link.Leave(7);
            rig.Link.Join(7);
            rig.Link.Receive(Hello(0u, 7, 42UL));
            Assert.AreEqual(0, rig.Heard.Count, "the old secret");
            rig.Link.Receive(Hello(0u, 7, 43UL));
            Assert.AreEqual(1, rig.Heard.Count);
        }

        [Test]
        public void ALeaveThatWentUnnoticedBeforeTheNextJoinCountsAsALeave()
        {
            var rig = new HostRig(42UL, 43UL);
            rig.Link.Join(7);
            rig.Link.Join(7); // The same id again, and no leave in between.
            CollectionAssert.AreEqual(new[] { 7 }, rig.Left);
            Frame second = Open(rig.Link.Sent.Last());
            Assert.AreEqual(43UL, second.Token, "the newcomer gets a secret of their own");
            rig.Link.Receive(Hello(0u, 7, 42UL));
            Assert.AreEqual(0, rig.Heard.Count);
        }

        [Test]
        public void APlayerWhoVanishedWithoutAnEventIsNoticed()
        {
            var rig = new HostRig(42UL);
            rig.Link.Join(7);
            rig.Link.Present.Remove(7); // The link never says they left.
            rig.Wire.Tick(1f);
            Assert.AreEqual(0, rig.Left.Count, "a moment's absence is not a leave");
            rig.Wire.Tick(WireHost.AbsentSeconds);
            CollectionAssert.AreEqual(new[] { 7 }, rig.Left);
        }

        [Test]
        public void ABriefAbsenceIsForgiven()
        {
            var rig = new HostRig(42UL);
            rig.Link.Join(7);
            rig.Link.Present.Remove(7);
            rig.Wire.Tick(2f);
            rig.Link.Present.Add(7);
            rig.Wire.Tick(2f);
            rig.Link.Present.Remove(7);
            rig.Wire.Tick(2f);
            Assert.AreEqual(0, rig.Left.Count);
        }

        [Test]
        public void FramesWaitInOrderUntilTheLinkWillTakeThem()
        {
            var rig = new HostRig(42UL);
            rig.Link.Accept = false;
            rig.Link.Join(7);
            rig.Wire.SendToPeer(7, NetMessage.PlayerLeft(1));
            rig.Wire.SendToPeer(7, NetMessage.PlayerLeft(2));
            Assert.AreEqual(0, rig.Link.Sent.Count);
            rig.Link.Accept = true;
            rig.Wire.Tick(0f);
            Frame[] frames = rig.Link.Sent.Select(Open).ToArray();
            CollectionAssert.AreEqual(new[] { 0u, 1u, 2u }, frames.Select(f => f.Sequence).ToArray());
            Assert.AreEqual(Envelope.KindHostHello, frames[0].Kind);
            Assert.AreEqual(2, NetCodec.Decode(frames[2].Payload).A);
        }

        [Test]
        public void ALinkThatNeverTakesFramesLosesThePlayerInsteadOfFillingMemory()
        {
            var rig = new HostRig(42UL);
            rig.Link.Accept = false;
            rig.Link.Join(7);
            for (int i = 0; i < 300 && rig.Left.Count == 0; i++)
            {
                rig.Wire.SendToPeer(7, NetMessage.PlayerLeft(i % 8));
            }
            CollectionAssert.AreEqual(new[] { 7 }, rig.Left);
            rig.Wire.SendToPeer(7, NetMessage.PlayerLeft(1)); // Nothing happens, and nothing throws.
        }

        [Test]
        public void NothingIsSentUntilThisDeviceKnowsItsOwnId()
        {
            var rig = new HostRig(42UL);
            rig.Link.Local = -1;
            rig.Link.Join(7);
            Assert.AreEqual(0, rig.Link.Sent.Count);
            rig.Link.Local = 5;
            rig.Wire.Tick(0f);
            Assert.AreEqual(1, rig.Link.Sent.Count);
            Assert.AreEqual(5, Open(rig.Link.Sent[0]).Sender);
        }

        [Test]
        public void ADecoderThatThrowsOnJunkDoesNotTakeTheHostDown()
        {
            var rig = new HostRig(42UL);
            rig.Link.Join(7);
            var random = new Random(77);
            uint sequence = 0;
            for (int round = 0; round < 600; round++)
            {
                var junk = new byte[random.Next(0, 40)];
                random.NextBytes(junk);
                if (junk.Length > 0 && round % 3 == 0)
                {
                    junk[0] = (byte)random.Next(0, 40); // Often a real message kind, then nonsense.
                }
                rig.Link.Receive(Envelope.Pack(Envelope.KindMessage, sequence++, 7, 42UL, junk));
            }
            rig.Heard.Clear();
            rig.Link.Receive(Hello(sequence, 7, 42UL));
            Assert.AreEqual(1, rig.Heard.Count, "the host still hears the guest after all that");
        }

        [Test]
        public void ANewcomerWhoNeverAnswersIsGreetedAFewTimesAndThenLeftAlone()
        {
            var rig = new HostRig(42UL);
            rig.Link.Join(7);
            for (int i = 0; i < 40; i++)
            {
                rig.Wire.Tick(WireHost.RegreetSeconds);
            }
            Assert.AreEqual(1 + WireHost.MaxRegreets, rig.Link.Sent.Count);
            Assert.IsTrue(rig.Link.Sent.All(sent => Open(sent).Sequence == 0u), "every repeat is the same frame");
            CollectionAssert.AreEqual(rig.Link.Sent[0].Value, rig.Link.Sent[3].Value);
        }

        // ----- A guest, one frame at a time -----

        private sealed class GuestRig
        {
            public readonly FakeLink Link = new FakeLink();
            public readonly WireClient Wire;
            public readonly List<NetMessage> Heard = new List<NetMessage>();
            public readonly List<int> Found = new List<int>();
            public int Lost;

            public GuestRig()
            {
                Link.Local = 4;
                Link.Present.Add(3);
                Wire = new WireClient(Link);
                Wire.FromHost += message => Heard.Add(message);
                Wire.HostFound += peer => Found.Add(peer);
                Wire.HostLost += () => Lost++;
            }

            public byte[] Greeting(int sender, ulong token, uint sequence = 0u)
            {
                return Envelope.Pack(Envelope.KindHostHello, sequence, sender, token, null);
            }

            public byte[] FromHost(uint sequence, int sender, ulong token, NetMessage message)
            {
                return Envelope.Pack(Envelope.KindMessage, sequence, sender, token, NetCodec.Encode(message));
            }
        }

        [Test]
        public void TheGreetingMakesItsSenderTheHost()
        {
            var rig = new GuestRig();
            rig.Link.Receive(rig.Greeting(3, 9UL));
            Assert.AreEqual(3, rig.Wire.HostPeer);
            CollectionAssert.AreEqual(new[] { 3 }, rig.Found);
        }

        [Test]
        public void OnlyTheFirstGreetingCounts()
        {
            var rig = new GuestRig();
            rig.Link.Receive(rig.Greeting(3, 9UL));
            rig.Link.Receive(rig.Greeting(5, 10UL));
            Assert.AreEqual(3, rig.Wire.HostPeer);
            Assert.AreEqual(1, rig.Found.Count);
            Assert.AreEqual(1, rig.Wire.RejectedFrames);
        }

        [Test]
        public void AGreetingThatIsNotAnHonestOneIsRefused()
        {
            var rig = new GuestRig();
            rig.Link.Receive(rig.Greeting(3, 9UL, 5u));
            rig.Link.Receive(rig.Greeting(3, 0UL));
            rig.Link.Receive(rig.Greeting(-1, 9UL));
            rig.Link.Receive(rig.FromHost(0u, 3, 9UL, NetMessage.PlayerLeft(1))); // Not a greeting.
            Assert.AreEqual(-1, rig.Wire.HostPeer);
            Assert.AreEqual(4, rig.Wire.RejectedFrames);
        }

        [Test]
        public void MessagesNeedTheHostsIdAndSecret()
        {
            var rig = new GuestRig();
            rig.Link.Receive(rig.Greeting(3, 9UL));
            rig.Link.Receive(rig.FromHost(1u, 3, 8UL, NetMessage.PlayerLeft(1)));
            rig.Link.Receive(rig.FromHost(1u, 5, 9UL, NetMessage.PlayerLeft(1)));
            Assert.AreEqual(0, rig.Heard.Count);
            rig.Link.Receive(rig.FromHost(1u, 3, 9UL, NetMessage.PlayerLeft(2)));
            Assert.AreEqual(2, rig.Heard.Single().A);
        }

        [Test]
        public void TheGuestSpeaksWithItsOwnIdAndTheSecret()
        {
            var rig = new GuestRig();
            rig.Link.Receive(rig.Greeting(3, 9UL));
            rig.Wire.SendToHost(NetMessage.Hello("Zed", 2, OnlineRoomHost.ProtocolVersion));
            Frame frame = Open(rig.Link.Sent.Single());
            Assert.AreEqual(3, frame.To);
            Assert.AreEqual(4, frame.Sender);
            Assert.AreEqual(9UL, frame.Token);
            Assert.AreEqual(0u, frame.Sequence);
        }

        [Test]
        public void ARepeatedGreetingMakesAGuestWhoWasNotHeardSendItsFirstFramesAgain()
        {
            var rig = new GuestRig();
            rig.Link.Receive(rig.Greeting(3, 9UL));
            rig.Wire.SendToHost(NetMessage.Hello("Zed", 2, OnlineRoomHost.ProtocolVersion));
            rig.Link.Receive(rig.Greeting(3, 9UL)); // The host has heard nothing: our first frame was lost.
            Assert.AreEqual(2, rig.Link.Sent.Count);
            CollectionAssert.AreEqual(rig.Link.Sent[0].Value, rig.Link.Sent[1].Value, "the same frame, sequence number and all");
            Assert.AreEqual(1, rig.Found.Count, "found only once");
        }

        [Test]
        public void ARepeatedGreetingAfterTheHostHasAnsweredChangesNothing()
        {
            var rig = new GuestRig();
            rig.Link.Receive(rig.Greeting(3, 9UL));
            rig.Wire.SendToHost(NetMessage.Hello("Zed", 2, OnlineRoomHost.ProtocolVersion));
            rig.Link.Receive(rig.FromHost(1u, 3, 9UL, NetMessage.PlayerLeft(1))); // The host answered.
            rig.Link.Receive(rig.Greeting(3, 9UL));
            Assert.AreEqual(1, rig.Link.Sent.Count);
        }

        [Test]
        public void AHostWhoVanishedWithoutAnEventIsNoticed()
        {
            var rig = new GuestRig();
            rig.Link.Receive(rig.Greeting(3, 9UL));
            rig.Link.Present.Remove(3);
            rig.Wire.Tick(1f);
            Assert.AreEqual(0, rig.Lost);
            rig.Wire.Tick(WireClient.AbsentSeconds);
            Assert.AreEqual(1, rig.Lost);
            rig.Wire.Tick(10f);
            Assert.AreEqual(1, rig.Lost, "reported once");
        }

        [Test]
        public void TheHostLeavingIsReportedOnceAndOnlyForTheHost()
        {
            var rig = new GuestRig();
            rig.Link.Receive(rig.Greeting(3, 9UL));
            rig.Link.Leave(8);
            Assert.AreEqual(0, rig.Lost);
            rig.Link.Leave(3);
            rig.Link.Leave(3);
            Assert.AreEqual(1, rig.Lost);
        }

        [Test]
        public void AGuestWaitsToSpeakUntilItHasAnId()
        {
            var rig = new GuestRig();
            rig.Link.Local = -1;
            rig.Link.Receive(rig.Greeting(3, 9UL));
            rig.Wire.SendToHost(NetMessage.Hello("Zed", 2, OnlineRoomHost.ProtocolVersion));
            Assert.AreEqual(0, rig.Link.Sent.Count);
            rig.Link.Local = 6;
            rig.Wire.Tick(0f);
            Assert.AreEqual(6, Open(rig.Link.Sent.Single()).Sender);
        }

        [Test]
        public void AGuestWhoseLinkNeverTakesItsFramesGivesUpOnTheHost()
        {
            var rig = new GuestRig();
            rig.Link.Receive(rig.Greeting(3, 9UL));
            rig.Link.Accept = false;
            for (int i = 0; i < 300 && rig.Lost == 0; i++)
            {
                rig.Wire.SendToHost(NetMessage.Hello("Zed", 2, OnlineRoomHost.ProtocolVersion));
            }
            Assert.AreEqual(1, rig.Lost);
        }

        // ----- A link that shuffles and repeats -----

        private sealed class ShufflingNet
        {
            private readonly Random m_Random;
            private readonly List<Action> m_Pending = new List<Action>();
            private readonly HashSet<int> m_Present = new HashSet<int>();
            private readonly double m_DuplicateChance;

            public ShufflingNet(int seed, double duplicateChance = 0.15)
            {
                m_Random = new Random(seed);
                m_DuplicateChance = duplicateChance;
            }

            public Node AddNode(int id)
            {
                m_Present.Add(id);
                return new Node(this, id);
            }

            public bool IsPresent(int id)
            {
                return m_Present.Contains(id);
            }

            public void Connect(Node guest, Node host)
            {
                host.RaisePeerJoined(guest.Id);
            }

            public void Disconnect(Node leaver, params Node[] others)
            {
                Deliver();
                m_Present.Remove(leaver.Id);
                foreach (Node other in others)
                {
                    other.RaisePeerLeft(leaver.Id);
                }
            }

            // Gone from the room without anyone being told.
            public void Vanish(Node node)
            {
                m_Present.Remove(node.Id);
            }

            // The receiver is never told who sent it, as on Photon's Shared mode.
            public void Enqueue(Node target, byte[] bytes)
            {
                Action deliver = () => target.RaiseReceived((byte[])bytes.Clone());
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

            /// <summary>Frames this node will silently lose before it sends normally (a transport dropping early data).</summary>
            public int DropOutgoing { get; set; }

            /// <summary>While true the transport takes nothing (it is not ready yet).</summary>
            public bool Refuse { get; set; }

            /// <summary>How many frames this node has sent in all, lost ones included.</summary>
            public int SentCount { get; private set; }

            public event Action<byte[]> Received;

            public event Action<int> PeerJoined;

            public event Action<int> PeerLeft;

            public int LocalPeer
            {
                get { return Id; }
            }

            public bool IsPresent(int peer)
            {
                return m_Net.IsPresent(peer);
            }

            public void Know(Node other)
            {
                m_Peers[other.Id] = other;
            }

            public bool Send(int peer, byte[] bytes)
            {
                Node target;
                if (Refuse || !m_Peers.TryGetValue(peer, out target) || !m_Net.IsPresent(peer))
                {
                    return false;
                }
                SentCount++;
                if (DropOutgoing > 0)
                {
                    DropOutgoing--;
                    return true; // Taken by the transport, and then lost on the way.
                }
                m_Net.Enqueue(target, bytes);
                return true;
            }

            public void RaiseReceived(byte[] bytes)
            {
                Action<byte[]> handler = Received;
                if (handler != null)
                {
                    handler(bytes);
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
            public const ulong TokenBase = 0xABCD000000000000UL;

            public readonly ShufflingNet Net;
            public readonly Node HostNode;
            public readonly WireHost Wire;
            public readonly OnlineRoomHost Room;
            public readonly List<OnlineBot> Bots = new List<OnlineBot>();
            public readonly List<Node> Nodes = new List<Node>();
            public readonly List<WireClient> Clients = new List<WireClient>();
            private ulong m_Issued;

            public WireRoom(int players, int seed, double duplicateChance = 0.15)
            {
                Net = new ShufflingNet(seed, duplicateChance);
                HostNode = Net.AddNode(100);
                var words = new List<string>();
                for (int i = 0; i < 80; i++)
                {
                    words.Add("word" + i);
                }
                Wire = new WireHost(HostNode, () => TokenBase + ++m_Issued);
                Room = new OnlineRoomHost(
                    Wire, new MatchSettings { Rounds = 1, Seed = seed },
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

            /// <summary>The secret the host hands the nth guest to arrive (1 for the first).</summary>
            public static ulong TokenFor(int arrival)
            {
                return TokenBase + (ulong)arrival;
            }

            public void AddGuest(int index, int seed, int dropFirstFrames = 0)
            {
                Node node = Net.AddNode(index);
                node.Know(HostNode);
                HostNode.Know(node);
                node.DropOutgoing = dropFirstFrames;
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
                Wire.Tick(dt);
                foreach (WireClient client in Clients)
                {
                    client.Tick(dt);
                }
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
        public void ForgedFramesFromAStrangerInTheRoomChangeNothing()
        {
            var room = new WireRoom(3, 7);
            Node stranger = room.Net.AddNode(55);
            stranger.Know(room.HostNode);
            stranger.Know(room.Nodes[0]);
            int before = room.Bots[1].Client.LobbyVersion;
            byte[] lobby = NetCodec.Encode(
                NetMessage.LobbyState(1, 5, ContentFilter.Raunchy, new[] { "X", "Y" }, new[] { 0, 0 }, ""));
            // To a guest, as the host but without the guest's secret (and with a guess at it).
            stranger.Send(room.Nodes[0].Id, Envelope.Pack(Envelope.KindMessage, 1u, room.HostNode.Id, 0UL, lobby));
            stranger.Send(room.Nodes[0].Id, Envelope.Pack(Envelope.KindMessage, 1u, room.HostNode.Id, WireRoom.TokenFor(2), lobby));
            // To the host, as the first guest, with another guest's secret and with none.
            stranger.Send(room.HostNode.Id, Envelope.Pack(
                Envelope.KindMessage, 9u, room.Nodes[0].Id, WireRoom.TokenFor(2), NetCodec.Encode(NetMessage.Hello("Evil", 3, OnlineRoomHost.ProtocolVersion))));
            stranger.Send(room.HostNode.Id, Envelope.Pack(
                Envelope.KindMessage, 9u, 55, WireRoom.TokenFor(1), NetCodec.Encode(NetMessage.Hello("Evil", 3, OnlineRoomHost.ProtocolVersion))));
            room.Step(0f);
            Assert.AreEqual(before, room.Bots[1].Client.LobbyVersion);
            Assert.AreNotEqual(5, room.Bots[1].Client.RoundCount);
            Assert.AreEqual(3, room.Room.Members.Count);
            CollectionAssert.DoesNotContain(room.Bots[0].Client.Names.ToArray(), "Evil");
            Assert.AreEqual(2, room.Wire.RejectedFrames);
            Assert.AreEqual(2, room.Clients[0].RejectedFrames);
        }

        [Test]
        public void AGreetingThatWasLostIsSentAgain()
        {
            var room = new WireRoom(1, 21); // Just the host.
            room.HostNode.DropOutgoing = 1; // The first greeting vanishes, as if the newcomer was not ready.
            room.AddGuest(1, 21);
            room.Step(0f);
            Assert.AreEqual(-1, room.Clients[0].HostPeer, "the guest has not heard from the host yet");
            Assert.IsTrue(room.RunUntil(() => room.Clients[0].HostPeer == 100, 10f), "the host should greet again");
            Assert.IsTrue(room.RunUntil(() => room.Bots[1].Client.State == ClientState.Lobby, 10f));
        }

        [Test]
        public void AGuestWhoseFirstHelloWasLostIsHeardOnceTheHostGreetsAgain()
        {
            var room = new WireRoom(1, 31, 0.0);
            room.AddGuest(1, 31, 1); // The guest's first frame, its hello, vanishes.
            room.Step(0f);
            Assert.AreEqual(100, room.Clients[0].HostPeer);
            Assert.AreEqual(1, room.Room.Members.Count, "the host has not heard the hello");
            Assert.IsTrue(room.RunUntil(() => room.Room.Members.Count == 2, 10f), "the hello should be sent again");
            Assert.IsTrue(room.RunUntil(() => room.Bots[1].Client.State == ClientState.Lobby, 10f));
        }

        [Test]
        public void ARoomThatCouldNotSendYetCatchesUpWhenItCan()
        {
            var room = new WireRoom(1, 41, 0.0);
            Node late = room.Net.AddNode(7);
            late.Know(room.HostNode);
            room.HostNode.Know(late);
            room.HostNode.Refuse = true; // The host's link refuses everything for a moment.
            var client = new WireClient(late);
            var bot = new OnlineBot(client, new PlayerProfile("Late", 3), 41, () => new byte[10]);
            client.HostFound += peer => bot.Join();
            room.Clients.Add(client);
            room.Bots.Add(bot);
            room.Net.Connect(late, room.HostNode);
            room.Step(0f);
            Assert.AreEqual(-1, client.HostPeer);
            room.HostNode.Refuse = false;
            Assert.IsTrue(room.RunUntil(() => room.Room.Members.Count == 2, 10f));
            CollectionAssert.AreEquivalent(new[] { "Host", "Late" }, room.Bots[0].Client.Names.ToArray());
        }

        [Test]
        public void AGuestWhoNeverAnswersIsOnlyGreetedAFewTimes()
        {
            var room = new WireRoom(1, 22);
            room.HostNode.DropOutgoing = int.MaxValue; // Nothing the host sends gets through.
            room.AddGuest(1, 22);
            room.RunUntil(() => false, 120f);
            Assert.AreEqual(1 + WireHost.MaxRegreets, room.HostNode.SentCount,
                "one greeting and then a limited number of repeats");
        }

        [Test]
        public void AGuestWhoHasAnsweredIsNotGreetedAgain()
        {
            var room = new WireRoom(2, 23);
            room.RunUntil(() => room.Bots[1].Client.State == ClientState.Lobby, 10f);
            int sentAtStart = room.HostNode.SentCount;
            room.RunUntil(() => false, 30f);
            // Only the lobby messages go out now (none, since nothing changes): no repeated greetings.
            Assert.AreEqual(sentAtStart, room.HostNode.SentCount);
        }

        [Test]
        public void AHostWhoVanishedWithoutAnEventEndsEveryGuestsMatch()
        {
            var room = new WireRoom(3, 24);
            room.Room.Start();
            room.Step(0f);
            room.Net.Vanish(room.HostNode);
            room.RunUntil(() => false, WireClient.AbsentSeconds + 1f);
            foreach (OnlineBot bot in room.Bots.Skip(1))
            {
                Assert.AreEqual(ClientState.Ended, bot.Client.State);
            }
        }

        [Test]
        public void AGuestWhoVanishedWithoutAnEventIsDroppedByTheHost()
        {
            var room = new WireRoom(4, 25);
            room.Net.Vanish(room.Nodes[1]);
            room.RunUntil(() => false, WireHost.AbsentSeconds + 1f);
            Assert.AreEqual(3, room.Room.Members.Count);
        }

        [Test]
        public void GarbageOnTheLinkIsIgnored()
        {
            var room = new WireRoom(2, 8);
            room.HostNode.RaiseReceived(new byte[] { 1, 2, 3 });
            room.HostNode.RaiseReceived(Envelope.Pack(Envelope.KindMessage, 5u, 1, WireRoom.TokenFor(1), new byte[] { 200, 1, 2 }));
            room.Nodes[0].RaiseReceived(new byte[] { 9, 9, 9, 9, 9, 9, 9, 9 });
            room.Step(0f);
            Assert.AreEqual(2, room.Room.Members.Count);
            Assert.AreEqual(ClientState.Lobby, room.Bots[1].Client.State);
        }
    }
}
