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

namespace Telewheel
{
    /// <summary>Parses the plain-text word lists shipped in Resources/Telewheel.</summary>
    public static class WordList
    {
        public const int MaxWordLength = 20;

        /// <summary>
        /// One word or short phrase per line. Blank lines and lines starting with # are ignored,
        /// repeated words (any case) are dropped, and overlong lines are skipped.
        /// </summary>
        public static List<string> Parse(string text)
        {
            var words = new List<string>();
            if (string.IsNullOrEmpty(text))
            {
                return words;
            }
            var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            foreach (string rawLine in text.Split('\n'))
            {
                string line = GuessNormalizer.Normalize(rawLine);
                if (line.Length == 0 || line[0] == '#' || line.Length > MaxWordLength)
                {
                    continue;
                }
                if (seen.Add(line))
                {
                    words.Add(line);
                }
            }
            return words;
        }
    }

    /// <summary>
    /// Hands out wheel words without repeating one until the whole list has been used. The wheel
    /// shows several candidates, the player's spin picks one, and only that one is used up.
    /// </summary>
    public sealed class WordDeck
    {
        private readonly List<string> m_All;
        private readonly List<string> m_Remaining;
        private readonly TwRandom m_Random;

        public WordDeck(IEnumerable<string> words, int seed)
        {
            m_All = new List<string>(words);
            m_Random = new TwRandom(seed);
            m_Remaining = new List<string>(m_All);
        }

        public int TotalCount
        {
            get { return m_All.Count; }
        }

        public int RemainingCount
        {
            get { return m_Remaining.Count; }
        }

        /// <summary>
        /// Picks <paramref name="count"/> distinct candidates at random without using them up. When
        /// fewer than that are left the deck is refilled first.
        /// </summary>
        public List<string> Draw(int count)
        {
            if (count > m_All.Count)
            {
                throw new InvalidOperationException(
                    "Word list has " + m_All.Count + " words but the wheel needs " + count);
            }
            if (m_Remaining.Count < count)
            {
                m_Remaining.Clear();
                m_Remaining.AddRange(m_All);
            }
            var pool = new List<string>(m_Remaining);
            var picked = new List<string>(count);
            for (int i = 0; i < count; i++)
            {
                int index = m_Random.NextInt(pool.Count);
                picked.Add(pool[index]);
                pool.RemoveAt(index);
            }
            return picked;
        }

        /// <summary>Marks a word as used so it does not come up again until the deck is refilled.</summary>
        public void Consume(string word)
        {
            m_Remaining.Remove(word);
        }
    }
}
