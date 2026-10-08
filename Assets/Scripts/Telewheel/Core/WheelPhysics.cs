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
    /// <summary>
    /// Spin physics for the word wheel: grab and drag, release to fling, then a long ease-out that
    /// always comes to rest exactly on a segment centre (so the pointer never lands on a seam).
    ///
    /// Conventions: Angle is in degrees and positive turns the wheel clockwise as the player sees
    /// it. Segment i is centred under the fixed pointer when Angle is -i * SegmentSize (mod 360),
    /// so segment centres sit at integer multiples of SegmentSize.
    /// </summary>
    public sealed class WheelPhysics
    {
        public const float MinFling = 360f;
        public const float MaxFling = 1800f;

        /// <summary>Flicks slower than this are ignored (a gentle touch should not spin the wheel).</summary>
        public const float MinAcceptedFling = 200f;

        private const float FreeDecay = 1.1f;
        private const float SettleSpeed = 40f;
        private const float MinSettleDistance = 2f;

        private bool m_Dragging;
        private bool m_Settling;
        private float m_SettleAccel;
        private float m_SettleTarget;
        private float m_SmoothedDragSpeed;

        public WheelPhysics(int segments)
        {
            if (segments < 2)
            {
                throw new ArgumentOutOfRangeException("segments");
            }
            Segments = segments;
        }

        public int Segments { get; private set; }

        public float SegmentSize
        {
            get { return 360f / Segments; }
        }

        /// <summary>Current rotation in degrees (unbounded).</summary>
        public float Angle { get; private set; }

        /// <summary>Degrees per second.</summary>
        public float Velocity { get; private set; }

        public bool Spinning { get; private set; }

        /// <summary>Raised once, on the step the wheel comes to rest.</summary>
        public event Action<int> Stopped;

        /// <summary>The segment under the pointer for any rotation.</summary>
        public int SegmentAt(float angle)
        {
            double size = SegmentSize;
            long index = (long)Math.Floor((-angle + size / 2.0) / size);
            long wrapped = index % Segments;
            return (int)(wrapped < 0 ? wrapped + Segments : wrapped);
        }

        public int CurrentSegment
        {
            get { return SegmentAt(Angle); }
        }

        /// <summary>A rotation that centres <paramref name="segment"/> under the pointer.</summary>
        public float AngleForSegment(int segment)
        {
            return -segment * SegmentSize;
        }

        public void BeginDrag()
        {
            m_Dragging = true;
            Spinning = false;
            Velocity = 0;
            m_SmoothedDragSpeed = 0;
            m_Settling = false;
        }

        /// <summary>Moves the wheel with the player's hand. <paramref name="dt"/> is the time since the last call.</summary>
        public void Drag(float newAngle, float dt)
        {
            if (!m_Dragging)
            {
                return;
            }
            if (dt > 0)
            {
                float instant = (newAngle - Angle) / dt;
                m_SmoothedDragSpeed = m_SmoothedDragSpeed * 0.6f + instant * 0.4f;
            }
            Angle = newAngle;
        }

        /// <summary>Lets go of the wheel, flinging it at the speed of the last drag.</summary>
        public bool EndDrag()
        {
            m_Dragging = false;
            return Fling(m_SmoothedDragSpeed);
        }

        /// <summary>
        /// Starts a spin at <paramref name="degreesPerSecond"/> (sign gives direction), clamped to the
        /// allowed range. Returns false when the flick was too gentle to count.
        /// </summary>
        public bool Fling(float degreesPerSecond)
        {
            float speed = Math.Abs(degreesPerSecond);
            if (speed < MinAcceptedFling)
            {
                return false;
            }
            speed = Math.Min(MaxFling, Math.Max(MinFling, speed));
            Velocity = degreesPerSecond < 0 ? -speed : speed;
            Spinning = true;
            m_Settling = false;
            return true;
        }

        /// <summary>Advances the spin by <paramref name="dt"/> seconds.</summary>
        public void Step(float dt)
        {
            if (!Spinning || m_Dragging || dt <= 0)
            {
                return;
            }
            if (m_Settling)
            {
                StepSettle(dt);
                return;
            }
            double factor = Math.Exp(-FreeDecay * dt);
            // Exact integral of v * e^(-k t) over the step, so the free spin does not drift with the frame rate.
            Angle += (float)(Velocity * (1.0 - factor) / FreeDecay);
            Velocity = (float)(Velocity * factor);
            if (Math.Abs(Velocity) < SettleSpeed)
            {
                BeginSettle();
            }
        }

        /// <summary>
        /// Chooses the next segment centre ahead of the wheel and a constant deceleration that
        /// brings the wheel to a stop exactly there.
        /// </summary>
        private void BeginSettle()
        {
            double size = SegmentSize;
            double target = Velocity > 0
                ? Math.Ceiling(Angle / size) * size
                : Math.Floor(Angle / size) * size;
            double distance = Math.Abs(target - Angle);
            if (distance < MinSettleDistance)
            {
                target += Velocity > 0 ? size : -size;
                distance = Math.Abs(target - Angle);
            }
            double speed = Math.Abs(Velocity);
            m_SettleAccel = (float)(speed * speed / (2.0 * distance));
            m_SettleTarget = (float)target;
            m_Settling = true;
        }

        private void StepSettle(float dt)
        {
            float speed = Math.Abs(Velocity);
            float stopTime = speed / m_SettleAccel;
            if (dt >= stopTime)
            {
                Rest();
                return;
            }
            float sign = Velocity > 0 ? 1f : -1f;
            Angle += sign * (speed * dt - 0.5f * m_SettleAccel * dt * dt);
            Velocity = sign * (speed - m_SettleAccel * dt);
        }

        private void Rest()
        {
            Spinning = false;
            m_Settling = false;
            Velocity = 0;
            Angle = m_SettleTarget;
            Action<int> handler = Stopped;
            if (handler != null)
            {
                handler(CurrentSegment);
            }
        }
    }
}
