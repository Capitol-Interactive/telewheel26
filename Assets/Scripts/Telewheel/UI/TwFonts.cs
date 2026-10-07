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

using TMPro;
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// The two design-system typefaces as TextMeshPro font assets, built at runtime from the
    /// TTFs in Resources/Telewheel/Fonts. If a font is missing, text falls back to TextMeshPro's
    /// default font so a missing file never leaves the game unreadable.
    /// </summary>
    public static class TwFonts
    {
        private static TMP_FontAsset s_Display;
        private static TMP_FontAsset s_Body;
        private static TMP_FontAsset s_Bold;

        // Domain reload is off in this project, so drop cached assets when Play starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Display = null;
            s_Body = null;
            s_Bold = null;
        }

        /// <summary>Lilita One: titles, buttons and other display type (always uppercase).</summary>
        public static TMP_FontAsset Display
        {
            get { return Get(ref s_Display, "Telewheel/Fonts/LilitaOne-Regular"); }
        }

        /// <summary>Nunito Regular: running text.</summary>
        public static TMP_FontAsset Body
        {
            get { return Get(ref s_Body, "Telewheel/Fonts/Nunito-Regular"); }
        }

        /// <summary>Nunito ExtraBold: headings and labels.</summary>
        public static TMP_FontAsset Bold
        {
            get { return Get(ref s_Bold, "Telewheel/Fonts/Nunito-ExtraBold"); }
        }

        /// <summary>True when the custom fonts loaded (false means the default font is standing in).</summary>
        public static bool CustomFontsLoaded
        {
            get { return Display != TMP_Settings.defaultFontAsset; }
        }

        private static TMP_FontAsset Get(ref TMP_FontAsset cache, string resourcePath)
        {
            if (cache != null)
            {
                return cache;
            }
            TMP_FontAsset fallback = TMP_Settings.defaultFontAsset;
            Font font = Resources.Load<Font>(resourcePath);
            if (font == null)
            {
                Debug.LogWarning("[Telewheel] Missing font " + resourcePath + "; using the default font.");
                cache = fallback;
                return cache;
            }
            TMP_FontAsset asset = TMP_FontAsset.CreateFontAsset(font);
            if (asset == null)
            {
                cache = fallback;
                return cache;
            }
            asset.name = font.name + " (Telewheel)";
            if (fallback != null && asset.fallbackFontAssetTable != null)
            {
                asset.fallbackFontAssetTable.Add(fallback);
            }
            cache = asset;
            return cache;
        }
    }
}
