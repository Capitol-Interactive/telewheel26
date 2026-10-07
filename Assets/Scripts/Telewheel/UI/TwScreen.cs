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
    /// One thing on screen in front of the player: a root object placed ahead of them, built from
    /// the pieces in TwUi. Destroying a screen removes everything it made.
    /// </summary>
    public abstract class TwScreen
    {
        public GameObject Root { get; private set; }

        protected TwScreen(string name, float distanceMeters, float eyeOffsetMeters)
        {
            Root = TwUi.NewRoot("Telewheel " + name);
            TwUi.PlaceInFront(Root.transform, distanceMeters, eyeOffsetMeters);
        }

        protected Transform T
        {
            get { return Root.transform; }
        }

        /// <summary>
        /// Adds a small MENU button (the system menu: exit, preferences, photo) in the screen's
        /// bottom-left corner, for players whose hand menu is hidden between turns.
        /// </summary>
        public void AddMenuButton(Action onMenu)
        {
            if (Root != null)
            {
                TwButton.Create(T, "Menu", 0.18f, 0.07f, TwButton.Style.Ghost,
                    new Vector3(-0.66f, -0.72f, 0f), onMenu);
            }
        }

        /// <summary>Called every frame while the screen is showing.</summary>
        public virtual void Tick(float dt)
        {
        }

        public virtual void Dispose()
        {
            if (Root == null)
            {
                return;
            }
            // Deactivate first so its buttons stop being clickable straight away.
            Root.SetActive(false);
            UnityEngine.Object.Destroy(Root);
            Root = null;
        }

        // ----- Shared building blocks -----

        protected void Backdrop(float width, float height)
        {
            TwGfx.Shape(T, "Border",
                TwGfx.RoundedRect(width + TwUi.Px(8f), height + TwUi.Px(8f), TwUi.Px(TwTokens.RadiusLg + 4)),
                TwTokens.Line, new Vector3(0f, 0f, TwUi.ZPanel + 0.002f));
            TwUi.Panel(T, "Panel", width, height, TwTokens.Surface200, new Vector3(0f, 0f, TwUi.ZPanel));
        }

        protected TextMeshPro Title(string text, float y)
        {
            return TwUi.Display(T, text, TwUi.Px(TwTokens.DisplayLg), TwTokens.Sun, new Vector3(0f, y, 0f));
        }

        protected TextMeshPro Heading(string text, float y, float wrapWidth = 0f)
        {
            return TwUi.Text(T, text, TwUi.Px(TwTokens.Heading), TwTokens.Ink, TwFonts.Bold,
                new Vector3(0f, y, 0f), TextAlignmentOptions.Center, wrapWidth);
        }

        protected TextMeshPro Body(string text, float y, float wrapWidth = 0f)
        {
            return TwUi.Text(T, text, TwUi.Px(TwTokens.Body), TwTokens.InkMuted, TwFonts.Body,
                new Vector3(0f, y, 0f), TextAlignmentOptions.Center, wrapWidth);
        }

        protected TextMeshPro Label(string text, float x, float y,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center)
        {
            return TwUi.Text(T, text.ToUpperInvariant(), TwUi.Px(TwTokens.Label) * 1.15f, TwTokens.InkMuted,
                TwFonts.Bold, new Vector3(x, y, 0f), alignment);
        }

        protected TwButton Button(string label, float x, float y, float width, float height,
            TwButton.Style style, System.Action onClick)
        {
            return TwButton.Create(T, label, width, height, style, new Vector3(x, y, 0f), onClick);
        }

        /// <summary>Names and scores, highest first; the leaders in sun yellow.</summary>
        protected void Scoreboard(MatchMachine machine, float topY, float rowHeight)
        {
            int count = machine.Settings.PlayerCount;
            var order = new System.Collections.Generic.List<int>();
            for (int i = 0; i < count; i++)
            {
                order.Add(i);
            }
            order.Sort((a, b) =>
            {
                int byScore = machine.Scores[b].CompareTo(machine.Scores[a]);
                return byScore != 0 ? byScore : a.CompareTo(b);
            });
            System.Collections.Generic.IList<int> leaders = machine.Leaders;
            for (int row = 0; row < order.Count; row++)
            {
                int player = order[row];
                uint color = leaders.Contains(player) ? TwTokens.Sun : TwTokens.Ink;
                float y = topY - rowHeight * row;
                TwUi.Text(T, machine.Settings.NameOf(player), TwUi.Px(TwTokens.Heading), color,
                    TwFonts.Bold, new Vector3(-0.3f, y, 0f), TextAlignmentOptions.Left);
                TwUi.Text(T, machine.Scores[player].ToString(), TwUi.Px(TwTokens.Heading), color,
                    TwFonts.Bold, new Vector3(0.3f, y, 0f), TextAlignmentOptions.Right);
            }
        }
    }
}
