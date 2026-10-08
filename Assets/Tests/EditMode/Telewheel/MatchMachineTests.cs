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
using NUnit.Framework;

namespace Telewheel.Tests
{
    public class MatchMachineTests
    {
        private static IEnumerable<int> PlayerCounts()
        {
            for (int n = 2; n <= 8; n++)
            {
                yield return n;
            }
        }

        private static List<string> Words()
        {
            var words = new List<string>();
            for (int i = 0; i < 40; i++)
            {
                words.Add("word" + i);
            }
            return words;
        }

        private static MatchMachine NewMachine(int players, int rounds = 1, bool sharedVote = true, int seed = 11)
        {
            var settings = new MatchSettings
            {
                PlayerCount = players,
                Rounds = rounds,
                SharedVote = sharedVote,
                Seed = seed,
            };
            return new MatchMachine(settings, new WordDeck(Words(), seed));
        }

        private static byte[] DrawingFor(Stage stage)
        {
            return new[] { (byte)stage.Chain, (byte)stage.Turn, (byte)stage.Player };
        }

        /// <summary>Plays one stage the way a player would, recording what each prompt showed.</summary>
        private sealed class Player
        {
            public readonly List<string> Log = new List<string>();
            public Func<ChainState, bool> Vote = chain => true;
            public int HandoffCount;

            public void PlayToEnd(MatchMachine machine)
            {
                int safety = 0;
                while (machine.Phase != MatchPhase.GameEnd)
                {
                    Assert.Less(safety++, 5000, "match did not finish");
                    Step(machine);
                }
            }

            public void Step(MatchMachine machine)
            {
                switch (machine.Phase)
                {
                    case MatchPhase.Lobby:
                        machine.Start();
                        break;
                    case MatchPhase.Handoff:
                        HandoffCount++;
                        Assert.IsNull(machine.PromptText, "prompt must stay hidden during a hand-off");
                        Assert.IsNull(machine.PromptDrawing, "prompt must stay hidden during a hand-off");
                        machine.ConfirmHandoff();
                        break;
                    case MatchPhase.Spin:
                        machine.CompleteSpin(machine.CurrentStage.Player % machine.WheelWords.Count);
                        machine.ConfirmSpin();
                        break;
                    case MatchPhase.Countdown:
                        machine.SkipCountdown();
                        break;
                    case MatchPhase.Turn:
                        PlayTurn(machine);
                        break;
                    case MatchPhase.Present:
                        machine.SkipPresent();
                        break;
                    case MatchPhase.Vote:
                        machine.CastVote(0, Vote(machine.Chains[machine.PresentChain]));
                        break;
                    case MatchPhase.VoteResult:
                        machine.ContinueAfterVote();
                        break;
                    case MatchPhase.RoundEnd:
                        machine.ContinueRound();
                        break;
                }
            }

            private void PlayTurn(MatchMachine machine)
            {
                Stage stage = machine.CurrentStage;
                ChainState chain = machine.Chains[stage.Chain];
                if (stage.Kind == StageKind.Draw)
                {
                    string expected = stage.Turn == 0 ? chain.Word : chain.Entries[stage.Turn - 1].Text;
                    Assert.IsNotNull(expected);
                    Assert.AreEqual(expected, machine.PromptText, "draw prompt");
                    Assert.IsNull(machine.PromptDrawing);
                    machine.SubmitDrawing(DrawingFor(stage));
                }
                else
                {
                    CollectionAssert.AreEqual(
                        chain.Entries[stage.Turn - 1].Drawing, machine.PromptDrawing, "guess prompt");
                    Assert.IsNull(machine.PromptText);
                    Assert.IsTrue(machine.SubmitGuess("guess " + stage.Player + " " + stage.Chain));
                }
            }
        }

        [Test]
        public void StartsInTheLobbyAndRejectsOutOfOrderCommands()
        {
            MatchMachine machine = NewMachine(3);
            Assert.AreEqual(MatchPhase.Lobby, machine.Phase);
            Assert.Throws<InvalidOperationException>(machine.ConfirmHandoff);
            Assert.Throws<InvalidOperationException>(() => machine.SubmitDrawing(null));
            Assert.Throws<InvalidOperationException>(() => machine.CastVote(0, true));
            machine.Start();
            Assert.AreEqual(MatchPhase.Handoff, machine.Phase);
            Assert.AreEqual(0, machine.ActivePlayer);
            Assert.Throws<InvalidOperationException>(machine.Start);
        }

