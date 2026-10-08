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
using System.Linq;
using NUnit.Framework;

namespace Telewheel.Tests
{
    public class OnlineMatchTests
    {
        private static IEnumerable<int> PlayerCounts()
        {
            for (int n = 2; n <= 8; n++)
            {
                yield return n;
            }
        }

        /// <summary>A player on the other end of the wire: answers the host the way a person would.</summary>
        private sealed class Bot
        {
            public readonly int Seat;
            public bool Silent;
            public readonly List<NetMessage> Received = new List<NetMessage>();
            private readonly OnlineMatchHost m_Host;
            private NetMessage m_PendingTurn;

            public Bot(OnlineMatchHost host, int seat)
            {
                m_Host = host;
                Seat = seat;
            }

            public void OnMessage(NetMessage message)
            {
                Received.Add(message);
                if (Silent)
                {
                    return;
                }
                switch (message.Kind)
                {
                    case NetKind.WheelWords:
                        m_Host.Receive(Seat, NetMessage.SpinResult(Seat % message.Words.Length));
                        m_Host.Receive(Seat, NetMessage.Ready());
                        break;
                    case NetKind.TurnStart:
                        // Say the prompt is in; the work starts when the host says go.
                        m_PendingTurn = message;
                        m_Host.Receive(Seat, NetMessage.TurnReady(message.A));
                        break;
                    case NetKind.TurnGo:
                        if (m_PendingTurn.B == (int)StageKind.Draw)
                        {
                            m_Host.Receive(Seat, NetMessage.SubmitDrawing(new[] { (byte)Seat, (byte)m_PendingTurn.A }));
                        }
                        else
                        {
                            m_Host.Receive(Seat, NetMessage.SubmitGuess("guess " + Seat + " " + m_PendingTurn.A));
                        }
                        break;
                    case NetKind.PresentItem:
                        if (message.A == Seat)
                        {
                            m_Host.Receive(Seat, NetMessage.Advance());
                        }
                        break;
                    case NetKind.VoteOpen:
                        m_Host.Receive(Seat, NetMessage.Vote(Seat % 2 == 0));
                        break;
                }
            }
        }

        private sealed class Room
        {
            public readonly OnlineMatchHost Host;
            public readonly List<Bot> Bots = new List<Bot>();
            public readonly List<NetMessage> Broadcasts = new List<NetMessage>();

            public Room(int players, int rounds, int seed = 3)
            {
                var settings = new MatchSettings { Rounds = rounds, Seed = seed };
                var words = new List<string>();
                for (int i = 0; i < 60; i++)
                {
                    words.Add("word" + i);
                }
                Host = new OnlineMatchHost(settings, new WordDeck(words, seed));
                for (int i = 0; i < players; i++)
                {
                    Host.AddPlayer("P" + (i + 1));
                    Bots.Add(new Bot(Host, i));
                }
                Host.Send += Route;
            }

            private void Route(int to, NetMessage message)
            {
                if (to == OnlineMatchHost.Everyone)
                {
                    Broadcasts.Add(message);
                    foreach (Bot bot in Bots)
                    {
                        bot.OnMessage(message);
                    }
                }
                else
                {
                    Bots[to].OnMessage(message);
                }
            }

            public bool RunToEnd(float step = 1f, int maxSteps = 20000)
            {
                Host.Start();
                for (int i = 0; i < maxSteps && Host.Phase != OnlinePhase.GameEnd; i++)
                {
                    Host.Tick(step);
                }
                return Host.Phase == OnlinePhase.GameEnd;
            }
        }

        [Test]
        public void LobbyRulesAreEnforced()
        {
            var host = new OnlineMatchHost(new MatchSettings(), new WordDeck(new[] { "a", "b", "c", "d", "e", "f", "g", "h" }, 1));
            Assert.AreEqual(0, host.AddPlayer("Sam"));
            Assert.AreEqual(1, host.AddPlayer("  "));
            Assert.AreEqual("Sam", host.NameOf(0));
            Assert.AreEqual("Player 2", host.NameOf(1));
            host.RemovePlayer(0);
            Assert.AreEqual(1, host.PlayerCount);
            Assert.Throws<System.ArgumentException>(host.Start, "one player is not enough");
            for (int i = 0; i < 7; i++)
            {
                host.AddPlayer("x");
            }
            Assert.AreEqual(8, host.PlayerCount);
            Assert.Throws<System.InvalidOperationException>(() => host.AddPlayer("too many"));
            host.Start();
            Assert.AreEqual(OnlinePhase.Spin, host.Phase);
            Assert.Throws<System.InvalidOperationException>(() => host.AddPlayer("late"));
            Assert.Throws<System.InvalidOperationException>(host.Start);
        }

