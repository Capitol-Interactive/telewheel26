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
using TMPro;
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// The reveal of one chain, one item at a time. Words and guesses are text on this screen;
    /// drawings float in the open space in front (shown by the game, not this screen).
    /// </summary>
    public sealed class TwPresentScreen : TwScreen
    {
        private readonly MatchMachine m_Machine;
        private readonly TwClockView m_Clock;

        public TwPresentScreen(MatchMachine machine, Action onSkip)
            : base("Present", 1.7f, 0.1f)
        {
            m_Machine = machine;
            PresentItem item = machine.CurrentPresentItem;
            MatchSettings settings = machine.Settings;
            // Item 0 is the spun word (the spin); item i is the chain's turn i - 1.
            new TwChainTrack(T, new Vector3(0f, 0.6f, 0f), machine.Planner, machine.PresentIndex - 1);

            string caption;
            switch (item.Kind)
            {
                case PresentItemKind.Word:
                    caption = TwCopy.OwnerLine(settings.NameOf(item.Player));
                    break;
                case PresentItemKind.Drawing:
                    caption = TwCopy.DrawnBy(settings.NameOf(item.Player));
                    break;
                default:
                    caption = TwCopy.GuessedBy(settings.NameOf(item.Player));
                    break;
            }
            Label(caption, 0f, -0.55f);

            if (item.Kind != PresentItemKind.Drawing)
            {
                var headline = new TwHeadline(T, item.Text ?? string.Empty, TwUi.Px(TwTokens.DisplayXl), Vector3.zero);
                headline.Root.name = "Item";
            }

            m_Clock = new TwClockView(T, new Vector3(-0.62f, -0.55f, 0f), 0.26f);
            Button(TwCopy.Skip, 0.62f, -0.55f, 0.26f, 0.09f, TwButton.Style.Secondary, onSkip);
        }

        public override void Tick(float dt)
        {
            TurnClock clock = m_Machine.Clock;
            float fraction = clock.Total > 0f ? clock.Remaining / clock.Total : 0f;
            m_Clock.Set(clock.WholeSeconds, fraction, false);
        }
    }

    /// <summary>The final guess is up. Did it land? One shared tap for the room.</summary>
    public sealed class TwVoteScreen : TwScreen
    {
        private readonly MatchMachine m_Machine;
        private readonly TwClockView m_Clock;

        public TwVoteScreen(MatchMachine machine, Action<bool> onVote)
            : base("Vote", 1.7f, 0.1f)
        {
            m_Machine = machine;
            PresentItem item = machine.CurrentPresentItem;
            MatchSettings settings = machine.Settings;
            new TwChainTrack(T, new Vector3(0f, 0.6f, 0f), machine.Planner, machine.Planner.TurnsPerChain - 1);

            Label(TwCopy.GuessedBy(settings.NameOf(item.Player)), 0f, 0.36f);
            new TwHeadline(T, item.Text ?? string.Empty, TwUi.Px(TwTokens.DisplayXl), new Vector3(0f, 0.22f, 0f));
            Body("The word was " + machine.PresentItems[0].Text + ".", 0.06f);
            TwUi.Display(T, TwCopy.DidItLand, TwUi.Px(TwTokens.DisplayLg), TwTokens.Ink, new Vector3(0f, -0.1f, 0f));
            Button(TwCopy.Yes, -0.22f, -0.3f, 0.34f, 0.13f, TwButton.Style.Positive, () => onVote(true));
            Button(TwCopy.No, 0.22f, -0.3f, 0.34f, 0.13f, TwButton.Style.Negative, () => onVote(false));
            m_Clock = new TwClockView(T, new Vector3(0f, -0.55f, 0f), 0.34f);
        }

        public override void Tick(float dt)
        {
            TurnClock clock = m_Machine.Clock;
            float fraction = clock.Total > 0f ? clock.Remaining / clock.Total : 0f;
            m_Clock.Set(clock.WholeSeconds, fraction, clock.Warning);
        }
    }

    /// <summary>"NAILED IT!" or "IT DRIFTED!" after the vote, with the point if there was one.</summary>
    public sealed class TwVoteResultScreen : TwScreen
    {
        public TwVoteResultScreen(MatchMachine machine, Action onNext)
            : base("Vote result", 1.6f, 0.1f)
        {
            bool landed = machine.LastVoteLanded;
            Backdrop(1.0f, 0.75f);
            new TwHeadline(T, landed ? TwCopy.NailedIt : TwCopy.ItDrifted,
                TwUi.Px(TwTokens.DisplayXl), new Vector3(0f, 0.15f, 0f));
            string owner = machine.Settings.NameOf(machine.Chains[machine.PresentChain].Owner);
            Heading(landed ? owner + ": +1 point" : TwCopy.DriftedLine, -0.02f, 0.9f);
            Button(TwCopy.Next, 0f, -0.2f, 0.4f, 0.11f, TwButton.Style.Primary, onNext);
        }
    }

    /// <summary>The scores after a round.</summary>
    public sealed class TwRoundEndScreen : TwScreen
    {
        public TwRoundEndScreen(MatchMachine machine, Action onNext)
            : base("Round end", 1.5f, 0f)
        {
            Backdrop(1.0f, 1.0f);
            Title(machine.IsFinalRound ? TwCopy.GameOver : TwCopy.RoundOver, 0.4f);
            Label(TwCopy.RoundLabel(machine.Round, machine.Settings.Rounds), 0f, 0.28f);
            Scoreboard(machine, 0.16f, 0.065f);
            Button(machine.IsFinalRound ? "Final results" : TwCopy.Next, 0f, -0.42f, 0.5f, 0.11f,
                TwButton.Style.Primary, onNext);
        }
    }

    /// <summary>The winner and the ways to carry on.</summary>
    public sealed class TwGameEndScreen : TwScreen
    {
        public TwGameEndScreen(MatchMachine machine, Action onSame, Action onNew, Action onMenu)
            : base("Game end", 1.5f, 0f)
        {
            Backdrop(1.1f, 1.15f);
            var names = new System.Collections.Generic.List<string>();
            foreach (int player in machine.Leaders)
            {
                names.Add(machine.Settings.NameOf(player));
            }
            new TwHeadline(T, TwCopy.GameOver, TwUi.Px(TwTokens.DisplayXl), new Vector3(0f, 0.46f, 0f));
            Heading(TwCopy.WinnerLine(string.Join(" & ", names.ToArray())), 0.32f, 0.95f);
            Scoreboard(machine, 0.2f, 0.06f);
            Button(TwCopy.PlayAgainSame, 0f, -0.34f, 0.8f, 0.1f, TwButton.Style.Primary, onSame);
            Button(TwCopy.PlayAgainNew, 0f, -0.47f, 0.8f, 0.1f, TwButton.Style.Secondary, onNew);
            Button(TwCopy.MainMenu, 0f, -0.58f, 0.3f, 0.07f, TwButton.Style.Ghost, onMenu);
        }
    }
}
