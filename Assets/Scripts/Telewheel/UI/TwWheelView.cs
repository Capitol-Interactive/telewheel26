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
    /// <summary>
    /// The spin wheel from the logo: candy wedges, a gold rim with marquee bulbs, a gold hub and
    /// a pointer. The words are hidden until the wheel stops. Rotation follows
    /// <see cref="WheelPhysics"/>: Angle degrees clockwise as the player sees it.
    /// </summary>
    public sealed class TwWheelView
    {
        public readonly GameObject Root;
        public readonly WheelPhysics Spin;

        private const int BulbCount = 24;

        private readonly Transform m_Rotor;
        private readonly GameObject[] m_Wedges;
        private readonly GameObject[] m_Bulbs;
        private readonly float m_Radius;
        private bool m_Dragging;
        private float m_DragStartPointer;
        private float m_DragStartWheel;
        private float m_BulbClock;
        private int m_Winner = -1;
        private float m_WinnerPulse;
        private int m_LastSegment;

        public TwWheelView(Transform parent, Vector3 localPosition, float radiusMeters, int segments)
        {
            m_Radius = radiusMeters;
            Spin = new WheelPhysics(segments);
            Root = new GameObject("Wheel");
            Root.transform.SetParent(parent, false);
            Root.transform.localPosition = localPosition;

            // A soft violet glow behind the wheel, then the gold rim.
            TwGfx.Shape(Root.transform, "Glow", TwGfx.Disc(radiusMeters * 1.18f, 48), TwTokens.Violet,
                new Vector3(0f, 0f, TwUi.ZPanel));
            TwGfx.Shape(Root.transform, "Band", TwGfx.Disc(radiusMeters * 1.1f, 48), TwTokens.Deep,
                new Vector3(0f, 0f, TwUi.ZGlow));
            TwGfx.Shape(Root.transform, "Rim", TwGfx.Disc(radiusMeters * 1.07f, 48), TwTokens.Gold,
                new Vector3(0f, 0f, TwUi.ZShadow));

            var rotor = new GameObject("Rotor");
            rotor.transform.SetParent(Root.transform, false);
            m_Rotor = rotor.transform;

            m_Wedges = new GameObject[segments];
            float size = 360f / segments;
            for (int i = 0; i < segments; i++)
            {
                float center = i * size;
                uint hue = TwTokens.WheelHues[i % TwTokens.WheelHues.Length];
                m_Wedges[i] = TwGfx.Shape(
                    m_Rotor, "Wedge " + i, TwGfx.Wedge(radiusMeters, center - size * 0.5f, center + size * 0.5f, 6),
                    hue, new Vector3(0f, 0f, TwUi.ZFace));
                // A thin dark seam between wedges, drawn just behind them.
                Vector2 edge = TwGfx.Clockwise(center + size * 0.5f) * radiusMeters * 0.5f;
                GameObject seam = TwUi.Pill(m_Rotor, "Seam", radiusMeters, TwUi.Px(5f), TwTokens.Deep,
                    new Vector3(edge.x, edge.y, TwUi.ZFace * 0.5f));
                seam.transform.localRotation = Quaternion.Euler(0f, 0f, 90f - (center + size * 0.5f));
            }

            // Marquee bulbs around the rim.
            m_Bulbs = new GameObject[BulbCount];
            for (int i = 0; i < BulbCount; i++)
            {
                Vector2 p = TwGfx.Clockwise(360f * i / BulbCount) * radiusMeters * 1.085f;
                m_Bulbs[i] = TwGfx.Shape(Root.transform, "Bulb " + i, TwGfx.Disc(radiusMeters * 0.03f, 10),
                    TwTokens.Bulb, new Vector3(p.x, p.y, TwUi.ZShadow * 0.5f));
            }

            // Hub and pointer.
            TwGfx.Shape(Root.transform, "HubBorder", TwGfx.Disc(radiusMeters * 0.2f, 32), TwTokens.Deep,
                new Vector3(0f, 0f, -TwUi.ZFace * 0.5f));
            TwGfx.Shape(Root.transform, "Hub", TwGfx.Disc(radiusMeters * 0.16f, 32), TwTokens.Gold,
                new Vector3(0f, 0f, -TwUi.ZFace));
            float pw = radiusMeters * 0.26f;
            float ph = radiusMeters * 0.32f;
            TwGfx.Shape(Root.transform, "PointerBorder", TwGfx.Pointer(pw * 1.3f, ph * 1.25f), TwTokens.Deep,
                new Vector3(0f, radiusMeters * 1.18f + TwUi.Px(4f), -TwUi.ZFace));
            TwGfx.Shape(Root.transform, "Pointer", TwGfx.Pointer(pw, ph), TwTokens.Sun,
                new Vector3(0f, radiusMeters * 1.18f, -TwUi.ZFace * 2f));

            // Something to point at, for grabbing the wheel.
            var box = Root.AddComponent<BoxCollider>();
            box.size = new Vector3(radiusMeters * 2.2f, radiusMeters * 2.2f, 0.05f);
            box.center = new Vector3(0f, 0f, 0.02f);

            Apply();
        }

        /// <summary>Raised each time a new segment passes the pointer while the wheel is moving.</summary>
        public event Action Ticked;

        public bool Spinning
        {
            get { return Spin.Spinning || m_Dragging; }
        }

        public BoxCollider Collider
        {
            get { return Root.GetComponent<BoxCollider>(); }
        }

        /// <summary>Starts a spin at the given speed (degrees per second, sign gives direction).</summary>
        public bool Fling(float degreesPerSecond)
        {
            return Spin.Fling(degreesPerSecond);
        }

        /// <summary>
        /// Lets the player grab the wheel with the pointer and flick it. Call every frame while the
        /// wheel is showing. Returns true while the player is holding it.
        /// </summary>
        public bool UpdateGrab(Ray ray, bool pressedThisFrame, bool held, float dt)
        {
            if (!m_Dragging)
            {
                if (pressedThisFrame && !Spin.Spinning)
                {
                    float angle;
                    if (TryPointerAngle(ray, out angle) && IsOverWheel(ray))
                    {
                        m_Dragging = true;
                        m_DragStartPointer = angle;
                        m_DragStartWheel = Spin.Angle;
                        Spin.BeginDrag();
                        OpenBrushFacade.EatPaintInput();
                    }
                }
                return m_Dragging;
            }

            if (!held)
            {
                m_Dragging = false;
                Spin.EndDrag();
                return false;
            }
            float now;
            if (TryPointerAngle(ray, out now))
            {
                float delta = Mathf.DeltaAngle(m_DragStartPointer, now);
                // Keep following across the wrap by moving the reference with the pointer.
                m_DragStartPointer = now;
                m_DragStartWheel += delta;
                Spin.Drag(m_DragStartWheel, dt);
            }
            return true;
        }

        private bool IsOverWheel(Ray ray)
        {
            RaycastHit hit;
            BoxCollider box = Collider;
            return box != null && box.Raycast(ray, out hit, 200f);
        }

        /// <summary>The angle (degrees clockwise from the top) at which the ray crosses the wheel's plane.</summary>
        private bool TryPointerAngle(Ray ray, out float degrees)
        {
            var plane = new Plane(Root.transform.forward, Root.transform.position);
            float enter;
            if (!plane.Raycast(ray, out enter))
            {
                degrees = 0f;
                return false;
            }
            Vector3 local = Root.transform.InverseTransformPoint(ray.GetPoint(enter));
            degrees = Mathf.Atan2(local.x, local.y) * Mathf.Rad2Deg;
            return true;
        }

        /// <summary>Marks the segment the wheel stopped on, so it pulses.</summary>
        public void HighlightWinner(int segment)
        {
            m_Winner = segment;
            m_WinnerPulse = 0f;
        }

        public void Tick(float dt)
        {
            Spin.Step(dt);
            Apply();

            int segment = Spin.CurrentSegment;
            if (segment != m_LastSegment)
            {
                m_LastSegment = segment;
                Action handler = Ticked;
                if (Spinning && handler != null)
                {
                    handler();
                }
            }

            // Chase the marquee bulbs while spinning, otherwise a slow twinkle.
            m_BulbClock += dt * (Spinning ? 18f : 3f);
            for (int i = 0; i < m_Bulbs.Length; i++)
            {
                bool lit = ((int)m_BulbClock + i) % 3 == 0;
                m_Bulbs[i].GetComponent<MeshRenderer>().sharedMaterial =
                    TwGfx.Flat(lit ? TwTokens.Bulb : TwTokens.Gold);
            }

            if (m_Winner >= 0 && m_Winner < m_Wedges.Length)
            {
                m_WinnerPulse += dt;
                float pulse = 1f + 0.06f * Mathf.Sin(m_WinnerPulse * 8f);
                m_Wedges[m_Winner].transform.localScale = new Vector3(pulse, pulse, 1f);
            }
        }

        private void Apply()
        {
            // Unity turns positive Z rotation counter-clockwise, so negate for a clockwise angle.
            m_Rotor.localRotation = Quaternion.Euler(0f, 0f, -Spin.Angle);
        }
    }
}
