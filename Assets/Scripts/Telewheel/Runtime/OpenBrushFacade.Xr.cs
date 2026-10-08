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
using TiltBrush;
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// The headset-specific part of the facade: the display refresh rate, and hand tracking as a way to
    /// point at and press Telewheel's buttons. Neither can be tried without a headset, so both are
    /// written defensively (a failure only turns the feature off) and both report what they found, which
    /// the self-test and the log show.
    /// </summary>
    public static partial class OpenBrushFacade
    {
        // ----- Refresh rate -----

        /// <summary>What the last <see cref="RequestRefreshRate"/> found and did, for the log and self-test.</summary>
        public static string RefreshRateStatus { get; private set; } = "not requested";

        /// <summary>
        /// Asks the headset for the highest display refresh rate it supports up to <paramref name="target"/>.
        /// Quest apps start at a lower rate unless they ask. This uses the project's OpenXR extension
        /// package (<c>XR_FB_display_refresh_rate</c>), which the Quest build turns on; Unity's own
        /// display subsystem can read the rate but not change it. The runtime changes the rate a moment
        /// after the request and then reports it, which <see cref="RefreshRateStatus"/> follows. Never throws.
        /// </summary>
        public static string RequestRefreshRate(float target)
        {
            try
            {
                float[] supported = OpenXR.Extensions.FBDisplayRefreshRate.GetDisplayRefreshRates();
                if (supported == null || supported.Length == 0)
                {
                    return RefreshRateStatus =
                        "the headset's refresh rates are not available (the feature is off, or this is not a Quest)";
                }
                float current = OpenXR.Extensions.FBDisplayRefreshRate.DisplayRefreshRate;
                float best = 0f;
                var all = new List<string>();
                foreach (float rate in supported)
                {
                    all.Add(rate.ToString("0.#"));
                    if (rate <= target + 0.01f && rate > best)
                    {
                        best = rate;
                    }
                }
                string list = "supports " + string.Join("/", all.ToArray()) + " Hz";
                if (best <= 0f)
                {
                    return RefreshRateStatus = "running at " + current + " Hz; " + list + ", none up to " + target;
                }
                if (Mathf.Abs(best - current) < 0.5f)
                {
                    return RefreshRateStatus = "running at " + current + " Hz; " + list;
                }
                OpenXR.Extensions.FBDisplayRefreshRate.OnDisplayRefreshRateChanged -= OnRefreshRateChanged;
                OpenXR.Extensions.FBDisplayRefreshRate.OnDisplayRefreshRateChanged += OnRefreshRateChanged;
                OpenXR.Extensions.FBDisplayRefreshRate.DisplayRefreshRate = best;
                return RefreshRateStatus = "asked for " + best + " Hz (was " + current + "); " + list;
            }
            catch (Exception e)
            {
                return RefreshRateStatus = "refresh rate request failed: " + e.Message;
            }
        }

        private static void OnRefreshRateChanged(float from, float to)
        {
            RefreshRateStatus = "now " + to + " Hz (was " + from + ")";
            Debug.Log("[Telewheel] Display refresh rate " + RefreshRateStatus);
        }

        // ----- Hands -----

        // Pressing needs a firm pinch, letting go a clearly open one, so a wobbling pinch does not flicker.
        private const float PinchPress = 0.8f;
        private const float PinchRelease = 0.5f;
        private const float HandSearchSeconds = 1f;

        // The XR Hands package's Meta aim-hand device is a TrackedDevice: devicePosition and deviceRotation
        // are the aim pose, pinchStrengthIndex is 0 (open) to 1 (pinched). The alternatives are only there in
        // case a package update renames things; the first name that exists wins and the self-test says which.
        private static readonly string[] HandPositionPaths = { "devicePosition", "aimPose/position", "pointer/position" };
        private static readonly string[] HandRotationPaths = { "deviceRotation", "aimPose/rotation", "pointer/rotation" };
        private static readonly string[] HandTrackedPaths = { "isTracked", "aimPose/isTracked" };
        private static readonly string[] HandPinchPaths = { "pinchStrengthIndex", "indexPressed" };
        private const string HandLayout = "MetaAimHand";

        private sealed class Hand
        {
            public string Side;
            public UnityEngine.InputSystem.InputDevice Device;
            public UnityEngine.InputSystem.InputControl<Vector3> Position;
            public UnityEngine.InputSystem.InputControl<Quaternion> Rotation;
            public UnityEngine.InputSystem.InputControl<float> Tracked;
            public UnityEngine.InputSystem.InputControl<float> Pinch;
            public bool IsTracked;
            public bool Pinching;
            public bool PressedThisFrame;
            public string Found = "no device";
        }

        private static readonly Hand[] s_Hands =
        {
            new Hand { Side = "RightHand" },
            new Hand { Side = "LeftHand" },
        };
        private static int s_HandFrame = -1;
        private static float s_NextHandSearch;

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetXrStatics()
        {
            RefreshRateStatus = "not requested";
            s_HandFrame = -1;
            s_NextHandSearch = 0f;
            foreach (Hand hand in s_Hands)
            {
                hand.Device = null;
                hand.Position = null;
                hand.Rotation = null;
                hand.Tracked = null;
                hand.Pinch = null;
                hand.IsTracked = false;
                hand.Pinching = false;
                hand.PressedThisFrame = false;
                hand.Found = "no device";
            }
        }

        /// <summary>True when a hand is tracked well enough to point with.</summary>
        public static bool HandsTracked
        {
            get
            {
                PollHands();
                foreach (Hand hand in s_Hands)
                {
                    if (hand.IsTracked && hand.Position != null && hand.Rotation != null)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>A hand's index finger and thumb just came together (once per pinch).</summary>
        public static bool HandPinchPressedThisFrame
        {
            get
            {
                PollHands();
                foreach (Hand hand in s_Hands)
                {
                    if (hand.PressedThisFrame)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>A hand is pinching now.</summary>
        public static bool HandPinchHeld
        {
            get
            {
                PollHands();
                foreach (Hand hand in s_Hands)
                {
                    if (hand.Pinching)
                    {
                        return true;
                    }
                }
                return false;
            }
        }

        /// <summary>What hand tracking found, for the self-test: which device, which controls, tracked or not.</summary>
        public static string HandInputStatus
        {
            get
            {
                PollHands();
                var lines = new List<string>();
                foreach (Hand hand in s_Hands)
                {
                    lines.Add(hand.Side + ": " + hand.Found + (hand.Device != null ? (hand.IsTracked ? ", tracked" : ", not tracked") : string.Empty));
                }
                return string.Join("; ", lines.ToArray());
            }
        }

        /// <summary>
        /// The ray from the hand being used (the one pinching, else the right, else the left), in world
        /// space. False when no hand is tracked.
        /// </summary>
        public static bool TryGetHandRay(out Ray ray)
        {
            ray = default(Ray);
            PollHands();
            Hand hand = ActiveHand();
            Quaternion rotation;
            float scale;
            Vector3 translation;
            if (hand == null || !TryGetTrackingToWorld(out rotation, out scale, out translation))
            {
                return false;
            }
            Vector3 origin = rotation * (hand.Position.ReadValue() * scale) + translation;
            Vector3 direction = rotation * (hand.Rotation.ReadValue() * Vector3.forward);
            ray = new Ray(origin, direction);
            return true;
        }

        private static Hand ActiveHand()
        {
            Hand chosen = null;
            foreach (Hand hand in s_Hands)
            {
                if (!hand.IsTracked || hand.Position == null || hand.Rotation == null)
                {
                    continue;
                }
                if (hand.Pinching)
                {
                    return hand;
                }
                if (chosen == null)
                {
                    chosen = hand; // The right hand comes first.
                }
            }
            return chosen;
        }

        // Hand poses arrive in the headset's own tracking space; the scene works in decimetres under the
        // camera rig. The camera's pose in both spaces gives the mapping, so nothing here depends on how
        // the rig is built.
        private static bool TryGetTrackingToWorld(out Quaternion rotation, out float scale, out Vector3 translation)
        {
            rotation = Quaternion.identity;
            scale = 1f;
            translation = Vector3.zero;
            Camera camera = App.VrSdk == null ? null : App.VrSdk.GetVrCamera();
            if (camera == null)
            {
                return false;
            }
            UnityEngine.XR.InputDevice head = UnityEngine.XR.InputDevices.GetDeviceAtXRNode(UnityEngine.XR.XRNode.CenterEye);
            Vector3 headPosition;
            Quaternion headRotation;
            if (!head.isValid
                || !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyePosition, out headPosition)
                || !head.TryGetFeatureValue(UnityEngine.XR.CommonUsages.centerEyeRotation, out headRotation))
            {
                return false;
            }
            Transform eye = camera.transform;
            scale = Mathf.Max(0.0001f, eye.lossyScale.x);
            rotation = eye.rotation * Quaternion.Inverse(headRotation);
            translation = eye.position - rotation * (headPosition * scale);
            return true;
        }

        // Reads both hands once per frame, however many times the properties above are asked.
        private static void PollHands()
        {
            if (s_HandFrame == Time.frameCount)
            {
                return;
            }
            s_HandFrame = Time.frameCount;
            if (Time.unscaledTime >= s_NextHandSearch)
            {
                s_NextHandSearch = Time.unscaledTime + HandSearchSeconds;
                foreach (Hand hand in s_Hands)
                {
                    if (hand.Device == null || !hand.Device.added)
                    {
                        FindHandDevice(hand);
                    }
                }
            }
            foreach (Hand hand in s_Hands)
            {
                ReadHand(hand);
            }
        }

        private static void FindHandDevice(Hand hand)
        {
            hand.Device = null;
            hand.Position = null;
            hand.Rotation = null;
            hand.Tracked = null;
            hand.Pinch = null;
            hand.Found = "no " + HandLayout + " device";
            foreach (UnityEngine.InputSystem.InputDevice device in UnityEngine.InputSystem.InputSystem.devices)
            {
                if (device.layout != HandLayout || !HasUsage(device, hand.Side))
                {
                    continue;
                }
                hand.Device = device;
                hand.Position = FindControl<Vector3>(device, HandPositionPaths);
                hand.Rotation = FindControl<Quaternion>(device, HandRotationPaths);
                hand.Tracked = FindControl<float>(device, HandTrackedPaths);
                hand.Pinch = FindControl<float>(device, HandPinchPaths);
                hand.Found = HandLayout
                    + " (position " + (hand.Position == null ? "MISSING" : hand.Position.path)
                    + ", rotation " + (hand.Rotation == null ? "MISSING" : hand.Rotation.path)
                    + ", pinch " + (hand.Pinch == null ? "MISSING" : hand.Pinch.path) + ")";
                return;
            }
        }

        private static bool HasUsage(UnityEngine.InputSystem.InputDevice device, string usage)
        {
            foreach (var candidate in device.usages)
            {
                if (candidate.ToString() == usage)
                {
                    return true;
                }
            }
            return false;
        }

        private static UnityEngine.InputSystem.InputControl<T> FindControl<T>(
            UnityEngine.InputSystem.InputDevice device, string[] paths) where T : struct
        {
            foreach (string path in paths)
            {
                var control = device.TryGetChildControl<UnityEngine.InputSystem.InputControl<T>>(path);
                if (control != null)
                {
                    return control;
                }
            }
            return null;
        }

        private static void ReadHand(Hand hand)
        {
            bool wasPinching = hand.Pinching;
            hand.PressedThisFrame = false;
            if (hand.Device == null || !hand.Device.added)
            {
                hand.IsTracked = false;
                hand.Pinching = false;
                return;
            }
            hand.IsTracked = hand.Tracked != null ? hand.Tracked.ReadValue() > 0.5f : hand.Position != null;
            if (!hand.IsTracked || hand.Pinch == null)
            {
                hand.Pinching = false;
                return;
            }
            float pinch = hand.Pinch.ReadValue();
            if (!wasPinching && pinch >= PinchPress)
            {
                hand.Pinching = true;
                hand.PressedThisFrame = true;
            }
            else if (wasPinching && pinch <= PinchRelease)
            {
                hand.Pinching = false;
            }
        }
    }
}