        [TestCaseSource("PlayerCounts")]
        public void AFullMatchRunsToTheEnd(int n)
        {
            var room = new Room(n, 2);
            Assert.IsTrue(room.RunToEnd(), "the match did not finish");
            Assert.AreEqual(2, room.Host.Round);
        }

        [TestCaseSource("PlayerCounts")]
        public void EveryChainIsWholeAndPlayedByTheRightPeople(int n)
        {
            var room = new Room(n, 1);
            Assert.IsTrue(room.RunToEnd());
            ChainPlanner planner = room.Host.Planner;
            foreach (ChainState chain in room.Host.Chains)
            {
                Assert.IsNotNull(chain.Word);
                Assert.AreEqual(planner.TurnsPerChain, chain.Entries.Count);
                for (int turn = 0; turn < chain.Entries.Count; turn++)
                {
                    ChainEntry entry = chain.Entries[turn];
                    Assert.AreEqual(planner.KindOfTurn(turn), entry.Kind);
                    Assert.AreEqual(planner.PlayerForTurn(chain.Owner, turn), entry.Player);
                    if (entry.Kind == StageKind.Draw)
                    {
                        CollectionAssert.AreEqual(new[] { (byte)entry.Player, (byte)turn }, entry.Drawing);
                    }
                    else
                    {
                        Assert.AreEqual("guess " + entry.Player + " " + turn, entry.Text);
                    }
                }
                Assert.AreEqual(planner.FinalGuesser(chain.Owner), chain.Entries[chain.Entries.Count - 1].Player);
            }
        }

        [TestCaseSource("PlayerCounts")]
        public void EachPlayerOnlyGetsTheirOwnPromptAndNobodyElsesWord(int n)
        {
            var room = new Room(n, 1);
            Assert.IsTrue(room.RunToEnd());
            ChainPlanner planner = room.Host.Planner;
            foreach (Bot bot in room.Bots)
            {
                List<NetMessage> turns = bot.Received.Where(m => m.Kind == NetKind.TurnStart).ToList();
                Assert.AreEqual(planner.TurnsPerChain, turns.Count);
                for (int t = 0; t < turns.Count; t++)
                {
                    ChainState chain = room.Host.Chains[planner.ChainForTurn(bot.Seat, t)];
                    if (turns[t].B == (int)StageKind.Draw)
                    {
                        string expected = t == 0 ? chain.Word : chain.Entries[t - 1].Text;
                        Assert.AreEqual(expected, turns[t].Text);
                        Assert.IsNull(turns[t].Data);
                    }
                    else
                    {
                        CollectionAssert.AreEqual(chain.Entries[t - 1].Drawing, turns[t].Data);
                        Assert.IsNull(turns[t].Text);
                    }
                }
                // Wheel words are private: only that player's own list was sent to them.
                Assert.AreEqual(1, bot.Received.Count(m => m.Kind == NetKind.WheelWords));
            }
            Assert.IsFalse(room.Broadcasts.Any(m => m.Kind == NetKind.WheelWords || m.Kind == NetKind.TurnStart));
        }

        [TestCaseSource("PlayerCounts")]
        public void OnlyTheChainOwnerScoresWhenTheRoomVotesYes(int n)
        {
            var room = new Room(n, 2);
            Assert.IsTrue(room.RunToEnd());
            // Even seats vote yes, odd seats no, so a chain lands when more than half the room is even-seated.
            int yes = Enumerable.Range(0, n).Count(s => s % 2 == 0);
            bool lands = yes * 2 > n;
            for (int p = 0; p < n; p++)
            {
                Assert.AreEqual(lands ? 2 : 0, room.Host.Scores[p], "player " + p);
            }
            Assert.IsTrue(room.Broadcasts.Any(m => m.Kind == NetKind.GameEnd));
            Assert.AreEqual(2, room.Broadcasts.Count(m => m.Kind == NetKind.RoundEnd));
        }

