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

using UnityEngine;

namespace Telewheel
{
    /// <summary>The player's settings, remembered between sessions.</summary>
    public static class TwPrefs
    {
        private const string MixedRealityKey = "telewheel.mixedReality";
        private const string RightHandedKey = "telewheel.rightHanded";

        /// <summary>Show passthrough instead of a virtual environment (when the device supports it).</summary>
        public static bool MixedReality
        {
            get { return PlayerPrefs.GetInt(MixedRealityKey, 0) == 1; }
            set { PlayerPrefs.SetInt(MixedRealityKey, value ? 1 : 0); }
        }

        /// <summary>True when the drawing (brush) hand is the right hand.</summary>
        public static bool RightHanded
        {
            get { return PlayerPrefs.GetInt(RightHandedKey, 1) == 1; }
            set { PlayerPrefs.SetInt(RightHandedKey, value ? 1 : 0); }
        }

        /// <summary>Puts the saved settings into effect. Called once when the game starts.</summary>
        public static void Apply()
        {
            // Open Brush's WandOnRight is the menu hand: the opposite of the drawing hand.
            OpenBrushFacade.WandOnRight = !RightHanded;
            if (MixedReality && OpenBrushFacade.PassthroughSupported)
            {
                OpenBrushFacade.SetPassthrough(true);
            }
        }

        public static void SetMixedReality(bool on)
        {
            MixedReality = on;
            PlayerPrefs.Save();
            if (OpenBrushFacade.PassthroughSupported)
            {
                OpenBrushFacade.SetPassthrough(on);
            }
        }

        public static void SetRightHanded(bool rightHanded)
        {
            RightHanded = rightHanded;
            PlayerPrefs.Save();
            OpenBrushFacade.WandOnRight = !rightHanded;
        }
    }
}
