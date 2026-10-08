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

using NUnit.Framework;

namespace Telewheel.Tests
{
    public class PinchTrackerTests
    {
        private static PinchTracker TrackedOpenHand()
        {
            var tracker = new PinchTracker();
            tracker.Update(true, 0f); // The first sight of a hand never presses.
            return tracker;
        }

        [Test]
        public void ANewPinchPressesOnceAndThenHolds()
        {
            PinchTracker tracker = TrackedOpenHand();
            tracker.Update(true, 0.9f);
            Assert.IsTrue(tracker.PressedThisFrame);
            Assert.IsTrue(tracker.Pinching);
            tracker.Update(true, 0.95f);
            Assert.IsFalse(tracker.PressedThisFrame, "one press per pinch");
            Assert.IsTrue(tracker.Pinching);
        }

        [Test]
        public void AWeakPinchDoesNothing()
        {
            PinchTracker tracker = TrackedOpenHand();
            tracker.Update(true, 0.79f);
            Assert.IsFalse(tracker.Pinching);
            Assert.IsFalse(tracker.PressedThisFrame);
        }

        [Test]
        public void AWobblingPinchDoesNotFlicker()
        {
            PinchTracker tracker = TrackedOpenHand();
            tracker.Update(true, 0.85f);
            foreach (float strength in new[] { 0.7f, 0.6f, 0.75f, 0.55f, 0.9f })
            {
                tracker.Update(true, strength);
                Assert.IsTrue(tracker.Pinching, "still pinching at " + strength);
                Assert.IsFalse(tracker.PressedThisFrame, "no new press at " + strength);
            }
        }

        [Test]
        public void OpeningTheHandReleasesAndAllowsAnotherPress()
        {
            PinchTracker tracker = TrackedOpenHand();
            tracker.Update(true, 1f);
            tracker.Update(true, 0.5f);
            Assert.IsFalse(tracker.Pinching);
            tracker.Update(true, 0.85f);
            Assert.IsTrue(tracker.PressedThisFrame);
        }

        [Test]
        public void ALostHandIsNotPinching()
        {
            PinchTracker tracker = TrackedOpenHand();
            tracker.Update(true, 1f);
            tracker.Update(false, 1f);
            Assert.IsFalse(tracker.Pinching);
            Assert.IsFalse(tracker.PressedThisFrame);
        }

        [Test]
        public void AHandThatComesBackAlreadyPinchingDoesNotPress()
        {
            PinchTracker tracker = TrackedOpenHand();
            tracker.Update(false, 0f);
            tracker.Update(true, 1f);
            Assert.IsTrue(tracker.Pinching, "it is pinching");
            Assert.IsFalse(tracker.PressedThisFrame, "but nothing was pressed by it");
            tracker.Update(true, 0.2f);
            tracker.Update(true, 0.9f);
            Assert.IsTrue(tracker.PressedThisFrame, "a later pinch does");
        }

        [Test]
        public void TheFirstSightOfAHandNeverPresses()
        {
            var tracker = new PinchTracker();
            tracker.Update(true, 1f);
            Assert.IsFalse(tracker.PressedThisFrame);
            Assert.IsTrue(tracker.Pinching);
        }

        [Test]
        public void ResetForgetsTheHand()
        {
            PinchTracker tracker = TrackedOpenHand();
            tracker.Update(true, 1f);
            tracker.Reset();
            Assert.IsFalse(tracker.Pinching);
            tracker.Update(true, 1f);
            Assert.IsFalse(tracker.PressedThisFrame, "a hand seen after a reset is a new hand");
        }
    }
}
