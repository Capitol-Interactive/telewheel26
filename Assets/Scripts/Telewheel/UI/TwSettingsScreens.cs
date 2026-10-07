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
using UnityEngine;

namespace Telewheel
{
    /// <summary>Preferences: mixed reality, which hand draws, and how you point.</summary>
    public sealed class TwSettingsScreen : TwScreen
    {
        private readonly TwButton m_MixedReality;
        private readonly TwButton m_Hand;
        private readonly TMPro.TextMeshPro m_Note;

        public TwSettingsScreen(Action onBack)
            : base("Settings", 1.5f, 0f)
        {
            Backdrop(1.0f, 0.95f);
            Title(TwCopy.Settings, 0.36f);

            m_MixedReality = Button(string.Empty, 0f, 0.18f, 0.74f, 0.12f, TwButton.Style.Secondary, ToggleMixedReality);
            m_Note = Body(string.Empty, 0.1f, 0.9f);
            if (!OpenBrushFacade.PassthroughSupported)
            {
                m_MixedReality.SetInteractable(false);
                m_Note.text = "Mixed reality is not available on this device.";
            }

            m_Hand = Button(string.Empty, 0f, -0.04f, 0.74f, 0.12f, TwButton.Style.Secondary, ToggleHand);

            TwButton input = Button(TwCopy.Controllers, 0f, -0.22f, 0.74f, 0.12f, TwButton.Style.Secondary, null);
            input.SetInteractable(false);
            Body(TwCopy.HandsSoon, -0.31f);

            Button(TwCopy.Back, 0f, -0.42f, 0.4f, 0.1f, TwButton.Style.Primary, onBack);
            Refresh();
        }

        private void ToggleMixedReality()
        {
            bool switched = TwPrefs.SetMixedReality(!TwPrefs.MixedReality);
            m_Note.text = switched ? string.Empty : TwCopy.MixedRealityStuck;
            Refresh();
        }

        private void ToggleHand()
        {
            TwPrefs.SetRightHanded(!TwPrefs.RightHanded);
            Refresh();
        }

        private void Refresh()
        {
            m_MixedReality.SetLabel(TwCopy.MixedReality + ": " + (TwPrefs.MixedReality ? TwCopy.On : TwCopy.Off));
            m_Hand.SetLabel(TwPrefs.RightHanded ? TwCopy.RightHanded : TwCopy.LeftHanded);
        }
    }

    /// <summary>
    /// The always-available system menu (opened from the hand menu's settings button, the MENU
    /// button, or F10): exit the match, preferences and photo mode, and the online features that
    /// are not built yet.
    /// </summary>
    public sealed class TwSystemMenuScreen : TwScreen
    {
        public TwSystemMenuScreen(
            bool inMatch, bool canPhoto, Action onPreferences, Action onPhoto, Action onExit, Action onClose)
            : base("System menu", 1.2f, 0f)
        {
            Backdrop(0.8f, 1.0f);
            Title("Menu", 0.4f);
            Button(TwCopy.Settings, 0f, 0.24f, 0.6f, 0.1f, TwButton.Style.Secondary, onPreferences);
            TwButton photo = Button(TwCopy.Photo, 0f, 0.1f, 0.6f, 0.1f, TwButton.Style.Secondary, onPhoto);
            photo.SetInteractable(canPhoto);
            TwButton invite = Button(TwCopy.Invite, 0f, -0.04f, 0.6f, 0.1f, TwButton.Style.Secondary, null);
            invite.SetInteractable(false);
            TwButton friends = Button(TwCopy.OnlineFriends, 0f, -0.18f, 0.6f, 0.1f, TwButton.Style.Secondary, null);
            friends.SetInteractable(false);
            TwButton exit = Button(TwCopy.ExitMatch, 0f, -0.33f, 0.6f, 0.1f, TwButton.Style.Negative, onExit);
            exit.SetInteractable(inMatch);
            Button("Close", 0f, -0.45f, 0.3f, 0.07f, TwButton.Style.Ghost, onClose);
        }
    }
}
