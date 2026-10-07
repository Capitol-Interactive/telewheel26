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

namespace Telewheel
{
    /// <summary>A countdown timer driven by explicit time steps, so tests and bots control time.</summary>
    public sealed class TurnClock
    {
        private float m_Total;
        private float m_Remaining;
        private bool m_Running;

        public float Total
        {
            get { return m_Total; }
        }

        public float Remaining
        {
            get { return m_Remaining; }
        }

        public bool Running
        {
            get { return m_Running; }
        }

        public bool Expired
        {
            get { return m_Total > 0 && m_Remaining <= 0; }
        }

        /// <summary>Whole seconds left, rounded up, as shown on the clock.</summary>
        public int WholeSeconds
        {
            get { return (int)Math.Ceiling(Math.Max(0f, m_Remaining)); }
        }

        /// <summary>True once the timer is inside the warning window (and still counting).</summary>
        public bool Warning
        {
            get { return m_Running && m_Remaining <= TwTokens.TimerWarningSeconds && m_Remaining > 0; }
        }

        /// <summary>0 at the start, 1 when expired.</summary>
        public float Progress
        {
            get { return m_Total <= 0 ? 1f : 1f - Math.Max(0f, m_Remaining) / m_Total; }
        }

        public void Start(float seconds)
        {
            m_Total = seconds;
            m_Remaining = seconds;
            m_Running = true;
        }

        public void Stop()
        {
            m_Running = false;
        }

        /// <summary>Advances the clock. Returns true on the step the clock reaches zero.</summary>
        public bool Tick(float dt)
        {
            if (!m_Running || dt <= 0)
            {
                return false;
            }
            m_Remaining -= dt;
            if (m_Remaining <= 0)
            {
                m_Remaining = 0;
                m_Running = false;
                return true;
            }
            return false;
        }
    }
}
