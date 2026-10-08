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
    /// <summary>Who a player is in the lobby: a typed name and a pick from the icon palette.</summary>
    public sealed class PlayerProfile
    {
        public const int IconCount = 8;
        public const int MaxNameLength = 16;

        public string Name = string.Empty;
        public int Icon;

        public PlayerProfile()
        {
        }

        public PlayerProfile(string name, int icon)
        {
            Name = CleanName(name);
            Icon = CleanIcon(icon);
        }

        /// <summary>Strips control characters, collapses whitespace and caps the length.</summary>
        public static string CleanName(string raw)
        {
            string clean = GuessNormalizer.Normalize(raw);
            return clean.Length > MaxNameLength ? clean.Substring(0, MaxNameLength).TrimEnd() : clean;
        }

        /// <summary>Capital letter at the start of each word ("ann lee" becomes "Ann Lee"); never lowers a letter.</summary>
        public static string Capitalize(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return string.Empty;
            }
            char[] letters = name.ToCharArray();
            bool startOfWord = true;
            for (int i = 0; i < letters.Length; i++)
            {
                if (startOfWord && char.IsLetter(letters[i]))
                {
                    letters[i] = char.ToUpperInvariant(letters[i]);
                }
                startOfWord = letters[i] == ' ' || letters[i] == '-';
            }
            return new string(letters);
        }

        /// <summary>Wraps any number onto the icon palette.</summary>
        public static int CleanIcon(int icon)
        {
            return ((icon % IconCount) + IconCount) % IconCount;
        }
    }
}