        [Test]
        public void SameSeedPlaysTheSameMatch()
        {
            var a = new Room(5, 2, 77);
            var b = new Room(5, 2, 77);
            Assert.IsTrue(a.RunToEnd());
            Assert.IsTrue(b.RunToEnd());
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(a.Host.Chains[i].Word, b.Host.Chains[i].Word);
            }
        }

        [Test]
        public void ASilentPlayerDoesNotStallTheRoomTheClockMovesItOn()
        {
            var room = new Room(4, 1);
            room.Bots[2].Silent = true;
            Assert.IsTrue(room.RunToEnd(1f, 20000), "a silent player stalled the match");
            ChainPlanner planner = room.Host.Planner;
            foreach (ChainState chain in room.Host.Chains)
            {
                Assert.AreEqual(planner.TurnsPerChain, chain.Entries.Count);
            }
            ChainEntry theirs = room.Host.Chains[planner.ChainForTurn(2, 0)].Entries[0];
            Assert.AreEqual(2, theirs.Player);
            Assert.AreEqual(0, theirs.Drawing.Length, "their drawing is blank");
            ChainEntry theirGuess = room.Host.Chains[planner.ChainForTurn(2, 1)].Entries[1];
            Assert.AreEqual(TwCopy.NoGuess, theirGuess.Text);
        }

        [Test]
        public void APlayerWhoLeavesIsPlayedByTheHostAtOnce()
        {
            var room = new Room(4, 1);
            // Seat 1 never answers; they get their wheel words when the match starts, then leave.
            room.Bots[1].Silent = true;
            room.Host.Start();
            room.Host.RemovePlayer(1);
            Assert.IsTrue(room.Host.IsGone(1));
            // No waiting for any of their clocks: the room carries on as soon as the other three are done.
            int ticks = 0;
            while (room.Host.Phase != OnlinePhase.GameEnd && ticks++ < 400)
            {
                room.Host.Tick(1f);
            }
            Assert.AreEqual(OnlinePhase.GameEnd, room.Host.Phase);
            Assert.Less(ticks, 400);
            // Broadcasts still reach every bot in this harness, but private messages stop at once.
            int privateToLeaver = room.Bots[1].Received.Count(
                m => m.Kind == NetKind.WheelWords || m.Kind == NetKind.TurnStart);
            Assert.AreEqual(1, privateToLeaver, "only the wheel words, sent before they left");
            // The leaver's seat still has a blank drawing and an empty guess in the chains.
            ChainPlanner planner = room.Host.Planner;
            Assert.AreEqual(0, room.Host.Chains[planner.ChainForTurn(1, 0)].Entries[0].Drawing.Length);
            Assert.AreEqual(TwCopy.NoGuess, room.Host.Chains[planner.ChainForTurn(1, 1)].Entries[1].Text);
        }

        [Test]
        public void EveryoneLeavingStillFinishesTheMatch()
        {
            var room = new Room(3, 2);
            room.Host.Start();
            foreach (Bot bot in room.Bots)
            {
                bot.Silent = true;
            }
            for (int p = 0; p < 3; p++)
            {
                room.Host.RemovePlayer(p);
            }
            for (int i = 0; i < 2000 && room.Host.Phase != OnlinePhase.GameEnd; i++)
            {
                room.Host.Tick(1f);
            }
            Assert.AreEqual(OnlinePhase.GameEnd, room.Host.Phase);
        }

