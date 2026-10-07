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

using TMPro;
using UnityEngine;

namespace Telewheel
{
    /// <summary>The turn clock: big digits that turn magenta in the last ten seconds, over a draining bar.</summary>
    public sealed class TwClockView
    {
        public readonly GameObject Root;
        private readonly TextMeshPro m_Digits;
        private readonly GameObject m_Bar;
        private readonly float m_BarWidth;
        private bool m_Warning;
        private string m_LastText;

        public TwClockView(Transform parent, Vector3 localPosition, float widthMeters)
        {
            Root = new GameObject("Clock");
            Root.transform.SetParent(parent, false);
            Root.transform.localPosition = localPosition;
            float height = TwUi.Px(100f);
            TwUi.Panel(Root.transform, "Plate", widthMeters, height, TwTokens.Surface200,
                new Vector3(0f, 0f, TwUi.ZFace));
            m_Digits = TwUi.Display(Root.transform, "60", TwUi.Px(TwTokens.DisplayLg), TwTokens.Sun,
                new Vector3(0f, TwUi.Px(8f), 0f));
            m_BarWidth = widthMeters - TwUi.Px(36f);
            m_Bar = TwUi.Pill(Root.transform, "Bar", m_BarWidth, TwUi.Px(10f), TwTokens.Sun,
                new Vector3(0f, -height * 0.36f, 0.001f));
        }

        /// <summary>Shows <paramref name="wholeSeconds"/>, with the bar at <paramref name="remainingFraction"/> (0..1).</summary>
        public void Set(int wholeSeconds, float remainingFraction, bool warning)
        {
            string text = wholeSeconds.ToString();
            if (text != m_LastText)
            {
                m_Digits.text = text;
                m_LastText = text;
            }
            if (warning != m_Warning)
            {
                m_Warning = warning;
                m_Digits.color = TwGfx.ToColor(warning ? TwTokens.Magenta : TwTokens.Sun);
                m_Bar.GetComponent<MeshRenderer>().sharedMaterial =
                    TwGfx.Flat(warning ? TwTokens.Magenta : TwTokens.Sun);
            }
            float fraction = Mathf.Clamp01(remainingFraction);
            m_Bar.transform.localScale = new Vector3(Mathf.Max(0.001f, fraction), 1f, 1f);
            m_Bar.transform.localPosition = new Vector3(
                -m_BarWidth * (1f - fraction) * 0.5f, m_Bar.transform.localPosition.y, 0.001f);
        }
    }

    /// <summary>The prompt for this turn: a small label over the word to draw, or a hint when guessing.</summary>
    public sealed class TwWordCard
    {
        public readonly GameObject Root;
        private readonly TextMeshPro m_Label;
        private readonly TextMeshPro m_Word;

        public TwWordCard(Transform parent, Vector3 localPosition, float width, float height)
        {
            Root = new GameObject("WordCard");
            Root.transform.SetParent(parent, false);
            Root.transform.localPosition = localPosition;
            GameObject plate = TwGfx.Shape(
                Root.transform,
                "Plate",
                TwGfx.RoundedRect(width, height, TwUi.Px(TwTokens.RadiusMd)),
                TwTokens.Surface200,
                new Vector3(0f, 0f, TwUi.ZFace));
            plate.name = "Plate";
            TwGfx.Shape(
                Root.transform,
                "Border",
                TwGfx.RoundedRect(width + TwUi.Px(6f), height + TwUi.Px(6f), TwUi.Px(TwTokens.RadiusMd + 3)),
                TwTokens.Line,
                new Vector3(0f, 0f, TwUi.ZBorder));
            m_Label = TwUi.Text(Root.transform, string.Empty, TwUi.Px(TwTokens.Label) * 1.2f,
                TwTokens.InkMuted, TwFonts.Bold, new Vector3(0f, height * 0.3f, 0f));
            m_Word = TwUi.Display(Root.transform, string.Empty, TwUi.Px(TwTokens.DisplayLg) * 0.9f,
                TwTokens.Sun, new Vector3(0f, -height * 0.08f, 0f));
        }

