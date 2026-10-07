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
    public class OnlineRoomTests
    {
        private static IEnumerable<int> PlayerCounts()
        {
            for (int n = 2; n <= 8; n++)
            {
                yield return n;
            }
        }

        /// <summary>A whole room in one process: the host and its own player, plus bots joined over the loopback network.</summary>
        private sealed class Rig
        {
            public readonly LoopbackNetwork Net = new LoopbackNetwork();
            public readonly OnlineRoomHost Room;
            public readonly OnlineBot HostBot;
            public readonly List<OnlineBot> Bots = new List<OnlineBot>();
            public readonly List<int> Peers = new List<int>();

            public Rig(int rounds = 1, int seed = 5, int wordCount = 60)
            {
                var settings = new MatchSettings { Rounds = rounds, Seed = seed };
                var words = new List<string>();
                for (int i = 0; i < wordCount; i++)
                {
                    words.Add("word" + i);
                }
                Room = new OnlineRoomHost(
                    Net.Host, settings, new PlayerProfile("Host", 0), filter => words);
                HostBot = new OnlineBot(Room.LocalPort, new PlayerProfile("Host", 0), seed, DrawingFor(0));
                HostBot.Join();
                Step(0f);
            }

            public OnlineBot AddBot(string name, int icon = 1)
            {
                INetClientPort port;
                int peer = Net.Connect(out port);
                var bot = new OnlineBot(port, new PlayerProfile(name, icon), 100 + Bots.Count, DrawingFor(Bots.Count + 1));
                Bots.Add(bot);
                Peers.Add(peer);
                bot.Join();
                Step(0f);
                return bot;
            }

            public Rig WithPlayers(int total)
            {
                for (int i = 1; i < total; i++)
                {
                    AddBot("P" + (i + 1));
                }
                return this;
            }

            public IEnumerable<OnlineBot> Everyone
            {
                get { return new[] { HostBot }.Concat(Bots); }
            }

            // What the bot with this index "draws": bytes that say who drew them.
            private static Func<byte[]> DrawingFor(int index)
            {
                return () => new[] { (byte)index, (byte)42 };
            }

            public void Step(float dt)
            {
                Net.Deliver();
                Room.Tick(dt);
                foreach (OnlineBot bot in Everyone)
                {
                    bot.Tick(dt);
                }
                Net.Deliver();
                Room.Tick(0f);
            }

            public bool RunUntil(Func<bool> done, float maxSeconds = 4000f, float dt = 0.25f)
            {
                for (float t = 0f; t < maxSeconds && !done(); t += dt)
                {
                    Step(dt);
                }
                return done();
            }

            /// <summary>Starts the match and delivers the first messages, so players can act straight away.</summary>
            public void Begin()
            {
                Assert.IsTrue(Room.Start(), Room.StartProblem);
                Step(0f);
            }

            public bool RunToEnd()
            {
                Room.Start();
                return RunUntil(() => Everyone.All(b => b.Client.Phase == MatchPhase.GameEnd));
            }
        }

        // ----- The lobby -----

        [Test]
        public void TheHostIsSeatZeroAndWelcomedAlone()
        {
            var rig = new Rig();
            Assert.AreEqual(ClientState.Lobby, rig.HostBot.Client.State);
            Assert.AreEqual(0, rig.HostBot.Client.LocalSeat);
            Assert.IsTrue(rig.HostBot.Client.IsHost);
            CollectionAssert.AreEqual(new[] { "Host" }, rig.HostBot.Client.Names);
            Assert.IsFalse(rig.Room.CanStart, "one player cannot start a match");
        }

        [Test]
        public void PlayersGetSeatsInTheOrderTheyJoin()
        {
            var rig = new Rig();
            OnlineBot ann = rig.AddBot("Ann", 3);
            OnlineBot bo = rig.AddBot("Bo", 4);
            Assert.AreEqual(1, ann.Client.LocalSeat);
            Assert.AreEqual(2, bo.Client.LocalSeat);
            Assert.IsFalse(ann.Client.IsHost);
            foreach (OnlineBot bot in rig.Everyone)
            {
                CollectionAssert.AreEqual(new[] { "Host", "Ann", "Bo" }, bot.Client.Names);
                CollectionAssert.AreEqual(new[] { 0, 3, 4 }, bot.Client.Icons);
            }
            Assert.IsTrue(rig.Room.CanStart);
        }

        [Test]
        public void TwoPlayersWithTheSameNameAreToldApart()
        {
            var rig = new Rig();
            rig.AddBot("host");
            rig.AddBot("Sam");
            rig.AddBot("sam");
            CollectionAssert.AreEqual(new[] { "Host", "host 2", "Sam", "sam 2" }, rig.HostBot.Client.Names.ToArray());
        }

        [Test]
        public void ABlankNameBecomesPlayerN()
        {
            var rig = new Rig();
            rig.AddBot("   ");
            Assert.AreEqual("Player 2", rig.HostBot.Client.Names[1]);
        }

        [Test]
        public void ALongNameIsShortened()
        {
            var rig = new Rig();
            rig.AddBot(new string('x', 60));
            Assert.AreEqual(PlayerProfile.MaxNameLength, rig.HostBot.Client.Names[1].Length);
        }

        [Test]
        public void SomeoneLeavingTheLobbyShiftsTheSeatsDown()
        {
            var rig = new Rig();
            rig.AddBot("Ann");
            OnlineBot bo = rig.AddBot("Bo");
            rig.AddBot("Cy");
            rig.Net.Disconnect(rig.Peers[0]);
            rig.Step(0f);
            CollectionAssert.AreEqual(new[] { "Host", "Bo", "Cy" }, rig.HostBot.Client.Names);
            Assert.AreEqual(1, bo.Client.LocalSeat);
            Assert.AreEqual(2, rig.Bots[2].Client.LocalSeat);
        }

        [Test]
        public void TheRoomHoldsEightAndTurnsTheNinthAway()
        {
            var rig = new Rig().WithPlayers(8);
            OnlineBot ninth = rig.AddBot("Late");
            Assert.AreEqual(ClientState.Rejected, ninth.Client.State);
            Assert.AreEqual(RejectReason.RoomFull, ninth.Client.Rejection);
            Assert.AreEqual(8, rig.HostBot.Client.Names.Count);
        }

        [Test]
        public void AnotherProtocolVersionIsTurnedAway()
        {
            var rig = new Rig();
            INetClientPort port;
            rig.Net.Connect(out port);
            var client = new OnlineMatchClient(port, new PlayerProfile("Old", 0));
            port.SendToHost(NetMessage.Hello("Old", 0, OnlineRoomHost.ProtocolVersion + 1));
            rig.Step(0f);
            Assert.AreEqual(ClientState.Rejected, client.State);
            Assert.AreEqual(RejectReason.WrongVersion, client.Rejection);
            Assert.AreEqual(1, rig.Room.Members.Count);
        }

        [Test]
        public void ChangingYourNameInTheLobbyReachesEveryone()
        {
            var rig = new Rig();
            OnlineBot ann = rig.AddBot("Ann", 2);
            ann.Client.UpdateProfile(new PlayerProfile("Anna", 5));
            rig.Step(0f);
            Assert.AreEqual("Anna", rig.HostBot.Client.Names[1]);
            Assert.AreEqual(5, rig.HostBot.Client.Icons[1]);
            Assert.AreEqual(2, rig.Room.Members.Count);
        }

        [Test]
        public void TheHostsRulesAndEnvironmentReachEveryone()
        {
            var rig = new Rig().WithPlayers(3);
            var seen = new List<string>();
            rig.Bots[0].Client.EnvironmentChanged += seen.Add;
            rig.Room.SetRules(4, ContentFilter.Raunchy);
            rig.Room.SetEnvironment("space");
            rig.Step(0f);
            foreach (OnlineBot bot in rig.Everyone)
            {
                Assert.AreEqual(4, bot.Client.RoundCount);
                Assert.AreEqual(ContentFilter.Raunchy, bot.Client.Filter);
                Assert.AreEqual("space", bot.Client.Environment);
            }
            CollectionAssert.Contains(seen, "space");
        }

        [Test]
        public void RoundsAreClampedToWhatTheGameAllows()
        {
            var rig = new Rig().WithPlayers(2);
            rig.Room.SetRules(99, ContentFilter.Family);
            Assert.AreEqual(MatchSettings.MaxRounds, rig.Room.Settings.Rounds);
            rig.Room.SetRules(-3, ContentFilter.Family);
            Assert.AreEqual(MatchSettings.MinRounds, rig.Room.Settings.Rounds);
        }

        [Test]
        public void StartingNeedsEnoughWords()
        {
            var rig = new Rig(wordCount: 3).WithPlayers(3);
            Assert.IsFalse(rig.Room.Start());
            StringAssert.Contains("words", rig.Room.StartProblem);
            Assert.IsFalse(rig.Room.InMatch);
        }

        [Test]
        public void StartingNeedsTwoPlayers()
        {
            var rig = new Rig();
            Assert.IsFalse(rig.Room.Start());
            StringAssert.Contains("more players", rig.Room.StartProblem);
        }

        // ----- Whole matches -----

        [TestCaseSource("PlayerCounts")]
        public void EveryoneSeesTheSameMatchToTheEnd(int n)
        {
            var rig = new Rig(rounds: 2).WithPlayers(n);
            Assert.IsTrue(rig.RunToEnd(), "the match did not finish");
            int[] scores = rig.Room.Match.Scores.ToArray();
            foreach (OnlineBot bot in rig.Everyone)
            {
                Assert.AreEqual(ClientState.Playing, bot.Client.State);
                Assert.AreEqual(n, bot.Client.PlayerCount);
                CollectionAssert.AreEqual(scores, bot.Client.Scores.ToArray());
                Assert.AreEqual(2, bot.Client.Round);
                Assert.IsTrue(bot.Client.IsFinalRound);
            }
        }

        [TestCaseSource("PlayerCounts")]
        public void AChainsDrawingReachesTheNextPlayerUnchanged(int n)
        {
            var rig = new Rig().WithPlayers(n);
            var seen = new Dictionary<int, List<byte[]>>();
            foreach (OnlineBot bot in rig.Everyone)
            {
                OnlineBot captured = bot;
                bot.Client.PhaseChanged += (from, to) =>
                {
                    if (to == MatchPhase.Turn && captured.Client.TurnKind == StageKind.Guess)
                    {
                        List<byte[]> list;
                        if (!seen.TryGetValue(captured.Client.LocalSeat, out list))
                        {
                            list = new List<byte[]>();
                            seen[captured.Client.LocalSeat] = list;
                        }
                        list.Add(captured.Client.PromptDrawing);
                    }
                };
            }
            Assert.IsTrue(rig.RunToEnd());
            ChainPlanner planner = rig.Room.Match.Planner;
            foreach (ChainState chain in rig.Room.Match.Chains)
            {
                for (int turn = 1; turn < chain.Entries.Count; turn++)
                {
                    if (chain.Entries[turn].Kind != StageKind.Guess)
                    {
                        continue;
                    }
                    int guesser = chain.Entries[turn].Player;
                    byte[] drawing = chain.Entries[turn - 1].Drawing;
                    Assert.IsTrue(seen[guesser].Any(d => d.SequenceEqual(drawing)),
                        "player " + guesser + " should have been shown the drawing from turn " + (turn - 1));
                }
            }
            Assert.AreEqual(planner.TurnsPerChain, rig.Room.Match.Chains[0].Entries.Count);
        }

        [Test]
        public void TheSpinWordIsOneOfTheWordsOnThatPlayersWheel()
        {
            var rig = new Rig().WithPlayers(3);
            var wheels = new Dictionary<int, string[]>();
            foreach (OnlineBot bot in rig.Everyone)
            {
                OnlineBot captured = bot;
                bot.Client.PhaseChanged += (from, to) =>
                {
                    if (to == MatchPhase.Spin)
                    {
                        wheels[captured.Client.LocalSeat] = captured.Client.WheelWords.ToArray();
                    }
                };
            }
            Assert.IsTrue(rig.RunToEnd());
            Assert.AreEqual(3, wheels.Count);
            foreach (ChainState chain in rig.Room.Match.Chains)
            {
                Assert.AreEqual(8, wheels[chain.Owner].Length);
                CollectionAssert.Contains(wheels[chain.Owner], chain.Word);
            }
        }

        [Test]
        public void OnlyTheChainOwnerMayMoveTheRevealOn()
        {
            var rig = new Rig().WithPlayers(4);
            foreach (OnlineBot bot in rig.Everyone)
            {
                bot.Silent = true; // The test drives the reveal by hand.
            }
            rig.Begin();
            Assert.IsTrue(rig.RunUntil(() => rig.Everyone.All(b => b.Client.Phase == MatchPhase.Present), 4000f));
            int chain = rig.HostBot.Client.PresentChain;
            foreach (OnlineBot bot in rig.Everyone)
            {
                Assert.AreEqual(bot.Client.LocalSeat == chain, bot.Client.CanAdvancePresent);
            }
            int before = rig.HostBot.Client.PresentIndex;
            OnlineBot stranger = rig.Everyone.First(b => b.Client.LocalSeat != chain);
            stranger.Client.SkipPresent();
            rig.Step(0f);
            Assert.AreEqual(before, rig.HostBot.Client.PresentIndex, "a non-owner cannot advance the reveal");
            OnlineBot owner = rig.Everyone.First(b => b.Client.LocalSeat == chain);
            owner.Client.SkipPresent();
            rig.Step(0f);
            Assert.AreEqual(before + 1, rig.HostBot.Client.PresentIndex);
        }

        [Test]
        public void ThePresentItemsAreTheChainInOrder()
        {
            var rig = new Rig().WithPlayers(4);
            var kinds = new List<PresentItemKind>();
            rig.HostBot.Client.PhaseChanged += (from, to) => { };
            int lastChain = -1;
            int lastIndex = -1;
            Assert.IsTrue(rig.Room.Start());
            rig.RunUntil(() =>
            {
                OnlineMatchClient c = rig.HostBot.Client;
                if (c.Phase == MatchPhase.Present && c.PresentChain == 0
                    && (c.PresentChain != lastChain || c.PresentIndex != lastIndex))
                {
                    lastChain = c.PresentChain;
                    lastIndex = c.PresentIndex;
                    kinds.Add(c.CurrentPresentItem.Kind);
                }
                return c.Phase == MatchPhase.Vote;
            });
            Assert.AreEqual(PresentItemKind.Word, kinds[0]);
            Assert.AreEqual(PresentItemKind.Drawing, kinds[1]);
            Assert.AreEqual(PresentItemKind.Guess, kinds[2]);
            Assert.AreEqual(rig.Room.Match.Planner.TurnsPerChain, kinds.Count, "everything but the last guess");
        }

        [Test]
        public void TheVoteNeedsAMajorityOfEveryPlayer()
        {
            var rig = new Rig().WithPlayers(4);
            foreach (OnlineBot bot in rig.Everyone)
            {
                bot.Silent = true;
            }
            rig.Room.Start();
            Assert.IsTrue(rig.RunUntil(() => rig.HostBot.Client.Phase == MatchPhase.Present));
            rig.RunUntil(() => rig.HostBot.Client.Phase == MatchPhase.Vote);
            // Two yes out of four is not a majority.
            rig.Everyone.ElementAt(0).Client.CastVote(true);
            rig.Everyone.ElementAt(1).Client.CastVote(true);
            rig.Everyone.ElementAt(2).Client.CastVote(false);
            rig.Everyone.ElementAt(3).Client.CastVote(false);
            rig.Step(0f);
            Assert.AreEqual(MatchPhase.VoteResult, rig.HostBot.Client.Phase);
            Assert.IsFalse(rig.HostBot.Client.LastVoteLanded);
            CollectionAssert.AreEqual(new[] { 0, 0, 0, 0 }, rig.HostBot.Client.Scores.ToArray());
        }

        [Test]
        public void AMajorityScoresForTheChainOwnerOnly()
        {
            var rig = new Rig().WithPlayers(3);
            foreach (OnlineBot bot in rig.Everyone)
            {
                bot.Silent = true;
            }
            rig.Room.Start();
            rig.RunUntil(() => rig.HostBot.Client.Phase == MatchPhase.Vote);
            int owner = rig.HostBot.Client.PresentOwner;
            foreach (OnlineBot bot in rig.Everyone)
            {
                bot.Client.CastVote(true);
            }
            rig.Step(0f);
            Assert.IsTrue(rig.HostBot.Client.LastVoteLanded);
            for (int seat = 0; seat < 3; seat++)
            {
                Assert.AreEqual(seat == owner ? 1 : 0, rig.HostBot.Client.Scores[seat]);
            }
        }

        [Test]
        public void SubmittingTwiceOrOutOfTurnDoesNothing()
        {
            var rig = new Rig().WithPlayers(3);
            foreach (OnlineBot bot in rig.Everyone)
            {
                bot.Silent = true;
            }
            rig.Begin();
            OnlineMatchClient c = rig.HostBot.Client;
            c.SubmitDrawing(new byte[] { 1 });
            c.SubmitGuess("nope");
            c.CastVote(true);
            c.SkipPresent();
            rig.Step(0f);
            Assert.IsFalse(c.LocalDone, "nothing can be handed in during the spin");
            c.CompleteSpin(2);
            string word = c.SpinWord;
            c.CompleteSpin(5);
            Assert.AreEqual(word, c.SpinWord, "the wheel only stops once");
            c.ConfirmSpin();
            Assert.IsTrue(c.LocalDone);
            c.ConfirmSpin();
            rig.Step(0f);
            Assert.AreEqual(0, rig.Room.Match.Turn);
        }

        [Test]
        public void TheResultScreensAreNeverTreatedAsWaiting()
        {
            // Voting marks you done; the vote result, round end and game end that follow must start fresh,
            // or the "waiting for" screen would cover them.
            var rig = new Rig(rounds: 2).WithPlayers(3);
            var doneOnEntry = new Dictionary<MatchPhase, bool>();
            rig.HostBot.Client.PhaseChanged += (from, to) =>
            {
                if (to == MatchPhase.VoteResult || to == MatchPhase.RoundEnd || to == MatchPhase.GameEnd)
                {
                    doneOnEntry[to] = doneOnEntry.ContainsKey(to) && doneOnEntry[to] || rig.HostBot.Client.LocalDone;
                }
            };
            Assert.IsTrue(rig.RunToEnd());
            Assert.IsTrue(doneOnEntry.ContainsKey(MatchPhase.VoteResult));
            Assert.IsTrue(doneOnEntry.ContainsKey(MatchPhase.RoundEnd));
            Assert.IsTrue(doneOnEntry.ContainsKey(MatchPhase.GameEnd));
            foreach (KeyValuePair<MatchPhase, bool> entry in doneOnEntry)
            {
                Assert.IsFalse(entry.Value, entry.Key + " must not start as done");
            }
            Assert.AreEqual(0, rig.HostBot.Client.WaitingFor.Count);
        }

        [Test]
        public void TheLobbyVersionMovesWhenTheEnvironmentChanges()
        {
            var rig = new Rig().WithPlayers(2);
            int before = rig.Bots[0].Client.LobbyVersion;
            rig.Room.SetEnvironment("beach");
            rig.Step(0f);
            Assert.Greater(rig.Bots[0].Client.LobbyVersion, before, "a screen showing the old pick must rebuild");
            Assert.AreEqual("beach", rig.Bots[0].Client.Environment);
            int mid = rig.Bots[0].Client.LobbyVersion;
            rig.Room.SetEnvironment("space");
            rig.Step(0f);
            Assert.Greater(rig.Bots[0].Client.LobbyVersion, mid);
        }

        [Test]
        public void TheStepVersionMovesAsPeopleFinish()
        {
            var rig = new Rig().WithPlayers(3);
            foreach (OnlineBot bot in rig.Everyone)
            {
                bot.Silent = true;
            }
            rig.Begin();
            OnlineMatchClient c = rig.HostBot.Client;
            int start = c.StepVersion;
            rig.Bots[0].Client.CompleteSpin(0);
            rig.Bots[0].Client.ConfirmSpin();
            rig.Step(0f);
            Assert.Greater(c.StepVersion, start, "someone else finished, so the waiting list changed");
            int afterOther = c.StepVersion;
            rig.Net.Disconnect(rig.Peers[1]);
            rig.Step(0f);
            Assert.Greater(c.StepVersion, afterOther, "someone leaving changes it too");
        }

        // ----- Waiting for everyone's prompt before the clock starts -----

        // A player whose app answers only when the test says so: it never sends TurnReady by itself.
        private sealed class RawPlayer
        {
            public readonly INetClientPort Port;
            public readonly int Peer;
            public readonly List<NetMessage> Received = new List<NetMessage>();

            public RawPlayer(Rig rig, string name)
            {
                Peer = rig.Net.Connect(out Port);
                Port.FromHost += Received.Add;
                Port.SendToHost(NetMessage.Hello(name, 0, OnlineRoomHost.ProtocolVersion));
                rig.Step(0f);
            }

            public void SpinAndReady()
            {
                Port.SendToHost(NetMessage.SpinResult(0));
                Port.SendToHost(NetMessage.Ready());
            }
        }

        // Starts a three-player match (two bots and a raw player) and runs until the first turn is loading.
        private static RawPlayer StartUntilLoading(Rig rig)
        {
            var raw = new RawPlayer(rig, "Slow");
            rig.Begin();
            raw.SpinAndReady();
            Assert.IsTrue(rig.RunUntil(() => rig.HostBot.Client.IsLoadingTurn, 120f), "the first turn never began loading");
            return raw;
        }

        [Test]
        public void TheClockDoesNotStartUntilEveryoneHasTheirPrompt()
        {
            var rig = new Rig().WithPlayers(2);
            RawPlayer raw = StartUntilLoading(rig);
            for (int i = 0; i < 80; i++)
            {
                rig.Step(0.25f); // 20 seconds pass; the slow player has not answered.
            }
            Assert.IsTrue(rig.HostBot.Client.IsLoadingTurn, "everyone is still waiting for the slow player");
            Assert.AreNotEqual(MatchPhase.Countdown, rig.HostBot.Client.Phase);
            Assert.AreEqual(rig.Room.Settings.TurnLoadSeconds, rig.Room.Match.Clock.Total, 0.001f,
                "the drawing clock has not started; only the wait for prompts is running");

            raw.Port.SendToHost(NetMessage.TurnReady(0));
            rig.Step(0f);
            Assert.IsFalse(rig.HostBot.Client.IsLoadingTurn);
            Assert.AreEqual(MatchPhase.Countdown, rig.HostBot.Client.Phase);
            float expected = rig.Room.Settings.CountdownSeconds + rig.Room.Settings.DrawSeconds
                + rig.Room.Settings.TurnGraceSeconds;
            Assert.AreEqual(expected, rig.Room.Match.Clock.Total, 0.001f, "now the real turn clock runs");
        }

        [Test]
        public void ASlowPromptDoesNotHoldTheRoomForever()
        {
            var rig = new Rig().WithPlayers(2);
            StartUntilLoading(rig);
            Assert.IsTrue(
                rig.RunUntil(() => rig.HostBot.Client.Phase == MatchPhase.Countdown, rig.Room.Settings.TurnLoadSeconds + 5f),
                "the host must start the turn when the wait for prompts runs out");
        }

        [Test]
        public void SomeoneLeavingWhileTheTurnLoadsDoesNotHoldItUp()
        {
            var rig = new Rig().WithPlayers(2);
            RawPlayer raw = StartUntilLoading(rig);
            rig.Net.Disconnect(raw.Peer);
            rig.Step(0f);
            Assert.AreEqual(MatchPhase.Countdown, rig.HostBot.Client.Phase);
        }

        [Test]
        public void WorkHandedInBeforeTheGoIsNotCounted()
        {
            var rig = new Rig().WithPlayers(2);
            RawPlayer raw = StartUntilLoading(rig);
            raw.Port.SendToHost(NetMessage.SubmitDrawing(new byte[] { 1, 2, 3 }));
            rig.Step(0f);
            Assert.AreEqual(0, rig.Room.Match.Chains.Sum(c => c.Entries.Count));
        }

        [Test]
        public void ATurnReadyForTheWrongTurnDoesNotCount()
        {
            var rig = new Rig().WithPlayers(2);
            RawPlayer raw = StartUntilLoading(rig);
            raw.Port.SendToHost(NetMessage.TurnReady(5));
            rig.Step(0f);
            Assert.IsTrue(rig.HostBot.Client.IsLoadingTurn);
        }

        // ----- Measuring the connection -----

        [Test]
        public void TheLinkTestSendsEachGuestEachSizeAndTimesTheReplies()
        {
            var rig = new Rig().WithPlayers(4);
            var changes = 0;
            rig.Room.LinkTestChanged += () => changes++;
            rig.Room.RunLinkTest(new[] { 100, 50000 });
            Assert.IsTrue(rig.Room.LinkTestRunning);
            Assert.IsTrue(rig.RunUntil(() => !rig.Room.LinkTestRunning, 60f));
            IReadOnlyList<LinkTestResult> results = rig.Room.LinkTestResults;
            Assert.AreEqual(6, results.Count, "three guests, two sizes each");
            foreach (int seat in new[] { 1, 2, 3 })
            {
                CollectionAssert.AreEqual(
                    new[] { 100, 50000 }, results.Where(r => r.Seat == seat).Select(r => r.Bytes).ToArray());
            }
            Assert.IsFalse(results.Any(r => r.Seat == 0), "the host's own connection is not a network link");
            Assert.IsTrue(results.All(r => r.Seconds >= 0f));
            Assert.Greater(changes, 1);
        }

        [Test]
        public void TheLinkTestCanBeRunAgainAndNeverAfterTheStart()
        {
            var rig = new Rig().WithPlayers(2);
            rig.Room.RunLinkTest(new[] { 10 });
            rig.RunUntil(() => !rig.Room.LinkTestRunning, 30f);
            Assert.AreEqual(1, rig.Room.LinkTestResults.Count);
            rig.Room.RunLinkTest(new[] { 10, 20 });
            rig.RunUntil(() => !rig.Room.LinkTestRunning, 30f);
            Assert.AreEqual(2, rig.Room.LinkTestResults.Count, "a new run replaces the old results");
            rig.Begin();
            rig.Room.RunLinkTest(new[] { 10 });
            Assert.IsFalse(rig.Room.LinkTestRunning, "no test once the match has started");
        }

        // ----- Ids that the transport reuses -----

        private sealed class FakeHostPort : INetHostPort
        {
            public readonly List<KeyValuePair<int, NetMessage>> Sent = new List<KeyValuePair<int, NetMessage>>();

            public event Action<int, NetMessage> FromPeer;

            public event Action<int> PeerLeft;

            public void SendToPeer(int peer, NetMessage message)
            {
                Sent.Add(new KeyValuePair<int, NetMessage>(peer, message));
            }

            public void Raise(int peer, NetMessage message)
            {
                FromPeer(peer, message);
            }

            public void RaiseLeft(int peer)
            {
                PeerLeft(peer);
            }
        }

        [Test]
        public void ANewPlayerWithTheIdOfSomeoneWhoLeftIsTurnedAway()
        {
            var port = new FakeHostPort();
            var words = new List<string>();
            for (int i = 0; i < 40; i++)
            {
                words.Add("w" + i);
            }
            var room = new OnlineRoomHost(port, new MatchSettings(), new PlayerProfile("Host", 0), f => words);
            port.Raise(5, NetMessage.Hello("A", 0, OnlineRoomHost.ProtocolVersion));
            port.Raise(6, NetMessage.Hello("B", 1, OnlineRoomHost.ProtocolVersion));
            Assert.IsTrue(room.Start(), room.StartProblem);
            port.RaiseLeft(5);
            port.Sent.Clear();
            port.Raise(5, NetMessage.Hello("Newcomer", 2, OnlineRoomHost.ProtocolVersion));
            KeyValuePair<int, NetMessage> reply = port.Sent.Single(m => m.Key == 5);
            Assert.AreEqual(NetKind.Rejected, reply.Value.Kind);
            Assert.AreEqual((int)RejectReason.MatchStarted, reply.Value.A);
            Assert.AreEqual(3, room.Members.Count);
        }

        // ----- Lost connections and the start of the match -----

        [Test]
        public void ALostConnectionEndsTheMatchOnThatDevice()
        {
            var rig = new Rig().WithPlayers(3);
            rig.Begin();
            rig.Bots[0].Client.ConnectionLost();
            Assert.AreEqual(ClientState.Ended, rig.Bots[0].Client.State);
            Assert.AreEqual(EndReason.ConnectionLost, rig.Bots[0].Client.EndedBecause);
            StringAssert.Contains("lost your connection", TwCopy.EndedLine(EndReason.ConnectionLost));
        }

        [Test]
        public void ARoomClosedForALostConnectionTellsTheHostsOwnPlayerWhy()
        {
            var rig = new Rig().WithPlayers(2);
            rig.Room.Close(EndReason.ConnectionLost);
            rig.Step(0f);
            Assert.AreEqual(EndReason.ConnectionLost, rig.HostBot.Client.EndedBecause);
        }

        [Test]
        public void TheRoomSaysOnceWhenTheMatchStarts()
        {
            var rig = new Rig().WithPlayers(3);
            int started = 0;
            rig.Room.Started += () => started++;
            Assert.AreEqual(0, started);
            rig.Begin();
            Assert.AreEqual(1, started);
            Assert.IsFalse(rig.Room.Start(), "a second start is refused and says nothing");
            Assert.AreEqual(1, started);
        }

        // ----- Waiting for others -----

        [Test]
        public void TheWaitingListNamesWhoIsStillWorking()
        {
            var rig = new Rig().WithPlayers(3);
            rig.Bots[1].Silent = true; // Seat 2 never does anything.
            rig.Room.Start();
            Assert.IsTrue(rig.RunUntil(() => rig.HostBot.Client.Phase == MatchPhase.Countdown));
            Assert.AreEqual(3, rig.HostBot.Client.WaitingFor.Count, "nobody has submitted yet");
            rig.RunUntil(() => rig.HostBot.Client.LocalDone && rig.Bots[0].Client.LocalDone, 100f);
            CollectionAssert.AreEqual(new[] { 2 }, rig.HostBot.Client.WaitingFor.ToArray());
            CollectionAssert.AreEqual(new[] { 2 }, rig.Bots[0].Client.WaitingFor.ToArray());
        }

        [Test]
        public void ASlowPlayerDoesNotHoldUpTheRoomForever()
        {
            var rig = new Rig().WithPlayers(3);
            rig.Bots[1].Silent = true;
            Assert.IsTrue(rig.RunToEnd(), "the host moves on when the clock runs out");
        }

        // ----- People leaving -----

        [Test]
        public void SomeoneLeavingMidMatchIsAnnouncedAndTheMatchGoesOn()
        {
            var rig = new Rig().WithPlayers(4);
            var left = new List<int>();
            rig.HostBot.Client.PlayerLeft += left.Add;
            rig.Room.Start();
            Assert.IsTrue(rig.RunUntil(() => rig.HostBot.Client.Phase == MatchPhase.Turn));
            rig.Net.Disconnect(rig.Peers[1]); // Seat 2.
            rig.Step(0f);
            CollectionAssert.AreEqual(new[] { 2 }, left);
            Assert.IsTrue(rig.HostBot.Client.IsGone(2));
            Assert.IsTrue(rig.RunUntil(() =>
                rig.HostBot.Client.Phase == MatchPhase.GameEnd && rig.Bots[0].Client.Phase == MatchPhase.GameEnd
                && rig.Bots[2].Client.Phase == MatchPhase.GameEnd));
            Assert.AreEqual(4, rig.HostBot.Client.PlayerCount, "a player who left keeps their seat in the chains");
        }

        [Test]
        public void ThePlayerWhoLeftIsNotWaitedFor()
        {
            var rig = new Rig().WithPlayers(3);
            foreach (OnlineBot bot in rig.Everyone)
            {
                bot.Silent = true;
            }
            rig.Begin();
            rig.HostBot.Client.CompleteSpin(0);
            rig.HostBot.Client.ConfirmSpin();
            rig.Bots[0].Client.CompleteSpin(0);
            rig.Bots[0].Client.ConfirmSpin();
            rig.Step(0f);
            CollectionAssert.AreEqual(new[] { 2 }, rig.HostBot.Client.WaitingFor.ToArray());
            rig.Net.Disconnect(rig.Peers[1]);
            rig.Step(0f);
            Assert.AreEqual(MatchPhase.Countdown, rig.HostBot.Client.Phase, "the host spun for the one who left");
        }

        [Test]
        public void IfOnlyOnePlayerIsLeftTheMatchEnds()
        {
            var rig = new Rig().WithPlayers(2);
            rig.Room.Start();
            rig.Step(0f);
            rig.Net.Disconnect(rig.Peers[0]);
            rig.Step(0f);
            Assert.AreEqual(ClientState.Ended, rig.HostBot.Client.State);
            Assert.AreEqual(EndReason.NotEnoughPlayers, rig.HostBot.Client.EndedBecause);
            Assert.IsTrue(rig.Room.Closed);
        }

        [Test]
        public void WhenTheHostGoesEveryoneIsTold()
        {
            var rig = new Rig().WithPlayers(3);
            rig.Room.Start();
            rig.Step(0f);
            rig.Net.LoseHost();
            rig.Step(0f);
            foreach (OnlineBot bot in rig.Bots)
            {
                Assert.AreEqual(ClientState.Ended, bot.Client.State);
                Assert.AreEqual(EndReason.HostEnded, bot.Client.EndedBecause);
            }
        }

        [Test]
        public void TheHostClosingTheRoomTellsEveryone()
        {
            var rig = new Rig().WithPlayers(3);
            rig.Room.Close(EndReason.HostEnded);
            rig.Step(0f);
            foreach (OnlineBot bot in rig.Everyone)
            {
                Assert.AreEqual(ClientState.Ended, bot.Client.State);
            }
            Assert.IsFalse(rig.Room.Start());
        }

        [Test]
        public void SomeoneArrivingAfterTheStartIsTurnedAway()
        {
            var rig = new Rig().WithPlayers(3);
            rig.Room.Start();
            OnlineBot late = rig.AddBot("Late");
            Assert.AreEqual(ClientState.Rejected, late.Client.State);
            Assert.AreEqual(RejectReason.MatchStarted, late.Client.Rejection);
            Assert.AreEqual(3, rig.Room.Match.PlayerCount);
        }

        // ----- A hostile or broken player -----

        [Test]
        public void MessagesFromStrangersAndOutOfPlaceAreIgnored()
        {
            var rig = new Rig().WithPlayers(3);
            INetClientPort port;
            rig.Net.Connect(out port); // Connected, but never said hello.
            rig.Room.Start();
            port.SendToHost(NetMessage.SubmitDrawing(new byte[] { 9 }));
            port.SendToHost(NetMessage.Vote(true));
            port.SendToHost(NetMessage.Advance());
            rig.Step(0f);
            Assert.IsTrue(rig.RunUntil(() => rig.Everyone.All(b => b.Client.Phase == MatchPhase.GameEnd)));
            Assert.AreEqual(3, rig.Room.Match.PlayerCount);
        }

        [Test]
        public void AHugeDrawingIsTreatedAsBlank()
        {
            var rig = new Rig().WithPlayers(2);
            foreach (OnlineBot bot in rig.Everyone)
            {
                bot.Silent = true;
            }
            rig.Begin();
            rig.HostBot.Client.CompleteSpin(0);
            rig.HostBot.Client.ConfirmSpin();
            rig.Bots[0].Client.CompleteSpin(0);
            rig.Bots[0].Client.ConfirmSpin();
            rig.RunUntil(() => rig.HostBot.Client.Phase == MatchPhase.Turn, 20f);
            rig.HostBot.Client.SubmitDrawing(new byte[OnlineMatchHost.MaxDrawingBytes + 1]);
            rig.Bots[0].Client.SubmitDrawing(new byte[] { 1, 2, 3 });
            rig.Step(0f);
            ChainState chain = rig.Room.Match.Chains[0];
            Assert.AreEqual(0, chain.Entries[0].Drawing.Length);
        }

        [Test]
        public void ABigDrawingCrossesTheNetworkIntact()
        {
            var rig = new Rig().WithPlayers(2);
            foreach (OnlineBot bot in rig.Everyone)
            {
                bot.Silent = true;
            }
            rig.Begin();
            var big = new byte[300000];
            for (int i = 0; i < big.Length; i++)
            {
                big[i] = (byte)(i * 31);
            }
            rig.HostBot.Client.CompleteSpin(0);
            rig.HostBot.Client.ConfirmSpin();
            rig.Bots[0].Client.CompleteSpin(0);
            rig.Bots[0].Client.ConfirmSpin();
            rig.RunUntil(() => rig.HostBot.Client.Phase == MatchPhase.Turn, 20f);
            rig.HostBot.Client.SubmitDrawing(big);
            rig.Bots[0].Client.SubmitDrawing(new byte[0]);
            rig.Step(0f);
            Assert.IsTrue(rig.RunUntil(() => rig.Bots[0].Client.Phase == MatchPhase.Turn
                && rig.Bots[0].Client.TurnKind == StageKind.Guess));
            CollectionAssert.AreEqual(big, rig.Bots[0].Client.PromptDrawing);
        }
    }
}