        [Test]
        public void MessagesOutOfTurnOrFromStrangersAreIgnored()
        {
            var room = new Room(3, 1);
            room.Bots[0].Silent = room.Bots[1].Silent = room.Bots[2].Silent = true;
            room.Host.Start();
            // Wrong phase: a drawing and a vote before any turn exists.
            room.Host.Receive(0, NetMessage.SubmitDrawing(new byte[] { 1 }));
            room.Host.Receive(0, NetMessage.Vote(true));
            // A seat that does not exist, and a negative one.
            room.Host.Receive(9, NetMessage.Ready());
            room.Host.Receive(-1, NetMessage.Ready());
            room.Host.Receive(0, null);
            room.Host.Pump();
            Assert.AreEqual(OnlinePhase.Spin, room.Host.Phase);

            // Everyone spins and readies; now it is turn 0, a Draw turn, so a guess is the wrong kind.
            for (int p = 0; p < 3; p++)
            {
                room.Host.Receive(p, NetMessage.SpinResult(0));
                room.Host.Receive(p, NetMessage.Ready());
            }
            room.Host.Pump();
            Assert.AreEqual(OnlinePhase.Turn, room.Host.Phase);
            room.Host.Receive(0, NetMessage.SubmitGuess("wrong kind"));
            room.Host.Pump();
            Assert.AreEqual(0, room.Host.Chains.Sum(c => c.Entries.Count));

            // Before the host has said go, a drawing is too early and does not count.
            room.Host.Receive(0, NetMessage.SubmitDrawing(new byte[] { 9 }));
            room.Host.Pump();
            Assert.AreEqual(0, room.Host.Chains.Sum(c => c.Entries.Count));
            for (int p = 0; p < 3; p++)
            {
                room.Host.Receive(p, NetMessage.TurnReady(0));
            }
            room.Host.Pump();

            // A second drawing from the same player does not count twice.
            room.Host.Receive(0, NetMessage.SubmitDrawing(new byte[] { 1 }));
            room.Host.Receive(0, NetMessage.SubmitDrawing(new byte[] { 2 }));
            room.Host.Pump();
            Assert.AreEqual(1, room.Host.Chains.Sum(c => c.Entries.Count));
        }

        [Test]
        public void ReadyBeforeSpinningDoesNotCount()
        {
            var room = new Room(2, 1);
            room.Bots[0].Silent = room.Bots[1].Silent = true;
            room.Host.Start();
            room.Host.Receive(0, NetMessage.Ready());
            room.Host.Receive(1, NetMessage.Ready());
            room.Host.Pump();
            Assert.AreEqual(OnlinePhase.Spin, room.Host.Phase, "ready without a spin result must not start the turn");
        }

        [Test]
        public void OnlyTheChainOwnerCanMoveTheRevealOn()
        {
            var room = new Room(3, 1);
            foreach (Bot bot in room.Bots)
            {
                bot.Silent = true;
            }
            room.Host.Start();
            int guard = 0;
            while (room.Host.Phase != OnlinePhase.Present && guard++ < 5000)
            {
                room.Host.Tick(1f);
            }
            Assert.AreEqual(OnlinePhase.Present, room.Host.Phase);
            int shown = room.Broadcasts.Count(m => m.Kind == NetKind.PresentItem);
            room.Host.Receive(1, NetMessage.Advance());
            room.Host.Receive(2, NetMessage.Advance());
            room.Host.Pump();
            Assert.AreEqual(shown, room.Broadcasts.Count(m => m.Kind == NetKind.PresentItem));
            room.Host.Receive(0, NetMessage.Advance());
            room.Host.Pump();
            Assert.AreEqual(shown + 1, room.Broadcasts.Count(m => m.Kind == NetKind.PresentItem));
        }

        [Test]
        public void TheVoteResolvesAsSoonAsEveryoneHasVotedOrOnTimeout()
        {
            var room = new Room(4, 1);
            foreach (Bot bot in room.Bots)
            {
                bot.Silent = true;
            }
            room.Host.Start();
            int guard = 0;
            while (room.Host.Phase != OnlinePhase.Vote && guard++ < 5000)
            {
                room.Host.Tick(1f);
            }
            Assert.AreEqual(OnlinePhase.Vote, room.Host.Phase);
            room.Host.Receive(0, NetMessage.Vote(true));
            room.Host.Receive(1, NetMessage.Vote(true));
            room.Host.Receive(2, NetMessage.Vote(true));
            room.Host.Pump();
            Assert.AreEqual(OnlinePhase.Vote, room.Host.Phase, "still waiting on the fourth vote");
            room.Host.Receive(3, NetMessage.Vote(false));
            room.Host.Pump();
            Assert.AreEqual(OnlinePhase.VoteResult, room.Host.Phase);
            Assert.AreEqual(1, room.Host.Scores[0], "3 of 4 is a majority");

            // Next chain: nobody votes, the clock decides, and no votes means no point.
            guard = 0;
            while (room.Host.Phase != OnlinePhase.Vote && guard++ < 5000)
            {
                room.Host.Tick(1f);
            }
            for (int i = 0; i < 40 && room.Host.Phase == OnlinePhase.Vote; i++)
            {
                room.Host.Tick(1f);
            }
            Assert.AreEqual(OnlinePhase.VoteResult, room.Host.Phase);
            Assert.AreEqual(0, room.Host.Scores[1]);
        }
    }
}
