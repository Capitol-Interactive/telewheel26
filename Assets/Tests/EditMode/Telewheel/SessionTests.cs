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
    public class SessionTests
    {
        private static NetMessage RoundTrip(NetMessage message)
        {
            return NetCodec.Decode(NetCodec.Encode(message));
        }

        // ----- Wire format of the lobby messages -----

        [Test]
        public void HelloSurvivesTheWire()
        {
            NetMessage back = RoundTrip(NetMessage.Hello("Ann", 3, 7));
            Assert.AreEqual(NetKind.Hello, back.Kind);
            Assert.AreEqual("Ann", back.Text);
            Assert.AreEqual(3, back.A);
            Assert.AreEqual(7, back.B);
        }

        [Test]
        public void LobbyStateSurvivesTheWire()
        {
            NetMessage back = RoundTrip(NetMessage.LobbyState(
                2, 3, ContentFilter.Raunchy, new[] { "A", "B", "C" }, new[] { 1, 2, 3 }, "space"));
            Assert.AreEqual(NetKind.LobbyState, back.Kind);
            Assert.AreEqual(2, back.A);
            Assert.AreEqual(3, back.B);
            Assert.AreEqual((int)ContentFilter.Raunchy, back.C);
            CollectionAssert.AreEqual(new[] { "A", "B", "C" }, back.Words);
            CollectionAssert.AreEqual(new[] { 1, 2, 3 }, back.Numbers);
            Assert.AreEqual("space", back.Text);
        }

        [Test]
        public void TheSmallNoticesSurviveTheWire()
        {
            Assert.AreEqual((int)RejectReason.RoomFull, RoundTrip(NetMessage.Rejected(RejectReason.RoomFull)).A);
            Assert.AreEqual(4, RoundTrip(NetMessage.PlayerLeft(4)).A);
            Assert.AreEqual("beach", RoundTrip(NetMessage.Environment("beach")).Text);
            Assert.AreEqual(0b1011, RoundTrip(NetMessage.Progress(0b1011)).A);
            Assert.AreEqual((int)EndReason.NotEnoughPlayers, RoundTrip(NetMessage.MatchEnded(EndReason.NotEnoughPlayers)).A);
        }

        // ----- Profiles and scores -----

        [Test]
        public void ProfileNamesAreCleanedAndShortened()
        {
            Assert.AreEqual("Ann Lee", PlayerProfile.CleanName("  Ann \t  Lee \n"));
            Assert.AreEqual(string.Empty, PlayerProfile.CleanName(null));
            Assert.AreEqual(PlayerProfile.MaxNameLength, PlayerProfile.CleanName(new string('z', 50)).Length);
            Assert.AreEqual("Long Name Here", PlayerProfile.CleanName("Long Name Here"));
        }

        [Test]
        public void ProfileIconsWrapOntoThePalette()
        {
            Assert.AreEqual(0, PlayerProfile.CleanIcon(PlayerProfile.IconCount));
            Assert.AreEqual(PlayerProfile.IconCount - 1, PlayerProfile.CleanIcon(-1));
            Assert.AreEqual(3, PlayerProfile.CleanIcon(3));
        }

        [Test]
        public void LeadersIncludeEveryoneOnATie()
        {
            CollectionAssert.AreEqual(new[] { 1, 3 }, ScoreMath.Leaders(new[] { 2, 5, 1, 5 }).ToArray());
            CollectionAssert.AreEqual(new[] { 0 }, ScoreMath.Leaders(new[] { 0, 0, 0 }.Take(1).ToArray()).ToArray());
            CollectionAssert.AreEqual(new[] { 0, 1, 2 }, ScoreMath.Leaders(new[] { 0, 0, 0 }).ToArray());
        }

        [Test]
        public void NamesGetCapitalLettersWithoutLoweringAny()
        {
            Assert.AreEqual("Ann Lee", PlayerProfile.Capitalize("ann lee"));
            Assert.AreEqual("McDonald", PlayerProfile.Capitalize("mcDonald"));
            Assert.AreEqual("Mary-Jane", PlayerProfile.Capitalize("mary-jane"));
            Assert.AreEqual(string.Empty, PlayerProfile.Capitalize(null));
        }

        [Test]
        public void ThereIsOneColourForEveryIcon()
        {
            Assert.AreEqual(PlayerProfile.IconCount, TwTokens.PlayerColors.Length);
            CollectionAssert.AllItemsAreUnique(TwTokens.PlayerColors);
        }

        [Test]
        public void TheWaitingLineNamesWhoIsLeft()
        {
            Assert.AreEqual(TwCopy.HereWeGo, TwCopy.WaitingFor(new string[0]));
            Assert.AreEqual("Waiting for Ann", TwCopy.WaitingFor(new[] { "Ann" }));
            Assert.AreEqual("Waiting for Ann and Bo", TwCopy.WaitingFor(new[] { "Ann", "Bo" }));
            Assert.AreEqual("Waiting for Ann, Bo and Cy", TwCopy.WaitingFor(new[] { "Ann", "Bo", "Cy" }));
            Assert.AreEqual("Waiting for Ann, Bo and 3 more", TwCopy.WaitingFor(new[] { "Ann", "Bo", "Cy", "Di", "Ed" }));
        }

        [Test]
        public void EveryRejectionAndEndingHasAMessage()
        {
            foreach (RejectReason reason in System.Enum.GetValues(typeof(RejectReason)))
            {
                Assert.IsNotEmpty(TwCopy.RejectedLine(reason));
            }
            foreach (EndReason reason in System.Enum.GetValues(typeof(EndReason)))
            {
                Assert.IsNotEmpty(TwCopy.EndedLine(reason));
            }
            StringAssert.Contains("Ann left", TwCopy.PlayerLeftLine("Ann"));
        }

        // ----- Pass & Play through the shared interface -----

        private static PassAndPlaySession NewPassAndPlay(int players)
        {
            var settings = new MatchSettings { PlayerCount = players, Rounds = 1, PlayerNames = new[] { "Ann", "Bo", "Cy", "Di" } };
            var words = new List<string>();
            for (int i = 0; i < 40; i++)
            {
                words.Add("w" + i);
            }
            return new PassAndPlaySession(new MatchMachine(settings, new WordDeck(words, 1)));
        }

        [Test]
        public void ThePassAndPlayViewFollowsTheMachine()
        {
            PassAndPlaySession session = NewPassAndPlay(4);
            var phases = new List<MatchPhase>();
            session.PhaseChanged += (from, to) => phases.Add(to);
            Assert.IsFalse(session.IsOnline);
            Assert.AreEqual(4, session.PlayerCount);
            Assert.AreEqual("Bo", session.NameOf(1));

            session.Machine.Start();
            Assert.AreEqual(MatchPhase.Handoff, session.Phase);
            Assert.AreEqual(0, session.ActiveSeat);
            session.ConfirmHandoff();
            Assert.AreEqual(MatchPhase.Spin, session.Phase);
            Assert.IsNull(session.SpinWord);
            session.CompleteSpin(2);
            Assert.IsNotNull(session.SpinWord);
            session.ConfirmSpin();
            // The same player carries on from the spin into their own draw turn, so no hand-off.
            CollectionAssert.AreEqual(new[] { MatchPhase.Handoff, MatchPhase.Spin, MatchPhase.Countdown }, phases);
        }

        [Test]
        public void ThePassAndPlayViewReportsDrawAndGuessTurns()
        {
            PassAndPlaySession session = NewPassAndPlay(2);
            session.Machine.Start();
            session.ConfirmHandoff();
            session.CompleteSpin(0);
            string word = session.SpinWord;
            session.ConfirmSpin();
            Assert.AreEqual(MatchPhase.Countdown, session.Phase);
            Assert.AreEqual(StageKind.Draw, session.TurnKind);
            Assert.AreEqual(0, session.TurnIndex);
            Assert.AreEqual(word, session.PromptText);
            Assert.IsNull(session.PromptDrawing);
            session.Tick(session.Clock.Total + 0.1f);
            Assert.AreEqual(MatchPhase.Turn, session.Phase);
            session.SubmitDrawing(new byte[] { 1, 2, 3 });
            Assert.AreEqual(MatchPhase.Handoff, session.Phase);
        }

        [Test]
        public void ThePassAndPlayVoteIsOneSharedTap()
        {
            PassAndPlaySession session = NewPassAndPlay(2);
            var autoplayer = new MatchMachineDriver(session.Machine);
            autoplayer.RunTo(MatchPhase.Vote);
            Assert.IsTrue(session.CanAdvancePresent);
            Assert.AreEqual(session.PresentedWord, session.Machine.Chains[session.PresentChain].Word);
            session.CastVote(true);
            Assert.AreEqual(MatchPhase.VoteResult, session.Phase);
            Assert.IsTrue(session.LastVoteLanded);
            Assert.AreEqual(1, session.Scores[session.PresentOwner]);
            Assert.AreEqual(0, session.WaitingFor.Count);
            Assert.IsFalse(session.LocalDone);
        }

        /// <summary>Plays a Pass &amp; Play machine forward with blank drawings and fixed guesses, up to a phase.</summary>
        private sealed class MatchMachineDriver
        {
            private readonly MatchMachine m_Machine;

            public MatchMachineDriver(MatchMachine machine)
            {
                m_Machine = machine;
            }

            public void RunTo(MatchPhase target)
            {
                m_Machine.Start();
                for (int guard = 0; guard < 500 && m_Machine.Phase != target; guard++)
                {
                    switch (m_Machine.Phase)
                    {
                        case MatchPhase.Handoff:
                            m_Machine.ConfirmHandoff();
                            break;
                        case MatchPhase.Spin:
                            m_Machine.CompleteSpin(0);
                            m_Machine.ConfirmSpin();
                            break;
                        case MatchPhase.Countdown:
                            m_Machine.SkipCountdown();
                            break;
                        case MatchPhase.Turn:
                            if (m_Machine.CurrentStage.Kind == StageKind.Draw)
                            {
                                m_Machine.SubmitDrawing(new byte[] { 1 });
                            }
                            else
                            {
                                m_Machine.SubmitGuess("a guess");
                            }
                            break;
                        case MatchPhase.Present:
                            m_Machine.SkipPresent();
                            break;
                    }
                }
                Assert.AreEqual(target, m_Machine.Phase);
            }
        }
    }
}