        public void Set(string label, string word)
        {
            m_Label.text = label.ToUpperInvariant();
            m_Word.text = word.ToUpperInvariant();
        }
    }

    /// <summary>The text well that shows what the player has typed so far, with a blinking cursor.</summary>
    public sealed class TwGuessField
    {
        public readonly GameObject Root;
        private readonly TextMeshPro m_Text;
        private readonly string m_Placeholder;
        private string m_Value = string.Empty;

        public TwGuessField(Transform parent, Vector3 localPosition, float width, string placeholder = null)
        {
            m_Placeholder = placeholder ?? TwCopy.TypeYourGuess;
            Root = new GameObject("GuessField");
            Root.transform.SetParent(parent, false);
            Root.transform.localPosition = localPosition;
            float height = TwUi.Px(72f);
            TwGfx.Shape(Root.transform, "Border",
                TwGfx.RoundedRect(width + TwUi.Px(6f), height + TwUi.Px(6f), TwUi.Px(TwTokens.RadiusSm + 3)),
                TwTokens.Line, new Vector3(0f, 0f, TwUi.ZBorder));
            TwGfx.Shape(Root.transform, "Well",
                TwGfx.RoundedRect(width, height, TwUi.Px(TwTokens.RadiusSm)),
                TwTokens.Surface300, new Vector3(0f, 0f, TwUi.ZFace));
            m_Text = TwUi.Text(Root.transform, string.Empty, TwUi.Px(TwTokens.Heading), TwTokens.Ink,
                TwFonts.Bold, Vector3.zero);
            Refresh(true);
        }

        public string Value
        {
            get { return m_Value; }
        }

        public void SetValue(string value)
        {
            m_Value = value ?? string.Empty;
            Refresh(true);
        }

        /// <summary>Call every frame to blink the cursor.</summary>
        public void Tick(float time)
        {
            Refresh(Mathf.FloorToInt(time * 2f) % 2 == 0);
        }

        private void Refresh(bool cursorOn)
        {
            if (m_Value.Length == 0)
            {
                m_Text.color = TwGfx.ToColor(TwTokens.InkMuted);
                m_Text.text = cursorOn ? "|" + m_Placeholder : " " + m_Placeholder;
            }
            else
            {
                m_Text.color = TwGfx.ToColor(TwTokens.Ink);
                m_Text.text = m_Value + (cursorOn ? "|" : " ");
            }
        }
    }

    /// <summary>
    /// The row of turns in one chain: spin, then each draw and guess, then the reveal. Every node
    /// also says Done, Now or Up next, so state never depends on colour alone.
    /// </summary>
    public sealed class TwChainTrack
    {
        public readonly GameObject Root;

        /// <summary>The kind colours from the design system: sun spin, tangerine draw, ion guess, magenta reveal.</summary>
        public static uint ColorFor(StageKind kind)
        {
            switch (kind)
            {
                case StageKind.Draw:
                    return TwTokens.Tangerine;
                case StageKind.Guess:
                    return TwTokens.Ion;
                default:
                    return TwTokens.Sun;
            }
        }

