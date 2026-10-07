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

using System.Text;

namespace Telewheel
{
    /// <summary>
    /// The short code a host shares so friends can join: four letters, easy to read out and to type
    /// on the in-game keyboard. It has no I or O, which look like 1 and 0.
    /// </summary>
    public static class RoomCode
    {
        public const int Length = 4;
        public const string Alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ";

        public static string Generate(TwRandom random)
        {
            var sb = new StringBuilder(Length);
            for (int i = 0; i < Length; i++)
            {
                sb.Append(Alphabet[random.NextInt(Alphabet.Length)]);
            }
            return sb.ToString();
        }

        /// <summary>Upper-cases what was typed and drops anything that is not a letter.</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }
            var sb = new StringBuilder(Length);
            foreach (char c in raw)
            {
                char upper = char.ToUpperInvariant(c);
                if (upper >= 'A' && upper <= 'Z' && sb.Length < Length)
                {
                    sb.Append(upper);
                }
            }
            return sb.ToString();
        }

        public static bool IsValid(string code)
        {
            if (code == null || code.Length != Length)
            {
                return false;
            }
            foreach (char c in code)
            {
                if (Alphabet.IndexOf(c) < 0)
                {
                    return false;
                }
            }
            return true;
        }
    }
}
