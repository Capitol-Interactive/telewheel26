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
        private readonly IMatchView m_View;
        private readonly TwClockView m_Clock;

        public TwPresentScreen(IMatchView view, Action onSkip)
            : base("Present", 1.7f, 0.1f)
        {
            m_View = view;
            PresentItem item = view.CurrentPresentItem;
            // Item 0 is the spun word (the spin); item i is the chain's turn i - 1.
            new TwChainTrack(T, new Vector3(0f, 0.6f, 0f), view.Planner, view.PresentIndex - 1);

            string caption;
            switch (item.Kind)
            {
                case PresentItemKind.Word:
                    caption = TwCopy.OwnerLine(view.NameOf(item.Player));
                    break;
                case PresentItemKind.Drawing:
                    caption = TwCopy.DrawnBy(view.NameOf(item.Player));
                    break;
                default:
                    caption = TwCopy.GuessedBy(view.NameOf(item.Player));
                    break;
            }
            Label(caption, 0f, -0.55f);

            if (item.Kind != PresentItemKind.Drawing)
            {
                var headline = new TwHeadline(T, item.Text ?? string.Empty, TwUi.Px(TwTokens.DisplayXl), Vector3.zero);
                headline.Root.name = "Item";
            }

            m_Clock = new TwClockView(T, new Vector3(-0.62f, -0.55f, 0f), 0.26f);
            if (view.CanAdvancePresent)
            {
                Button(TwCopy.Skip, 0.62f, -0.55f, 0.26f, 0.09f, TwButton.Style.Secondary, onSkip);
            }
            else
            {
                // Online, only the chain's owner moves the reveal along.
                Label(TwCopy.OwnerMoves(view.NameOf(view.PresentOwner)), 0.45f, -0.55f);
            }
        }

        public override void Tick(float dt)
        {
            TurnClock clock = m_View.Clock;
            float fraction = clock.Total > 0f ? clock.Remaining / clock.Total : 0f;
            m_Clock.Set(clock.WholeSeconds, fraction, false);
        }
    }

    /// <summary>The final guess is up. Did it land? One shared tap for the room.</summary>
    public sealed class TwVoteScreen : TwScreen
    {
        private readonly IMatchView m_View;
        private readonly TwClockView m_Clock;

        public TwVoteScreen(IMatchView view, Action<bool> onVote)
            : base("Vote", 1.7f, 0.1f)
        {
            m_View = view;
            PresentItem item = view.CurrentPresentItem;
            new TwChainTrack(T, new Vector3(0f, 0.6f, 0f), view.Planner, view.Planner.TurnsPerChain - 1);

            Label(TwCopy.GuessedBy(view.NameOf(item.Player)), 0f, 0.36f);
            new TwHeadline(T, item.Text ?? string.Empty, TwUi.Px(TwTokens.DisplayXl), new Vector3(0f, 0.22f, 0f));
            Body("The word was " + view.PresentedWord + ".", 0.06f);
            TwUi.Display(T, TwCopy.DidItLand, TwUi.Px(TwTokens.DisplayLg), TwTokens.Ink, new Vector3(0f, -0.1f, 0f));
            Button(TwCopy.Yes, -0.22f, -0.3f, 0.34f, 0.13f, TwButton.Style.Positive, () => onVote(true));
            Button(TwCopy.No, 0.22f, -0.3f, 0.34f, 0.13f, TwButton.Style.Negative, () => onVote(false));
            m_Clock = new TwClockView(T, new Vector3(0f, -0.55f, 0f), 0.34f);
        }

        public override void Tick(float dt)
        {
            TurnClock clock = m_View.Clock;
            float fraction = clock.Total > 0f ? clock.Remaining / clock.Total : 0f;
            m_Clock.Set(clock.WholeSeconds, fraction, clock.Warning);
        }
    }

    /// <summary>"NAILED IT!" or "IT DRIFTED!" after the vote, with the point if there was one.</summary>
    public sealed class TwVoteResultScreen : TwScreen
    {
        /// <param name="onNext">Null when the game moves on by itself (online).</param>
        public TwVoteResultScreen(IMatchView view, Action onNext)
            : base("Vote result", 1.6f, 0.1f)
        {
            bool landed = view.LastVoteLanded;
            Backdrop(1.0f, 0.75f);
            new TwHeadline(T, landed ? TwCopy.NailedIt : TwCopy.ItDrifted,
                TwUi.Px(TwTokens.DisplayXl), new Vector3(0f, 0.15f, 0f));
            string owner = view.NameOf(view.PresentOwner);
            Heading(landed ? owner + ": +1 point" : TwCopy.DriftedLine, -0.02f, 0.9f);
            if (onNext != null)
            {
                Button(TwCopy.Next, 0f, -0.2f, 0.4f, 0.11f, TwButton.Style.Primary, onNext);
            }
        }
    }

    /// <summary>The scores after a round.</summary>
    public sealed class TwRoundEndScreen : TwScreen
    {
        /// <param name="onNext">Null when the game moves on by itself (online).</param>
        public TwRoundEndScreen(IMatchView view, Action onNext)
            : base("Round end", 1.5f, 0f)
        {
            Backdrop(1.0f, 1.0f);
            Title(view.IsFinalRound ? TwCopy.GameOver : TwCopy.RoundOver, 0.4f);
            Label(TwCopy.RoundLabel(view.Round, view.RoundCount), 0f, 0.28f);
            Scoreboard(view, 0.16f, 0.065f);
            if (onNext != null)
            {
                Button(view.IsFinalRound ? "Final results" : TwCopy.Next, 0f, -0.42f, 0.5f, 0.11f,
                    TwButton.Style.Primary, onNext);
            }
            else
            {
                Body(view.IsFinalRound ? TwCopy.ResultsComing : TwCopy.NextRoundComing, -0.42f);
            }
        }
    }

    /// <summary>The winner and the ways to carry on.</summary>
    public sealed class TwGameEndScreen : TwScreen
    {
        /// <param name="onSame">Null online, where there is no rematch button yet.</param>
        /// <param name="onNew">Null online, where there is no rematch button yet.</param>
        public TwGameEndScreen(IMatchView view, Action onSame, Action onNew, Action onMenu)
            : base("Game end", 1.5f, 0f)
        {
            Backdrop(1.1f, 1.15f);
            var names = new System.Collections.Generic.List<string>();
            foreach (int player in view.Leaders)
            {
                names.Add(view.NameOf(player));
            }
            new TwHeadline(T, TwCopy.GameOver, TwUi.Px(TwTokens.DisplayXl), new Vector3(0f, 0.46f, 0f));
            Heading(TwCopy.WinnerLine(string.Join(" & ", names.ToArray())), 0.32f, 0.95f);
            Scoreboard(view, 0.2f, 0.06f);
            if (onSame != null)
            {
                Button(TwCopy.PlayAgainSame, 0f, -0.34f, 0.8f, 0.1f, TwButton.Style.Primary, onSame);
            }
            if (onNew != null)
            {
                Button(TwCopy.PlayAgainNew, 0f, -0.47f, 0.8f, 0.1f, TwButton.Style.Secondary, onNew);
            }
            bool menuIsMain = onSame != null || onNew != null;
            Button(view.IsOnline ? TwCopy.LeaveRoom : TwCopy.MainMenu, 0f, menuIsMain ? -0.58f : -0.4f,
                menuIsMain ? 0.3f : 0.5f, menuIsMain ? 0.07f : 0.11f,
                menuIsMain ? TwButton.Style.Ghost : TwButton.Style.Primary, onMenu);
        }
    }
}