        [Test]
        public void SpinNeedsAWordBeforeItCanBeConfirmed()
        {
            MatchMachine machine = NewMachine(4);
            machine.Start();
            machine.ConfirmHandoff();
            Assert.AreEqual(MatchPhase.Spin, machine.Phase);
            Assert.AreEqual(machine.Settings.WheelSegments, machine.WheelWords.Count);
            Assert.Throws<InvalidOperationException>(machine.ConfirmSpin);
            Assert.Throws<ArgumentOutOfRangeException>(() => machine.CompleteSpin(99));
            machine.CompleteSpin(2);
            Assert.AreEqual(machine.WheelWords[2], machine.SpinWord);
            machine.CompleteSpin(5);
            Assert.AreEqual(machine.WheelWords[2], machine.SpinWord, "the first landing counts");
        }

        [Test]
        public void EvenCountPlayersDrawStraightAfterSpinning()
        {
            MatchMachine machine = NewMachine(4);
            machine.Start();
            machine.ConfirmHandoff();
            machine.CompleteSpin(0);
            machine.ConfirmSpin();
            Assert.AreEqual(MatchPhase.Countdown, machine.Phase, "no hand-off between spin and draw");
            Assert.AreEqual(0, machine.ActivePlayer);
        }

        [Test]
        public void OddCountPassesTheHeadsetAfterSpinning()
        {
            MatchMachine machine = NewMachine(3);
            machine.Start();
            machine.ConfirmHandoff();
            machine.CompleteSpin(0);
            machine.ConfirmSpin();
            Assert.AreEqual(MatchPhase.Handoff, machine.Phase);
            Assert.AreEqual(1, machine.ActivePlayer);
        }

        [Test]
        public void CountdownThenTurnRunOnTheClock()
        {
            MatchMachine machine = NewMachine(2);
            machine.Start();
            machine.ConfirmHandoff();
            machine.CompleteSpin(0);
            machine.ConfirmSpin();
            Assert.AreEqual(MatchPhase.Countdown, machine.Phase);
            Assert.AreEqual(5f, machine.Clock.Total);
            machine.Tick(4.9f);
            Assert.AreEqual(MatchPhase.Countdown, machine.Phase);
            machine.Tick(0.2f);
            Assert.AreEqual(MatchPhase.Turn, machine.Phase);
            Assert.AreEqual(60f, machine.Clock.Total);
            Assert.AreEqual(60f, machine.Clock.Remaining, 0.001f);
        }

        [Test]
        public void TurnTimeoutRaisesTheEventOnceAndWaitsForTheSubmission()
        {
            MatchMachine machine = NewMachine(2);
            int expired = 0;
            machine.TurnExpired += () => expired++;
            machine.Start();
            machine.ConfirmHandoff();
            machine.CompleteSpin(0);
            machine.ConfirmSpin();
            machine.SkipCountdown();
            machine.Tick(59f);
            Assert.AreEqual(0, expired);
            Assert.IsFalse(machine.TimedOut);
            machine.Tick(2f);
            machine.Tick(5f);
            Assert.AreEqual(1, expired);
            Assert.IsTrue(machine.TimedOut);
            Assert.AreEqual(MatchPhase.Turn, machine.Phase, "waits for the engine to hand over the drawing");
            machine.SubmitDrawing(null);
            Assert.IsFalse(machine.TimedOut);
            Assert.AreEqual(MatchPhase.Handoff, machine.Phase);
            Assert.AreEqual(0, machine.Chains[0].Entries[0].Drawing.Length);
        }

        [Test]
        public void EmptyGuessIsRefusedUntilTheTurnTimesOut()
        {
            MatchMachine machine = NewMachine(2);
            var player = new Player();
            while (!(machine.Phase == MatchPhase.Turn && machine.CurrentStage.Kind == StageKind.Guess))
            {
                player.Step(machine);
            }
            Assert.IsFalse(machine.SubmitGuess("   "));
            Assert.AreEqual(MatchPhase.Turn, machine.Phase);
            machine.Tick(61f);
            Assert.IsTrue(machine.SubmitGuess(""));
            Assert.AreEqual(TwCopy.NoGuess, machine.Chains[machine.Chains.Count - 1].Entries.Count > 0
                ? LastGuess(machine)
                : null);
        }

        private static string LastGuess(MatchMachine machine)
        {
            string last = null;
            foreach (ChainState chain in machine.Chains)
            {
                foreach (ChainEntry entry in chain.Entries)
                {
                    if (entry.Kind == StageKind.Guess)
                    {
                        last = entry.Text;
                    }
                }
            }
            return last;
        }

        [Test]
        public void WrongKindOfSubmissionIsRejected()
        {
            MatchMachine machine = NewMachine(2);
            machine.Start();
            machine.ConfirmHandoff();
            machine.CompleteSpin(0);
            machine.ConfirmSpin();
            machine.SkipCountdown();
            Assert.Throws<InvalidOperationException>(() => machine.SubmitGuess("nope"));
        }

