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
using System.Linq;
using NUnit.Framework;

namespace Telewheel.Tests
{
    public class PracticeRoomTests
    {
        private static IList<string> Words(ContentFilter filter)
        {
            var words = new List<string>();
            for (int i = 0; i < 80; i++)
            {
                words.Add("word" + i);
            }
            return words;
        }

        private static bool RunUntil(OnlineSession session, Func<bool> done, float maxSeconds = 4000f, float dt = 0.25f)
        {
            for (float t = 0f; t < maxSeconds && !done(); t += dt)
            {
                session.Tick(dt);
            }
            return done();
        }

        // ----- Room codes -----

        [Test]
        public void GeneratedCodesAreFourReadableLetters()
        {
            var random = new TwRandom(9);
            for (int i = 0; i < 200; i++)
            {
                string code = RoomCode.Generate(random);
                Assert.IsTrue(RoomCode.IsValid(code), code);
                Assert.AreEqual(RoomCode.Length, code.Length);
                Assert.AreEqual(-1, code.IndexOfAny(new[] { 'I', 'O' }), "no look-alikes for 1 and 0: " + code);
            }
        }

        [Test]
        public void CodesAreTheSameForTheSameSeed()
        {
            Assert.AreEqual(RoomCode.Generate(new TwRandom(4)), RoomCode.Generate(new TwRandom(4)));
        }

        [Test]
        public void TypedCodesAreTidied()
        {
            Assert.AreEqual("ABCD", RoomCode.Normalize(" a-b c'd "));
            Assert.AreEqual("ABCD", RoomCode.Normalize("abcdefg"));
            Assert.AreEqual("AB", RoomCode.Normalize("a1b2"));
            Assert.AreEqual(string.Empty, RoomCode.Normalize(null));
            Assert.IsFalse(RoomCode.IsValid("AB"));
            Assert.IsFalse(RoomCode.IsValid("ABCO"));
            Assert.IsFalse(RoomCode.IsValid(null));
        }

        // ----- The practice room -----

        [Test]
        public void ComputerPlayersJoinTheHostsLobbyOneByOne()
        {
            var me = new PlayerProfile("Me", 2);
            OnlineSession session = PracticeRoom.Host(me, new MatchSettings(), Words, 3, null, 7);
            Assert.IsTrue(session.IsHost);
            Assert.IsTrue(session.IsPractice);
            Assert.IsTrue(RoomCode.IsValid(session.JoinCode));
            session.Tick(0.1f);
            Assert.AreEqual(ClientState.Lobby, session.Client.State);
            Assert.AreEqual(1, session.Client.Names.Count);
            Assert.IsTrue(RunUntil(session, () => session.Client.Names.Count == 4, 60f));
            Assert.AreEqual("Me", session.Client.Names[0]);
            Assert.IsTrue(session.Room.CanStart);
            session.Leave();
        }

        [Test]
        public void AHostedPracticeMatchPlaysToTheEnd()
        {
            var settings = new MatchSettings { Rounds = 2, Seed = 3 };
            OnlineSession session = PracticeRoom.Host(new PlayerProfile("Me", 0), settings, Words, 3, null, 7);
            RunUntil(session, () => session.Room.Members.Count == 4, 60f);
            var me = new OnlineBot(session.Client, 5, null);
            Assert.IsTrue(session.Room.Start(), session.Room.StartProblem);
            bool finished = RunUntil(session, () =>
            {
                me.Tick(0.25f);
                return session.Client.Phase == MatchPhase.GameEnd;
            });
            Assert.IsTrue(finished, "the practice match did not finish");
            Assert.AreEqual(4, session.Client.Scores.Count);
            session.Leave();
        }

        [Test]
        public void JoiningAPracticeRoomStartsTheMatchByItself()
        {
            var settings = new MatchSettings { Rounds = 1, Seed = 3 };
            OnlineSession session = PracticeRoom.Join(
                new PlayerProfile("Me", 0), "abcd", settings, Words, 3, null, 7);
            Assert.IsFalse(session.IsHost);
            Assert.AreEqual("ABCD", session.JoinCode);
            var me = new OnlineBot(session.Client, 5, null);
            bool finished = RunUntil(session, () =>
            {
                me.Tick(0.25f);
                return session.Client.Phase == MatchPhase.GameEnd;
            });
            Assert.IsTrue(finished, "the practice match did not finish");
            Assert.AreEqual(4, session.Client.PlayerCount);
            Assert.AreEqual(1, session.Client.LocalSeat, "the guest connects first, so takes seat 1 after the host");
            Assert.IsFalse(session.Client.IsHost);
            session.Leave();
        }

        [Test]
        public void LeavingAHostedRoomEndsItForTheComputerPlayers()
        {
            OnlineSession session = PracticeRoom.Host(
                new PlayerProfile("Me", 0), new MatchSettings(), Words, 2, null, 7);
            RunUntil(session, () => session.Room.Members.Count == 3, 60f);
            session.Leave();
            Assert.IsTrue(session.Left);
            Assert.IsTrue(session.Room.Closed);
            session.Tick(1f); // Nothing happens after leaving, and nothing throws.
            session.Leave();
        }
    }
}
