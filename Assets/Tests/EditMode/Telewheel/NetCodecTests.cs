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
using System.IO;
using NUnit.Framework;

namespace Telewheel.Tests
{
    public class NetCodecTests
    {
        private static IEnumerable<NetMessage> Samples()
        {
            yield return NetMessage.SpinResult(5);
            yield return NetMessage.Ready();
            yield return NetMessage.SubmitDrawing(new byte[] { 1, 2, 3, 250 });
            yield return NetMessage.SubmitDrawing(new byte[0]);
            yield return NetMessage.SubmitGuess("a hat");
            yield return NetMessage.SubmitGuess("café ☃");
            yield return NetMessage.Vote(true);
            yield return NetMessage.Advance();
            yield return NetMessage.PhaseChanged(OnlinePhase.Vote, 2);
            yield return NetMessage.WheelWords(new[] { "cat", "dog", "bird" });
            yield return NetMessage.TurnStart(3, StageKind.Guess, 0f, 60f, null, new byte[] { 9, 9 });
            yield return NetMessage.TurnStart(0, StageKind.Draw, 5f, 60f, "spatula", null);
            yield return NetMessage.PresentItem(2, 1, 5, PresentItemKind.Drawing, 4, null, new byte[] { 7 }, 15f);
            yield return NetMessage.VoteOpen(1, 3, "a bat", "a cat", 30f);
            yield return NetMessage.VoteResult(1, true, new[] { 0, 2, 1 });
            yield return NetMessage.RoundEnd(1, false, new[] { 1, 0 });
            yield return NetMessage.GameEnd(new[] { 3, 3, 1 });
        }

        private static void AssertSame(NetMessage expected, NetMessage actual)
        {
            Assert.AreEqual(expected.Kind, actual.Kind);
            Assert.AreEqual(expected.A, actual.A);
            Assert.AreEqual(expected.B, actual.B);
            Assert.AreEqual(expected.C, actual.C);
            Assert.AreEqual(expected.D, actual.D);
            Assert.AreEqual(expected.X, actual.X);
            Assert.AreEqual(expected.Y, actual.Y);
            Assert.AreEqual(expected.Text, actual.Text);
            CollectionAssert.AreEqual(expected.Data, actual.Data);
            CollectionAssert.AreEqual(expected.Words, actual.Words);
            CollectionAssert.AreEqual(expected.Numbers, actual.Numbers);
            Assert.AreEqual(expected.Data == null, actual.Data == null, "null data must stay null");
            Assert.AreEqual(expected.Words == null, actual.Words == null, "null words must stay null");
        }

        [Test]
        public void EveryKindOfMessageSurvivesTheRoundTrip()
        {
            int count = 0;
            foreach (NetMessage message in Samples())
            {
                AssertSame(message, NetCodec.Decode(NetCodec.Encode(message)));
                count++;
            }
            Assert.AreEqual(17, count);
        }

        [Test]
        public void EmptyAndNullArraysAreKeptApart()
        {
            NetMessage empty = NetCodec.Decode(NetCodec.Encode(NetMessage.SubmitDrawing(new byte[0])));
            Assert.IsNotNull(empty.Data);
            Assert.AreEqual(0, empty.Data.Length);
            NetMessage none = NetCodec.Decode(NetCodec.Encode(NetMessage.Ready()));
            Assert.IsNull(none.Data);
            Assert.IsNull(none.Text);
        }

        [Test]
        public void LargeDrawingsSurvive()
        {
            var big = new byte[300000];
            for (int i = 0; i < big.Length; i++)
            {
                big[i] = (byte)(i * 31);
            }
            NetMessage decoded = NetCodec.Decode(NetCodec.Encode(NetMessage.SubmitDrawing(big)));
            CollectionAssert.AreEqual(big, decoded.Data);
        }

        [Test]
        public void TruncatedBytesAreRejectedNotCrashed()
        {
            byte[] bytes = NetCodec.Encode(NetMessage.TurnStart(1, StageKind.Draw, 5f, 60f, "hello", new byte[] { 1, 2, 3 }));
            for (int length = 0; length < bytes.Length; length++)
            {
                var cut = new byte[length];
                System.Array.Copy(bytes, cut, length);
                Assert.Throws<InvalidDataException>(() => NetCodec.Decode(cut), "length " + length);
            }
        }

        [Test]
        public void ABadLengthCannotMakeUsAllocateGigabytes()
        {
            byte[] good = NetCodec.Encode(NetMessage.SubmitDrawing(new byte[] { 1, 2, 3 }));
            // Data length sits after kind(1) + 4 ints(16) + 2 floats(8) + text flag(1) = offset 26.
            byte[] bad = (byte[])good.Clone();
            bad[26] = 0xFF;
            bad[27] = 0xFF;
            bad[28] = 0xFF;
            bad[29] = 0x7F;
            Assert.Throws<InvalidDataException>(() => NetCodec.Decode(bad));
        }
    }
}