        /// <param name="currentTurn">The turn being played, -1 for the spin, planner.TurnsPerChain for the reveal.</param>
        public TwChainTrack(Transform parent, Vector3 localPosition, ChainPlanner planner, int currentTurn)
        {
            Root = new GameObject("ChainTrack");
            Root.transform.SetParent(parent, false);
            Root.transform.localPosition = localPosition;

            int turns = planner.TurnsPerChain;
            int nodes = turns + 2;
            float nodeSize = Mathf.Min(TwUi.Px(52f), 0.7f / nodes);
            float spacing = Mathf.Min(TwUi.Px(100f), 1.2f / nodes);
            float x0 = -spacing * (nodes - 1) * 0.5f;

            for (int i = 0; i < nodes; i++)
            {
                int turn = i - 1; // node 0 is the spin, the last node is the reveal
                StageKind kind = turn < 0 ? StageKind.Spin
                    : (turn >= turns ? StageKind.Spin : planner.KindOfTurn(turn));
                bool isReveal = turn >= turns;
                uint color = isReveal ? TwTokens.Magenta : ColorFor(kind);
                string state = turn < currentTurn ? "Done" : (turn == currentTurn ? "Now" : "Up next");
                float x = x0 + spacing * i;

                if (i < nodes - 1)
                {
                    TwUi.Pill(Root.transform, "Link", spacing - nodeSize, TwUi.Px(6f), TwTokens.Line,
                        new Vector3(x + spacing * 0.5f, 0f, TwUi.ZShadow));
                }
                bool current = turn == currentTurn;
                if (current)
                {
                    TwGfx.Shape(Root.transform, "Glow", TwGfx.Disc(nodeSize * 0.5f + TwUi.Px(8f), 24),
                        TwTokens.FocusRing, new Vector3(x, 0f, TwUi.ZGlow));
                }
                TwGfx.Shape(Root.transform, "Node", TwGfx.Disc(nodeSize * 0.5f, 24),
                    turn <= currentTurn ? color : TwTokens.Surface300, new Vector3(x, 0f, TwUi.ZFace));
                string caption = isReveal ? "Reveal"
                    : (turn < 0 ? "Spin" : (kind == StageKind.Draw ? "Draw" : "Guess"));
                TwUi.Text(Root.transform, caption.ToUpperInvariant(), TwUi.Px(TwTokens.Label) * 0.9f,
                    TwTokens.Ink, TwFonts.Bold, new Vector3(x, -nodeSize * 0.9f, 0f));
                TwUi.Text(Root.transform, state, TwUi.Px(TwTokens.Label) * 0.8f, TwTokens.InkMuted,
                    TwFonts.Body, new Vector3(x, -nodeSize * 1.3f, 0f));
            }
        }
    }

    /// <summary>
    /// The suggested drawing area on the floor: a flat square outline. It is only a suggestion, so
    /// it has no collider and never stops a stroke.
    /// </summary>
    public sealed class TwFloorSquare
    {
        public readonly GameObject Root;

        public TwFloorSquare()
        {
            Root = new GameObject("Telewheel floor square");
            float side = TwLayout.FloorSquareSideMeters;
            float thickness = TwUi.Px(8f);
            Mesh bar = TwGfx.RoundedRect(side, thickness, thickness * 0.5f, 3);
            Mesh barSide = TwGfx.RoundedRect(thickness, side, thickness * 0.5f, 3);
            float half = side * 0.5f;
            // The shapes are built in the XY plane; lay them flat on the floor (rotate about X).
            Root.transform.localScale = Vector3.one * TwLayout.MetersToUnits;
            Transform t = Root.transform;
            AddBar(t, "Near", bar, new Vector3(0f, 0f, -half));
            AddBar(t, "Far", bar, new Vector3(0f, 0f, half));
            AddBar(t, "Left", barSide, new Vector3(-half, 0f, 0f));
            AddBar(t, "Right", barSide, new Vector3(half, 0f, 0f));
        }

        private static void AddBar(Transform parent, string name, Mesh mesh, Vector3 position)
        {
            GameObject go = TwGfx.Shape(parent, name, mesh, TwTokens.Ion, position);
            go.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
        }

        /// <summary>
        /// Places the square on the floor <see cref="TwLayout.FloorSquareCenterMeters"/> ahead of
        /// the player (z is how far ahead, in the direction they face), and keeps it facing that way.
        /// </summary>
        public void PlaceAhead(Transform head)
        {
            if (head == null)
            {
                return;
            }
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();
            Vector3 p = head.position + forward * TwLayout.ToUnits(TwLayout.FloorSquareCenterMeters.z);
            // A hair above the floor so it does not flicker against it.
            Root.transform.position = new Vector3(p.x, 0.05f, p.z);
            Root.transform.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        public void SetVisible(bool visible)
        {
            Root.SetActive(visible);
        }

        public void Destroy()
        {
            if (Root != null)
            {
                Object.Destroy(Root);
            }
        }
    }
}
