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
    /// <summary>The title screen: the game's name and the ways in.</summary>
    public sealed class TwMainMenuScreen : TwScreen
    {
        public TwMainMenuScreen(Action onPassAndPlay, Action onPlayOnline, Action onSettings)
            : base("Main menu", 1.5f, 0f)
        {
            Backdrop(0.95f, 0.95f);
            new TwHeadline(T, TwCopy.Title, TwUi.Px(TwTokens.DisplayXl), new Vector3(0f, 0.3f, 0f));
            Body("Draw it. Pass it. Guess it.", 0.19f);
            Button(TwCopy.PassAndPlay, 0f, 0.05f, 0.64f, 0.12f, TwButton.Style.Primary, onPassAndPlay);
            Button(TwCopy.PlayOnline, 0f, -0.11f, 0.64f, 0.12f, TwButton.Style.Secondary, onPlayOnline);
            Button(TwCopy.Settings, 0f, -0.27f, 0.64f, 0.12f, TwButton.Style.Secondary, onSettings);
            if (OpenBrushFacade.IsMonoscopic)
            {
                Body("Desktop: mouse aims, click presses, hold Alt to look around.", -0.41f, 0.85f);
            }
        }
    }

    /// <summary>The Pass &amp; Play rules: how many players and rounds, and which words.</summary>
    public sealed class TwSetupScreen : TwScreen
    {
        private readonly MatchSettings m_Settings;
        private readonly TextMeshPro m_Players;
        private readonly TextMeshPro m_Rounds;
        private readonly TwButton m_Words;

        public TwSetupScreen(MatchSettings settings, Action<MatchSettings> onStart, Action onBack)
            : base("Setup", 1.5f, 0f)
        {
            m_Settings = settings;
            Backdrop(1.05f, 1.05f);
            Title(TwCopy.PassAndPlay, 0.4f);

            Label(TwCopy.Players, -0.42f, 0.2f, TextAlignmentOptions.Left);
            Button("-", 0.12f, 0.2f, 0.1f, 0.1f, TwButton.Style.Secondary, () => ChangePlayers(-1));
            m_Players = TwUi.Display(T, string.Empty, TwUi.Px(TwTokens.DisplayLg), TwTokens.Sun, new Vector3(0.27f, 0.2f, 0f));
            Button("+", 0.42f, 0.2f, 0.1f, 0.1f, TwButton.Style.Secondary, () => ChangePlayers(1));

            Label(TwCopy.Rounds, -0.42f, 0.04f, TextAlignmentOptions.Left);
            Button("-", 0.12f, 0.04f, 0.1f, 0.1f, TwButton.Style.Secondary, () => ChangeRounds(-1));
            m_Rounds = TwUi.Display(T, string.Empty, TwUi.Px(TwTokens.DisplayLg), TwTokens.Sun, new Vector3(0.27f, 0.04f, 0f));
            Button("+", 0.42f, 0.04f, 0.1f, 0.1f, TwButton.Style.Secondary, () => ChangeRounds(1));

            Label("Words", -0.42f, -0.12f, TextAlignmentOptions.Left);
            m_Words = Button(string.Empty, 0.2f, -0.12f, 0.56f, 0.1f, TwButton.Style.Secondary, ToggleFilter);

            Button(TwCopy.Start, 0f, -0.3f, 0.54f, 0.12f, TwButton.Style.Primary, () => onStart(m_Settings));
            Button(TwCopy.Back, 0f, -0.44f, 0.3f, 0.08f, TwButton.Style.Ghost, onBack);
            Refresh();
        }

        private void ChangePlayers(int delta)
        {
            m_Settings.PlayerCount = Mathf.Clamp(
                m_Settings.PlayerCount + delta, MatchSettings.MinPlayers, MatchSettings.MaxPlayers);
            Refresh();
        }

        private void ChangeRounds(int delta)
        {
            m_Settings.Rounds = Mathf.Clamp(m_Settings.Rounds + delta, MatchSettings.MinRounds, MatchSettings.MaxRounds);
            Refresh();
        }

        private void ToggleFilter()
        {
            m_Settings.Filter = m_Settings.Filter == ContentFilter.Family ? ContentFilter.Raunchy : ContentFilter.Family;
            Refresh();
        }

        private void Refresh()
        {
            m_Players.text = m_Settings.PlayerCount.ToString();
            m_Rounds.text = m_Settings.Rounds.ToString();
            m_Words.SetLabel(m_Settings.Filter == ContentFilter.Family ? TwCopy.FamilyFriendly : TwCopy.Raunchy);
        }
    }
}