        [TestCaseSource("PlayerCounts")]
        public void EveryChainIsCompleteAndFinishesWithAGuess(int n)
        {
            MatchMachine machine = NewMachine(n);
            new Player().PlayToEnd(machine);
            Assert.AreEqual(MatchPhase.GameEnd, machine.Phase);
            ChainPlanner planner = machine.Planner;
            foreach (ChainState chain in machine.Chains)
            {
                Assert.IsNotNull(chain.Word);
                Assert.AreEqual(planner.TurnsPerChain, chain.Entries.Count);
                for (int turn = 0; turn < chain.Entries.Count; turn++)
                {
                    Assert.AreEqual(planner.KindOfTurn(turn), chain.Entries[turn].Kind);
                    Assert.AreEqual(planner.PlayerForTurn(chain.Owner, turn), chain.Entries[turn].Player);
                }
                Assert.AreEqual(StageKind.Guess, chain.Entries[chain.Entries.Count - 1].Kind);
                Assert.AreEqual(planner.FinalGuesser(chain.Owner), chain.Entries[chain.Entries.Count - 1].Player);
            }
        }

        [TestCaseSource("PlayerCounts")]
        public void WordsAreUniqueWithinARound(int n)
        {
            MatchMachine machine = NewMachine(n);
            new Player().PlayToEnd(machine);
            var words = new HashSet<string>();
            foreach (ChainState chain in machine.Chains)
            {
                Assert.IsTrue(words.Add(chain.Word), "word repeated: " + chain.Word);
            }
        }

        [TestCaseSource("PlayerCounts")]
        public void OnlyTheChainOwnerScoresWhenTheVoteLands(int n)
        {
            MatchMachine machine = NewMachine(n, 1);
            // Chains owned by even players land; odd players' chains do not.
            new Player { Vote = chain => chain.Owner % 2 == 0 }.PlayToEnd(machine);
            for (int player = 0; player < n; player++)
            {
                Assert.AreEqual(player % 2 == 0 ? 1 : 0, machine.Scores[player], "score of player " + player);
            }
        }

        [Test]
        public void ScoresAddUpAcrossRoundsAndTheLeaderWins()
        {
            MatchMachine machine = NewMachine(4, 3);
            new Player { Vote = chain => chain.Owner == 2 }.PlayToEnd(machine);
            Assert.AreEqual(3, machine.Round);
            Assert.AreEqual(3, machine.Scores[2]);
            Assert.AreEqual(0, machine.Scores[0]);
            CollectionAssert.AreEqual(new[] { 2 }, machine.Leaders);
        }

        [Test]
        public void TiedLeadersAreAllReported()
        {
            MatchMachine machine = NewMachine(3, 1);
            new Player { Vote = chain => chain.Owner != 1 }.PlayToEnd(machine);
            CollectionAssert.AreEqual(new[] { 0, 2 }, machine.Leaders);
        }

        [Test]
        public void RoundEndComesBeforeTheNextRoundAndTheFinalRoundEndsTheGame()
        {
            MatchMachine machine = NewMachine(2, 2);
            var player = new Player();
            var phases = new List<MatchPhase>();
            machine.PhaseChanged += (from, to) =>
            {
                if (to == MatchPhase.RoundEnd || to == MatchPhase.GameEnd)
                {
                    phases.Add(to);
                }
            };
            player.PlayToEnd(machine);
            CollectionAssert.AreEqual(
                new[] { MatchPhase.RoundEnd, MatchPhase.RoundEnd, MatchPhase.GameEnd }, phases);
        }

        [Test]
        public void EachRoundStartsFreshChains()
        {
            MatchMachine machine = NewMachine(3, 2);
            var player = new Player();
            while (machine.Phase != MatchPhase.RoundEnd)
            {
                player.Step(machine);
            }
            Assert.IsFalse(machine.IsFinalRound);
            machine.ContinueRound();
            Assert.AreEqual(2, machine.Round);
            Assert.AreEqual(MatchPhase.Handoff, machine.Phase);
            foreach (ChainState chain in machine.Chains)
            {
                Assert.IsNull(chain.Word);
                Assert.AreEqual(0, chain.Entries.Count);
            }
        }

