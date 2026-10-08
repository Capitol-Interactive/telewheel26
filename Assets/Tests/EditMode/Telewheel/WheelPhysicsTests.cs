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
using NUnit.Framework;

namespace Telewheel.Tests
{
    public class WheelPhysicsTests
    {
        private static void RunToRest(WheelPhysics wheel, float dt, float maxSeconds)
        {
            float t = 0;
            while (wheel.Spinning && t < maxSeconds)
            {
                wheel.Step(dt);
                t += dt;
            }
        }

        [Test]
        public void SegmentMappingMatchesTheConvention()
        {
            var wheel = new WheelPhysics(8);
            Assert.AreEqual(0, wheel.SegmentAt(0));
            Assert.AreEqual(1, wheel.SegmentAt(-45));
            Assert.AreEqual(7, wheel.SegmentAt(45));
            Assert.AreEqual(0, wheel.SegmentAt(360));
            Assert.AreEqual(0, wheel.SegmentAt(-720));
            Assert.AreEqual(2, wheel.SegmentAt(-90 + 10));
            Assert.AreEqual(3, wheel.SegmentAt(-135 - 20));
            for (int i = 0; i < 8; i++)
            {
                Assert.AreEqual(i, wheel.SegmentAt(wheel.AngleForSegment(i)));
            }
        }

        [Test]
        public void GentleFlicksAreIgnored()
        {
            var wheel = new WheelPhysics(8);
            Assert.IsFalse(wheel.Fling(WheelPhysics.MinAcceptedFling - 1));
            Assert.IsFalse(wheel.Spinning);
        }

        [Test]
        public void FlingSpeedIsClamped()
        {
            var wheel = new WheelPhysics(8);
            Assert.IsTrue(wheel.Fling(99999));
            Assert.AreEqual(WheelPhysics.MaxFling, wheel.Velocity, 0.001f);
            Assert.IsTrue(wheel.Fling(-99999));
            Assert.AreEqual(-WheelPhysics.MaxFling, wheel.Velocity, 0.001f);
            Assert.IsTrue(wheel.Fling(WheelPhysics.MinAcceptedFling + 1));
            Assert.AreEqual(WheelPhysics.MinFling, wheel.Velocity, 0.001f);
        }

        [Test]
        public void AlwaysComesToRestExactlyOnASegmentCentre()
        {
            var random = new TwRandom(5);
            foreach (int segments in new[] { 6, 8, 10, 12 })
            {
                for (int trial = 0; trial < 60; trial++)
                {
                    var wheel = new WheelPhysics(segments);
                    int stoppedOn = -1;
                    int stops = 0;
                    wheel.Stopped += segment =>
                    {
                        stoppedOn = segment;
                        stops++;
                    };
                    float speed = random.NextRange(300, 2200);
                    if (random.NextInt(2) == 0)
                    {
                        speed = -speed;
                    }
                    float dt = random.NextInt(2) == 0 ? 1f / 60f : 1f / 90f;
                    Assert.IsTrue(wheel.Fling(speed));
                    RunToRest(wheel, dt, 30f);

                    Assert.IsFalse(wheel.Spinning, "still spinning after 30s");
                    Assert.AreEqual(1, stops);
                    Assert.AreEqual(wheel.SegmentAt(wheel.Angle), stoppedOn);
                    float remainder = Math.Abs(wheel.Angle % wheel.SegmentSize);
                    float off = Math.Min(remainder, wheel.SegmentSize - remainder);
                    Assert.Less(off, 0.001f, "rest angle " + wheel.Angle + " is not a segment centre");
                }
            }
        }

        [Test]
        public void SpinLastsAPleasantTime()
        {
            var wheel = new WheelPhysics(8);
            wheel.Fling(1500);
            float t = 0;
            while (wheel.Spinning && t < 60)
            {
                wheel.Step(1f / 60f);
                t += 1f / 60f;
            }
            Assert.Greater(t, 3f, "stopped too abruptly");
            Assert.Less(t, 9f, "takes too long to stop");
        }

        [Test]
        public void NeverReversesDirectionWhileSettling()
        {
            var wheel = new WheelPhysics(8);
            wheel.Fling(900);
            float last = wheel.Angle;
            while (wheel.Spinning)
            {
                wheel.Step(1f / 72f);
                Assert.GreaterOrEqual(wheel.Angle, last - 1e-3f);
                last = wheel.Angle;
            }
        }

        [Test]
        public void DragThenReleaseFlingsAtHandSpeed()
        {
            var wheel = new WheelPhysics(8);
            wheel.BeginDrag();
            float angle = 0;
            for (int i = 0; i < 10; i++)
            {
                angle += 20f; // 20 degrees per 1/60 s = 1200 deg/s
                wheel.Drag(angle, 1f / 60f);
            }
            Assert.IsTrue(wheel.EndDrag());
            Assert.Greater(wheel.Velocity, 500f);
            Assert.IsTrue(wheel.Spinning);
        }

        [Test]
        public void SlowDragAndReleaseDoesNotSpin()
        {
            var wheel = new WheelPhysics(8);
            wheel.BeginDrag();
            wheel.Drag(1f, 1f / 60f);
            wheel.Drag(2f, 1f / 60f);
            Assert.IsFalse(wheel.EndDrag());
            Assert.IsFalse(wheel.Spinning);
        }
    }
}
