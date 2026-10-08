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
using NUnit.Framework;

namespace Telewheel.Tests
{
    public class ChainPlannerTests
    {
        private static IEnumerable<int> PlayerCounts()
        {
            for (int n = 2; n <= 10; n++)
            {
                yield return n;
            }
        }

        [Test]
        public void RejectsFewerThanTwoPlayers()
        {
            Assert.Throws<System.ArgumentOutOfRangeException>(() => new ChainPlanner(1));
        }

        [Test]
        public void EvenCountMatchesTheSpecExample()
        {
            // Spec: players 1 2 3 4 -> D G D G for the chain started by player 1.
            var planner = new ChainPlanner(4);
            Assert.AreEqual(4, planner.TurnsPerChain);
            for (int turn = 0; turn < 4; turn++)
            {
                Assert.AreEqual(turn, planner.PlayerForTurn(0, turn));
                Assert.AreEqual(turn % 2 == 0 ? StageKind.Draw : StageKind.Guess, planner.KindOfTurn(turn));
            }
            // "Player 1 gets drawing from player 4": on turn 1 player 0 works on chain 3.
            Assert.AreEqual(3, planner.ChainForTurn(0, 1));
        }

        [Test]
        public void OddCountMatchesTheSpecExample()
        {
            // Spec: players 1 2 3 4 5 -> X D G D G; the owner does not draw.
            var planner = new ChainPlanner(5);
            Assert.AreEqual(4, planner.TurnsPerChain);
            Assert.AreEqual(1, planner.PlayerForTurn(0, 0));
            Assert.AreEqual(2, planner.PlayerForTurn(0, 1));
            Assert.AreEqual(3, planner.PlayerForTurn(0, 2));
            Assert.AreEqual(4, planner.PlayerForTurn(0, 3));
            Assert.AreEqual(StageKind.Draw, planner.KindOfTurn(0));
            Assert.AreEqual(StageKind.Guess, planner.KindOfTurn(3));
        }

        [TestCaseSource("PlayerCounts")]
        public void EveryTurnIsAPermutationOfPlayersAndChains(int n)
        {
            var planner = new ChainPlanner(n);
            for (int turn = 0; turn < planner.TurnsPerChain; turn++)
            {
                var chains = new HashSet<int>();
                for (int player = 0; player < n; player++)
                {
                    int chain = planner.ChainForTurn(player, turn);
                    Assert.IsTrue(chains.Add(chain), "two players share chain " + chain + " on turn " + turn);
                    Assert.AreEqual(player, planner.PlayerForTurn(chain, turn));
                }
                Assert.AreEqual(n, chains.Count);
            }
        }

        [TestCaseSource("PlayerCounts")]
        public void ChainsEndOnAGuessBackAtTheOwner(int n)
        {
            var planner = new ChainPlanner(n);
            int last = planner.TurnsPerChain - 1;
            Assert.AreEqual(StageKind.Guess, planner.KindOfTurn(last));
            Assert.AreEqual(StageKind.Draw, planner.KindOfTurn(0));
            for (int chain = 0; chain < n; chain++)
            {
                // The final guess comes from the player just before the owner.
                Assert.AreEqual((chain + n - 1) % n, planner.FinalGuesser(chain));
            }
        }

        [TestCaseSource("PlayerCounts")]
        public void NoPlayerWorksTheSameChainTwiceAndOddOwnersSitOut(int n)
        {
            var planner = new ChainPlanner(n);
            for (int chain = 0; chain < n; chain++)
            {
                var seen = new HashSet<int>();
                for (int turn = 0; turn < planner.TurnsPerChain; turn++)
                {
                    Assert.IsTrue(seen.Add(planner.PlayerForTurn(chain, turn)));
                }
                if (planner.IsEven)
                {
                    Assert.AreEqual(n, seen.Count);
                }
                else
                {
                    Assert.AreEqual(n - 1, seen.Count);
                    Assert.IsFalse(seen.Contains(chain), "owner " + chain + " should not play their own chain");
                }
            }
        }

        [TestCaseSource("PlayerCounts")]
        public void SequentialScheduleCoversEveryStageOnce(int n)
        {
            var planner = new ChainPlanner(n);
            IReadOnlyList<Stage> schedule = planner.BuildSequentialSchedule();
            Assert.AreEqual(n + n * planner.TurnsPerChain, schedule.Count);

            var spins = new HashSet<int>();
            var turns = new HashSet<string>();
            foreach (Stage stage in schedule)
            {
                if (stage.Kind == StageKind.Spin)
                {
                    Assert.AreEqual(stage.Player, stage.Chain);
                    Assert.IsTrue(spins.Add(stage.Player));
                }
                else
                {
                    Assert.AreEqual(planner.KindOfTurn(stage.Turn), stage.Kind);
                    Assert.AreEqual(planner.PlayerForTurn(stage.Chain, stage.Turn), stage.Player);
                    Assert.IsTrue(turns.Add(stage.Chain + ":" + stage.Turn));
                }
            }
            Assert.AreEqual(n, spins.Count);
            Assert.AreEqual(n * planner.TurnsPerChain, turns.Count);
        }

        [TestCaseSource("PlayerCounts")]
        public void SequentialScheduleNeverUsesAChainBeforeItIsReady(int n)
        {
            var planner = new ChainPlanner(n);
            var spun = new HashSet<int>();
            var nextTurn = new int[n];
            foreach (Stage stage in planner.BuildSequentialSchedule())
            {
                if (stage.Kind == StageKind.Spin)
                {
                    spun.Add(stage.Chain);
                    continue;
                }
                Assert.IsTrue(spun.Contains(stage.Chain), "chain " + stage.Chain + " has no word yet");
                Assert.AreEqual(nextTurn[stage.Chain], stage.Turn, "turns on a chain must be in order");
                nextTurn[stage.Chain]++;
            }
        }

        [Test]
        public void EvenScheduleLetsOwnersDrawStraightAfterSpinning()
        {
            IReadOnlyList<Stage> schedule = new ChainPlanner(4).BuildSequentialSchedule();
            Assert.AreEqual(StageKind.Spin, schedule[0].Kind);
            Assert.AreEqual(StageKind.Draw, schedule[1].Kind);
            Assert.AreEqual(schedule[0].Player, schedule[1].Player);
            Assert.AreEqual(schedule[0].Chain, schedule[1].Chain);
        }

        [Test]
        public void OddScheduleSpinsEveryoneBeforeAnyDrawing()
        {
            IReadOnlyList<Stage> schedule = new ChainPlanner(5).BuildSequentialSchedule();
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(StageKind.Spin, schedule[i].Kind);
            }
            Assert.AreEqual(StageKind.Draw, schedule[5].Kind);
            // Player 0 draws the word that player 4 spun.
            Assert.AreEqual(0, schedule[5].Player);
            Assert.AreEqual(4, schedule[5].Chain);
        }
    }
}
