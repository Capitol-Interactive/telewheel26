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

namespace Telewheel
{
    /// <summary>
    /// Turns a hand's pinch strength (0 open, 1 pinched), read every frame, into a press and a hold.
    /// Pressing takes a firm pinch and letting go a clearly open one, so a wobbling pinch does not
    /// flicker; and a hand that has just been picked up by the tracker, or was lost and came back,
    /// does not press, even if it is already pinching.
    /// </summary>
    public sealed class PinchTracker
    {
        /// <summary>The strength at or above which an open hand counts as pinching.</summary>
        public const float PressAt = 0.8f;

        /// <summary>The strength at or below which a pinching hand counts as open again.</summary>
        public const float ReleaseAt = 0.5f;

        private bool m_WasTracked;

        /// <summary>The fingers are together now.</summary>
        public bool Pinching { get; private set; }

        /// <summary>The fingers came together on this update (true for one update per pinch).</summary>
        public bool PressedThisFrame { get; private set; }

        /// <summary>Call once per frame.</summary>
        /// <param name="tracked">The hand is being tracked right now.</param>
        /// <param name="strength">The pinch strength, 0 to 1 (ignored while not tracked).</param>
        public void Update(bool tracked, float strength)
        {
            bool wasPinching = Pinching;
            PressedThisFrame = false;
            if (!tracked)
            {
                Pinching = false;
                m_WasTracked = false;
                return;
            }
            if (!m_WasTracked)
            {
                m_WasTracked = true;
                Pinching = strength >= PressAt;
                return;
            }
            if (!wasPinching && strength >= PressAt)
            {
                Pinching = true;
                PressedThisFrame = true;
            }
            else if (wasPinching && strength <= ReleaseAt)
            {
                Pinching = false;
            }
        }

        /// <summary>Forgets everything, as if the hand had never been seen.</summary>
        public void Reset()
        {
            Pinching = false;
            PressedThisFrame = false;
            m_WasTracked = false;
        }
    }
}