        [Test]
        public void PresentAutoAdvancesThenVotesThenShowsTheResult()
        {
            MatchMachine machine = NewMachine(2);
            var player = new Player();
            while (machine.Phase != MatchPhase.Present)
            {
                player.Step(machine);
            }
            Assert.AreEqual(0, machine.PresentChain);
            Assert.AreEqual(PresentItemKind.Word, machine.CurrentPresentItem.Kind);
            Assert.AreEqual(15f, machine.Clock.Total);
            machine.Tick(15.1f);
            Assert.AreEqual(1, machine.PresentIndex);
            Assert.AreEqual(PresentItemKind.Drawing, machine.CurrentPresentItem.Kind);
            machine.Tick(15.1f);
            Assert.AreEqual(MatchPhase.Vote, machine.Phase);
            Assert.AreEqual(PresentItemKind.Guess, machine.CurrentPresentItem.Kind);
            Assert.AreEqual(30f, machine.Clock.Total);
            machine.CastVote(0, true);
            Assert.AreEqual(MatchPhase.VoteResult, machine.Phase);
            Assert.IsTrue(machine.LastVoteLanded);
            Assert.AreEqual(1, machine.Scores[0]);
            machine.Tick(4.1f);
            Assert.AreEqual(MatchPhase.Present, machine.Phase);
            Assert.AreEqual(1, machine.PresentChain);
        }

        [Test]
        public void VoteTimingOutWithNoVotesMeansNoPoint()
        {
            MatchMachine machine = NewMachine(2);
            var player = new Player();
            while (machine.Phase != MatchPhase.Vote)
            {
                player.Step(machine);
            }
            machine.Tick(31f);
            Assert.AreEqual(MatchPhase.VoteResult, machine.Phase);
            Assert.IsFalse(machine.LastVoteLanded);
            Assert.AreEqual(0, machine.Scores[0]);
        }

        [Test]
        public void FullRoomVoteNeedsEveryoneOrATimeoutAndAStrictMajority()
        {
            MatchMachine machine = NewMachine(4, 1, false);
            var player = new Player();
            while (machine.Phase != MatchPhase.Vote)
            {
                player.Step(machine);
            }
            machine.CastVote(0, true);
            machine.CastVote(1, true);
            Assert.AreEqual(MatchPhase.Vote, machine.Phase, "still waiting on the others");
            machine.CastVote(2, false);
            machine.CastVote(3, false);
            Assert.AreEqual(MatchPhase.VoteResult, machine.Phase);
            Assert.IsFalse(machine.LastVoteLanded, "2 of 4 is not a majority");
        }

        [Test]
        public void SkippingPresentGoesStraightToTheVote()
        {
            MatchMachine machine = NewMachine(4);
            var player = new Player();
            while (machine.Phase != MatchPhase.Present)
            {
                player.Step(machine);
            }
            int guard = 0;
            while (machine.Phase == MatchPhase.Present)
            {
                machine.SkipPresent();
                Assert.Less(guard++, 20);
            }
            Assert.AreEqual(MatchPhase.Vote, machine.Phase);
            Assert.AreEqual(machine.PresentItems.Count - 1, machine.PresentIndex);
        }

        [Test]
        public void ResetReturnsToTheLobbyWithZeroScores()
        {
            MatchMachine machine = NewMachine(3, 1);
            new Player().PlayToEnd(machine);
            Assert.Greater(machine.Scores[0] + machine.Scores[1] + machine.Scores[2], 0);
            machine.Reset();
            Assert.AreEqual(MatchPhase.Lobby, machine.Phase);
            Assert.AreEqual(0, machine.Scores[0] + machine.Scores[1] + machine.Scores[2]);
            new Player().PlayToEnd(machine);
            Assert.AreEqual(MatchPhase.GameEnd, machine.Phase);
        }

        [Test]
        public void SameSeedReplaysTheSameMatch()
        {
            MatchMachine a = NewMachine(5, 2, true, 1234);
            MatchMachine b = NewMachine(5, 2, true, 1234);
            new Player().PlayToEnd(a);
            new Player().PlayToEnd(b);
            Assert.AreEqual(a.Chains.Count, b.Chains.Count);
            for (int i = 0; i < a.Chains.Count; i++)
            {
                Assert.AreEqual(a.Chains[i].Word, b.Chains[i].Word);
            }
        }

        [TestCaseSource("PlayerCounts")]
        public void HandoffsHappenWheneverTheHeadsetChangesHands(int n)
        {
            MatchMachine machine = NewMachine(n);
            var player = new Player();
            player.PlayToEnd(machine);
            // Even: n spin+draw blocks then (turns-1) passes of n players. Odd: n spins then turns passes of n.
            int stages = machine.Planner.BuildSequentialSchedule().Count;
            int changes = 0;
            IReadOnlyList<Stage> schedule = machine.Planner.BuildSequentialSchedule();
            for (int i = 0; i < stages; i++)
            {
                if (i == 0 || schedule[i].Player != schedule[i - 1].Player)
                {
                    changes++;
                }
            }
            Assert.AreEqual(changes, player.HandoffCount);
        }
    }
}
