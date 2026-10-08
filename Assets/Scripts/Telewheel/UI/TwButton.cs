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
using System.Collections.Generic;
using TMPro;
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// A chunky pill button built to the design system: a face and border, a hard offset shadow, a
    /// uppercase Lilita One label, a focus ring when pointed at, and a press that drops the face by
    /// 4px and shortens the shadow. Clicked by <see cref="TwPointer"/>.
    /// </summary>
    public sealed class TwButton : MonoBehaviour
    {
        public enum Style
        {
            Primary,
            Secondary,
            Ghost,

            /// <summary>The green "Yes" button.</summary>
            Positive,

            /// <summary>The red "No" button.</summary>
            Negative,
        }

        private const float PressSeconds = 0.12f;

        private static readonly List<TwButton> s_Active = new List<TwButton>();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Active.Clear();
        }

        /// <summary>Every enabled button, for the pointer to test.</summary>
        public static IList<TwButton> Active
        {
            get { return s_Active; }
        }

        public Action Clicked;

        private Style m_Style;
        private bool m_Interactable = true;
        private bool m_Hovered;
        private float m_PressTimer;
        private Transform m_Body;
        private Transform m_Shadow;
        private GameObject m_Ring;
        private GameObject m_Face;
        private TextMeshPro m_Label;
        private BoxCollider m_Collider;
        private float m_Travel;
        private float m_ShadowOffset;

        public BoxCollider Collider
        {
            get { return m_Collider; }
        }

        public bool Interactable
        {
            get { return m_Interactable; }
        }

        public string Label
        {
            get { return m_Label == null ? string.Empty : m_Label.text; }
        }

        /// <summary>Creates a button. Sizes and positions are in metres in the parent's space.</summary>
        public static TwButton Create(
            Transform parent,
            string label,
            float width,
            float height,
            Style style,
            Vector3 localPosition,
            Action onClick)
        {
            var go = new GameObject("Button " + label);
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            TwButton button = go.AddComponent<TwButton>();
            button.Build(label, width, height, style);
            button.Clicked = onClick;
            return button;
        }

        private void Build(string label, float width, float height, Style style)
        {
            m_Style = style;
            m_Travel = TwUi.Px(TwTokens.PressTravel);
            m_ShadowOffset = TwUi.Px(6f);
            float border = TwUi.Px(TwTokens.BorderWidth);

            var body = new GameObject("Body");
            body.transform.SetParent(transform, false);
            m_Body = body.transform;

            if (style != Style.Ghost)
            {
                uint faceColor = FaceColor(style);
                uint borderColor = style == Style.Secondary ? TwTokens.Line : TwTokens.Deep;
                TwUi.Pill(m_Body, "Border", width + border * 2f, height + border * 2f, borderColor,
                    new Vector3(0f, 0f, TwUi.ZBorder));
                m_Face = TwUi.Pill(m_Body, "Face", width, height, faceColor, new Vector3(0f, 0f, TwUi.ZFace));

                GameObject shadow = TwUi.Pill(transform, "Shadow", width + border * 2f, height + border * 2f,
                    TwTokens.ShadowChunk, new Vector3(0f, -m_ShadowOffset, TwUi.ZShadow));
                m_Shadow = shadow.transform;
            }

            m_Ring = TwUi.Pill(transform, "Focus", width + border * 6f, height + border * 6f,
                TwTokens.FocusRing, new Vector3(0f, 0f, TwUi.ZGlow));
            m_Ring.SetActive(false);

            m_Label = TwUi.Text(
                m_Body,
                label.ToUpperInvariant(),
                TwUi.Px(TwTokens.DisplayMd) * (height > TwUi.Px(60f) ? 1f : 0.85f),
                LabelColor(true),
                TwFonts.Display,
                Vector3.zero);

            m_Collider = gameObject.AddComponent<BoxCollider>();
            m_Collider.size = new Vector3(width + border * 2f, height + border * 2f, 0.02f);
            m_Collider.center = new Vector3(0f, 0f, 0.005f);
        }

        private static uint FaceColor(Style style)
        {
            switch (style)
            {
                case Style.Primary:
                    return TwTokens.Sun;
                case Style.Positive:
                    return TwTokens.Lime;
                case Style.Negative:
                    return TwTokens.Magenta;
                default:
                    return TwTokens.Surface300;
            }
        }

        private uint LabelColor(bool interactable)
        {
            if (!interactable)
            {
                return TwTokens.InkMuted;
            }
            switch (m_Style)
            {
                case Style.Primary:
                case Style.Positive:
                case Style.Negative:
                    return TwTokens.OnBright;
                case Style.Ghost:
                    return TwTokens.Accent;
                default:
                    return TwTokens.Ink;
            }
        }

        private void OnEnable()
        {
            if (!s_Active.Contains(this))
            {
                s_Active.Add(this);
            }
        }

        private void OnDisable()
        {
            s_Active.Remove(this);
            m_Hovered = false;
        }

        public void SetLabel(string label)
        {
            if (m_Label != null)
            {
                m_Label.text = label.ToUpperInvariant();
            }
        }

        public void SetInteractable(bool interactable)
        {
            m_Interactable = interactable;
            if (m_Label != null)
            {
                m_Label.color = TwGfx.ToColor(LabelColor(interactable));
            }
            if (m_Face != null)
            {
                m_Face.GetComponent<MeshRenderer>().sharedMaterial =
                    TwGfx.Flat(interactable ? FaceColor(m_Style) : TwTokens.Surface200);
            }
        }

        /// <summary>Shows or hides the focus ring while the pointer is on the button.</summary>
        public void SetHovered(bool hovered)
        {
            if (m_Hovered == hovered)
            {
                return;
            }
            m_Hovered = hovered;
            if (m_Ring != null)
            {
                m_Ring.SetActive(hovered && m_Interactable);
            }
        }

        /// <summary>Presses the button: plays the press animation and runs the click handler.</summary>
        public void Press()
        {
            if (!m_Interactable)
            {
                return;
            }
            m_PressTimer = PressSeconds;
            TwAudio.Play(TwSound.Click, 0.4f);
            Action handler = Clicked;
            if (handler != null)
            {
                handler();
            }
        }

        private void Update()
        {
            if (m_Body == null)
            {
                return;
            }
            if (m_PressTimer > 0f)
            {
                m_PressTimer -= Time.unscaledDeltaTime;
            }
            float pressed = m_PressTimer > 0f ? 1f : 0f;
            m_Body.localPosition = new Vector3(0f, -m_Travel * pressed, 0f);
            if (m_Shadow != null)
            {
                m_Shadow.localPosition = new Vector3(0f, -m_ShadowOffset + m_Travel * pressed, TwUi.ZShadow);
            }
        }
    }
}
