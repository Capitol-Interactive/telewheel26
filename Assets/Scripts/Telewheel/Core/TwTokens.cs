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
    /// Design tokens from the Telewheel design system (Night theme is the VR default).
    /// Colors are 0xRRGGBB. Keep these in sync with project/tokens.json in the design system.
    /// </summary>
    public static class TwTokens
    {
        // Surfaces and text (Night theme).
        public const uint Surface100 = 0x1E0257;
        public const uint Surface200 = 0x2D0B7D;
        public const uint Surface300 = 0x3F16A3;
        public const uint Line = 0xA98AF6;
        public const uint Ink = 0xFFFFFF;
        public const uint InkMuted = 0xCDBDF7;
        public const uint Accent = 0xFBEB1B;
        public const uint FocusRing = 0xFBEB1B;

        // Candy wheel hues.
        public const uint Sun = 0xFBEB1B;
        public const uint Tangerine = 0xFC810B;
        public const uint Magenta = 0xFF00B7;
        public const uint Cobalt = 0x006CFD;
        public const uint Lime = 0xA1EB08;
        public const uint Ion = 0x28A3FB;

        // Constants.
        public const uint Night = 0x1E0257;
        public const uint Violet = 0x4300A7;
        public const uint Deep = 0x0A0340;
        public const uint Gold = 0xFFC81A;
        public const uint Bulb = 0xFFFBE8;
        public const uint Paper = 0xFFFFFF;
        public const uint OnBright = Night;
        public const uint OnCobalt = 0xFFFFFF;

        /// <summary>Hard offset shadow colour for resting buttons and cards (Night).</summary>
        public const uint ShadowChunk = 0x12013A;

        /// <summary>Wheel segment colours, in the order the design system cycles them.</summary>
        public static readonly uint[] WheelHues = { Magenta, Cobalt, Tangerine, Lime, Sun, Ion };

        // Spacing (design pixels).
        public const int Space1 = 4;
        public const int Space2 = 8;
        public const int Space3 = 12;
        public const int Space4 = 16;
        public const int Space6 = 24;
        public const int Space8 = 32;
        public const int Space12 = 48;

        // Radii (design pixels).
        public const int RadiusSm = 8;
        public const int RadiusMd = 16;
        public const int RadiusLg = 28;

        // Type sizes (design pixels). Nothing below Body in VR.
        public const int DisplayXl = 72;
        public const int DisplayLg = 48;
        public const int DisplayMd = 32;
        public const int Heading = 24;
        public const int Body = 18;
        public const int Label = 14;

        /// <summary>Press travel of a chunky button, in design pixels.</summary>
        public const int PressTravel = 4;

        /// <summary>Border width of primary buttons, in design pixels.</summary>
        public const int BorderWidth = 3;

        /// <summary>Seconds left at which the timer turns magenta.</summary>
        public const float TimerWarningSeconds = 10f;

        /// <summary>
        /// Conversion from design pixels to metres in VR. 48px (the minimum hit target) is 7.2cm.
        /// </summary>
        public const float PixelToMeters = 0.0015f;

        public static byte R(uint rgb)
        {
            return (byte)((rgb >> 16) & 0xFF);
        }

        public static byte G(uint rgb)
        {
            return (byte)((rgb >> 8) & 0xFF);
        }

        public static byte B(uint rgb)
        {
            return (byte)(rgb & 0xFF);
        }
    }
}
