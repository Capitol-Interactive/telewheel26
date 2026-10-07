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
    /// <summary>
    /// Builders for Telewheel's world-space UI. Every screen is a tree under a root object that
    /// is scaled so that one local unit is one metre; positions, sizes and text heights below are
    /// all in metres. Local +Z points away from the player, so layers that are "behind" something
    /// have a larger z.
    /// </summary>
    public static class TwUi
    {
        /// <summary>Design-system px to metres (see TwTokens.PixelToMeters).</summary>
        public static float Px(float pixels)
        {
            return pixels * TwTokens.PixelToMeters;
        }

        // Layers, in metres behind the text (which is at z = 0).
        public const float ZFace = 0.002f;
        public const float ZBorder = 0.003f;
        public const float ZShadow = 0.004f;
        public const float ZGlow = 0.006f;
        public const float ZPanel = 0.012f;

        private const float FontSize = 24f;

        /// <summary>
        /// A new, empty screen root. Created in world space (not under the Open Brush scene), scaled
        /// so one local unit is a metre.
        /// </summary>
        public static GameObject NewRoot(string name)
        {
            var root = new GameObject(name);
            root.transform.localScale = Vector3.one * TwLayout.MetersToUnits;
            return root;
        }

        /// <summary>
        /// A world position <paramref name="distanceMeters"/> ahead of the player (along the direction
        /// they face, ignoring head tilt) and <paramref name="eyeOffsetMeters"/> above their eyes.
        /// </summary>
        public static Vector3 PointInFront(float distanceMeters, float eyeOffsetMeters)
        {
            Transform head = OpenBrushFacade.Head;
            if (head == null)
            {
                return TwLayout.ToUnits(new Vector3(0f, eyeOffsetMeters + 1.5f, distanceMeters));
            }
            Vector3 forward = head.forward;
            forward.y = 0f;
            if (forward.sqrMagnitude < 0.0001f)
            {
                forward = Vector3.forward;
            }
            forward.Normalize();
            return head.position
                + forward * TwLayout.ToUnits(distanceMeters)
                + Vector3.up * TwLayout.ToUnits(eyeOffsetMeters);
        }

        /// <summary>
        /// Puts a screen root in front of the player: <paramref name="distanceMeters"/> ahead along
        /// the direction they face (ignoring head tilt), at their eye height plus
        /// <paramref name="eyeOffsetMeters"/>, turned to face them.
        /// </summary>
        public static void PlaceInFront(Transform root, float distanceMeters, float eyeOffsetMeters)
        {
            Transform head = OpenBrushFacade.Head;
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
            root.position = head.position
                + forward * TwLayout.ToUnits(distanceMeters)
                + Vector3.up * TwLayout.ToUnits(eyeOffsetMeters);
            root.rotation = Quaternion.LookRotation(forward, Vector3.up);
        }

        /// <summary>
        /// Text of a given em height (metres). Pivots on its alignment, so left-aligned text starts
        /// at <paramref name="localPosition"/> and centred text is centred on it.
        /// </summary>
        public static TextMeshPro Text(
            Transform parent,
            string text,
            float emMeters,
            uint rgb,
            TMP_FontAsset font,
            Vector3 localPosition,
            TextAlignmentOptions alignment = TextAlignmentOptions.Center,
            float wrapWidthMeters = 0f)
        {
            var go = new GameObject("Text");
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;

            TextMeshPro tmp = go.AddComponent<TextMeshPro>();
            tmp.font = font != null ? font : TMP_Settings.defaultFontAsset;
            tmp.fontSize = FontSize;
            tmp.enableAutoSizing = false;
            tmp.color = TwGfx.ToColor(rgb);
            tmp.alignment = alignment;
            tmp.overflowMode = TextOverflowModes.Overflow;
            tmp.text = text;

            // TextMeshPro's 3D text is 0.1 local units per font-size point.
            float scale = emMeters / (FontSize * 0.1f);
            go.transform.localScale = Vector3.one * scale;

            RectTransform rect = tmp.rectTransform;
            float pivotX = 0.5f;
            if (alignment == TextAlignmentOptions.Left || alignment == TextAlignmentOptions.TopLeft
                || alignment == TextAlignmentOptions.MidlineLeft || alignment == TextAlignmentOptions.BottomLeft)
            {
                pivotX = 0f;
            }
            else if (alignment == TextAlignmentOptions.Right || alignment == TextAlignmentOptions.TopRight
                || alignment == TextAlignmentOptions.MidlineRight || alignment == TextAlignmentOptions.BottomRight)
            {
                pivotX = 1f;
            }
            rect.pivot = new Vector2(pivotX, 0.5f);
            if (wrapWidthMeters > 0f)
            {
                tmp.textWrappingMode = TextWrappingModes.Normal;
                rect.sizeDelta = new Vector2(wrapWidthMeters / scale, rect.sizeDelta.y);
            }
            else
            {
                tmp.textWrappingMode = TextWrappingModes.NoWrap;
            }
            return tmp;
        }

        /// <summary>Display type: Lilita One, uppercase.</summary>
        public static TextMeshPro Display(
            Transform parent, string text, float emMeters, uint rgb, Vector3 localPosition)
        {
            return Text(parent, text.ToUpperInvariant(), emMeters, rgb, TwFonts.Display, localPosition);
        }

        /// <summary>A flat colour shape, in a plane behind the text.</summary>
        public static GameObject Pill(
            Transform parent, string name, float width, float height, uint rgb, Vector3 localPosition)
        {
            return TwGfx.Shape(
                parent, name, TwGfx.RoundedRect(width, height, height * 0.5f), rgb, localPosition);
        }

        public static GameObject Panel(
            Transform parent, string name, float width, float height, uint rgb, Vector3 localPosition)
        {
            return TwGfx.Shape(
                parent, name, TwGfx.RoundedRect(width, height, Px(TwTokens.RadiusLg)), rgb, localPosition);
        }
    }

    /// <summary>
    /// The wordmark treatment for a hero line: Lilita One caps in sun yellow with a deep outline
    /// and a hard drop shadow. Use one per screen.
    /// </summary>
    public sealed class TwHeadline
    {
        public readonly GameObject Root;
        private readonly TextMeshPro m_Main;
        private readonly TextMeshPro m_Shadow;

        public TwHeadline(Transform parent, string text, float emMeters, Vector3 localPosition)
        {
            Root = new GameObject("Headline");
            Root.transform.SetParent(parent, false);
            Root.transform.localPosition = localPosition;

            float drop = emMeters * 0.07f;
            m_Shadow = TwUi.Display(Root.transform, text, emMeters, TwTokens.Deep, new Vector3(drop * 0.4f, -drop, 0.003f));
            m_Main = TwUi.Display(Root.transform, text, emMeters, TwTokens.Sun, Vector3.zero);
            m_Main.outlineColor = (Color32)TwGfx.ToColor(TwTokens.Deep);
            m_Main.outlineWidth = 0.28f;
        }

        public void SetText(string text)
        {
            string upper = text.ToUpperInvariant();
            m_Main.text = upper;
            m_Shadow.text = upper;
        }
    }
}
