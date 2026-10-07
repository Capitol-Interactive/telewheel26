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
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// Points at Telewheel buttons and clicks them. In VR the ray comes from the brush controller
    /// (with a laser line); on a desktop it is the centre of the screen (with a small reticle).
    /// It runs before Open Brush's own update so that a click on a Telewheel button is marked as
    /// used before Open Brush decides whether to start a stroke.
    /// </summary>
    [DefaultExecutionOrder(-100)]
    public sealed class TwPointer : MonoBehaviour
    {
        private const float MaxDistanceUnits = 200f;
        private const float ReticleDistanceUnits = 15f;

        private TwButton m_Hover;
        private LineRenderer m_Laser;
        private GameObject m_Reticle;

        /// <summary>The button under the pointer, if any.</summary>
        public TwButton Hover
        {
            get { return m_Hover; }
        }

        private void Awake()
        {
            m_Reticle = TwGfx.Shape(transform, "Reticle", TwGfx.Disc(0.12f, 16), TwTokens.Sun, Vector3.zero);
            m_Reticle.SetActive(false);

            var laserObject = new GameObject("Laser");
            laserObject.transform.SetParent(transform, false);
            m_Laser = laserObject.AddComponent<LineRenderer>();
            m_Laser.positionCount = 2;
            m_Laser.startWidth = 0.03f;
            m_Laser.endWidth = 0.03f;
            m_Laser.sharedMaterial = TwGfx.Flat(TwTokens.Ion);
            m_Laser.useWorldSpace = true;
            m_Laser.enabled = false;
        }

        private void OnDisable()
        {
            SetHover(null);
        }

        private void Update()
        {
            IList<TwButton> buttons = TwButton.Active;
            Ray ray;
            if (buttons.Count == 0 || !OpenBrushFacade.TryGetPointerRay(out ray))
            {
                SetHover(null);
                m_Reticle.SetActive(false);
                m_Laser.enabled = false;
                return;
            }

            TwButton best = null;
            float bestDistance = float.MaxValue;
            for (int i = 0; i < buttons.Count; i++)
            {
                TwButton button = buttons[i];
                if (button == null || button.Collider == null || !button.isActiveAndEnabled)
                {
                    continue;
                }
                RaycastHit hit;
                if (button.Collider.Raycast(ray, out hit, MaxDistanceUnits) && hit.distance < bestDistance)
                {
                    bestDistance = hit.distance;
                    best = button;
                }
            }

            // Open Brush's own panels take priority when the player is pointing at one.
            if (best != null && OpenBrushFacade.IsPointingAtOpenBrushUi)
            {
                best = null;
            }

            SetHover(best);
            ShowAim(ray, best != null ? bestDistance : -1f);

            if (best != null && OpenBrushFacade.PrimaryPressedThisFrame)
            {
                // Stop Open Brush also starting a stroke from this click.
                OpenBrushFacade.EatPaintInput();
                best.Press();
            }
        }

        private void ShowAim(Ray ray, float hitDistance)
        {
            bool vr = !OpenBrushFacade.IsMonoscopic;
            float distance = hitDistance >= 0f ? hitDistance : ReticleDistanceUnits;
            Vector3 point = ray.origin + ray.direction * distance;

            m_Laser.enabled = vr;
            if (vr)
            {
                m_Laser.SetPosition(0, ray.origin);
                m_Laser.SetPosition(1, point);
            }
            // The reticle is the only aim cue on a desktop; in VR it marks the hit point.
            bool showReticle = !vr || hitDistance >= 0f;
            m_Reticle.SetActive(showReticle);
            if (showReticle)
            {
                m_Reticle.transform.position = point;
                m_Reticle.transform.rotation = Quaternion.LookRotation(ray.direction, Vector3.up);
                float size = vr ? 1f : Mathf.Max(0.3f, distance / ReticleDistanceUnits);
                m_Reticle.transform.localScale = Vector3.one * size;
            }
        }

        private void SetHover(TwButton button)
        {
            if (m_Hover == button)
            {
                return;
            }
            if (m_Hover != null)
            {
                m_Hover.SetHovered(false);
            }
            m_Hover = button;
            if (m_Hover != null)
            {
                m_Hover.SetHovered(true);
            }
        }
    }
}
