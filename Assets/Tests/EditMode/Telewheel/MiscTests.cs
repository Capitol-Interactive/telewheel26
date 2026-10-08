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

using NUnit.Framework;

namespace Telewheel.Tests
{
    public class MiscTests
    {
        [Test]
        public void ClockCountsDownAndExpiresOnce()
        {
            var clock = new TurnClock();
            clock.Start(2f);
            Assert.IsFalse(clock.Tick(1f));
            Assert.AreEqual(1f, clock.Remaining, 0.001f);
            Assert.IsTrue(clock.Tick(1.5f));
            Assert.IsTrue(clock.Expired);
            Assert.IsFalse(clock.Tick(1f), "expiry must be reported once");
            Assert.AreEqual(0f, clock.Remaining);
        }

        [Test]
        public void ClockWarnsInTheLastTenSecondsOnly()
        {
            var clock = new TurnClock();
            clock.Start(60f);
            Assert.IsFalse(clock.Warning);
            clock.Tick(49f);
            Assert.IsFalse(clock.Warning);
            clock.Tick(1f);
            Assert.IsTrue(clock.Warning);
            clock.Tick(10f);
            Assert.IsFalse(clock.Warning, "no warning once expired");
        }

        [Test]
        public void ClockShowsWholeSecondsRoundedUp()
        {
            var clock = new TurnClock();
            clock.Start(60f);
            clock.Tick(0.2f);
            Assert.AreEqual(60, clock.WholeSeconds);
            clock.Tick(59.7f);
            Assert.AreEqual(1, clock.WholeSeconds);
        }

        [Test]
        public void StoppedClockIgnoresTime()
        {
            var clock = new TurnClock();
            clock.Start(5f);
            clock.Stop();
            Assert.IsFalse(clock.Tick(10f));
            Assert.AreEqual(5f, clock.Remaining);
        }

        [TestCase("  hello   world ", "hello world")]
        [TestCase("tab\tseparated", "tab separated")]
        [TestCase("new\nline", "new line")]
        [TestCase("bell\u0007ring", "bellring")]
        [TestCase("", "")]
        [TestCase("   ", "")]
        public void GuessIsNormalized(string raw, string expected)
        {
            Assert.AreEqual(expected, GuessNormalizer.Normalize(raw));
        }

        [Test]
        public void GuessIsCapped()
        {
            string longGuess = new string('a', 200);
            Assert.AreEqual(GuessNormalizer.MaxLength, GuessNormalizer.Normalize(longGuess).Length);
            Assert.IsFalse(GuessNormalizer.IsAcceptable(null));
            Assert.IsTrue(GuessNormalizer.IsAcceptable("x"));
        }

        [Test]
        public void VoteNeedsAStrictMajorityOfAllVoters()
        {
            var tally = new VoteTally(4);
            tally.Cast(0, true);
            tally.Cast(1, true);
            Assert.IsFalse(tally.Landed, "2 of 4 is a tie, not a majority");
            tally.Cast(2, true);
            Assert.IsTrue(tally.Landed);
            Assert.IsFalse(tally.Complete);
            tally.Cast(3, false);
            Assert.IsTrue(tally.Complete);
            Assert.AreEqual(3, tally.Yes);
            Assert.AreEqual(1, tally.No);
        }

        [Test]
        public void VotersCanChangeTheirMind()
        {
            var tally = new VoteTally(3);
            tally.Cast(0, true);
            tally.Cast(0, false);
            Assert.AreEqual(0, tally.Yes);
            Assert.AreEqual(1, tally.No);
            Assert.AreEqual(1, tally.CastCount);
            Assert.IsFalse(tally.Cast(5, true));
        }

        [Test]
        public void SettingsValidateRanges()
        {
            var ok = new MatchSettings();
            Assert.DoesNotThrow(ok.Validate);
            Assert.Throws<System.ArgumentException>(new MatchSettings { PlayerCount = 1 }.Validate);
            Assert.Throws<System.ArgumentException>(new MatchSettings { PlayerCount = 9 }.Validate);
            Assert.Throws<System.ArgumentException>(new MatchSettings { Rounds = 0 }.Validate);
            Assert.Throws<System.ArgumentException>(new MatchSettings { Rounds = 6 }.Validate);
            Assert.Throws<System.ArgumentException>(new MatchSettings { DrawSeconds = 0 }.Validate);
        }

        [Test]
        public void PlayerNamesFallBackToNumbers()
        {
            var settings = new MatchSettings { PlayerNames = new[] { "Sam", " ", null } };
            Assert.AreEqual("Sam", settings.NameOf(0));
            Assert.AreEqual("Player 2", settings.NameOf(1));
            Assert.AreEqual("Player 3", settings.NameOf(2));
            Assert.AreEqual("Player 4", settings.NameOf(3));
        }

        [Test]
        public void SharedVoteCountsAsOneVoter()
        {
            Assert.AreEqual(1, new MatchSettings { SharedVote = true, PlayerCount = 6 }.VoterCount);
            Assert.AreEqual(6, new MatchSettings { SharedVote = false, PlayerCount = 6 }.VoterCount);
        }

        [Test]
        public void TokensSplitColours()
        {
            Assert.AreEqual(0xFB, TwTokens.R(TwTokens.Sun));
            Assert.AreEqual(0xEB, TwTokens.G(TwTokens.Sun));
            Assert.AreEqual(0x1B, TwTokens.B(TwTokens.Sun));
            Assert.AreEqual(6, TwTokens.WheelHues.Length);
        }
    }
}
