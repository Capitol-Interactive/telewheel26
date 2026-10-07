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
    /// <summary>Cleans up what a player typed before it is stored and shown to everyone.</summary>
    public static class GuessNormalizer
    {
        public const int MaxLength = 40;

        /// <summary>Strips control characters, collapses whitespace and caps the length.</summary>
        public static string Normalize(string raw)
        {
            if (string.IsNullOrEmpty(raw))
            {
                return string.Empty;
            }
            var sb = new StringBuilder(raw.Length);
            bool pendingSpace = false;
            foreach (char c in raw)
            {
                if (char.IsControl(c) && !char.IsWhiteSpace(c))
                {
                    continue;
                }
                if (char.IsWhiteSpace(c))
                {
                    pendingSpace = sb.Length > 0;
                    continue;
                }
                if (pendingSpace)
                {
                    sb.Append(' ');
                    pendingSpace = false;
                }
                sb.Append(c);
                if (sb.Length >= MaxLength)
                {
                    break;
                }
            }
            return sb.ToString();
        }

        public static bool IsAcceptable(string raw)
        {
            return Normalize(raw).Length > 0;
        }
    }
}
