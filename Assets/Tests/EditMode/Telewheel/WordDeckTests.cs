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

using System.Collections.Generic;
using NUnit.Framework;

namespace Telewheel.Tests
{
    public class WordDeckTests
    {
        private static List<string> Words(int count)
        {
            var words = new List<string>();
            for (int i = 0; i < count; i++)
            {
                words.Add("word" + i);
            }
            return words;
        }

        [Test]
        public void DrawReturnsDistinctWords()
        {
            var deck = new WordDeck(Words(30), 7);
            List<string> picked = deck.Draw(8);
            Assert.AreEqual(8, picked.Count);
            Assert.AreEqual(8, new HashSet<string>(picked).Count);
        }

        [Test]
        public void SameSeedGivesTheSameDraws()
        {
            var a = new WordDeck(Words(30), 99);
            var b = new WordDeck(Words(30), 99);
            for (int i = 0; i < 5; i++)
            {
                CollectionAssert.AreEqual(a.Draw(8), b.Draw(8));
            }
        }

        [Test]
        public void ConsumedWordsDoNotComeBackUntilTheDeckRefills()
        {
            var deck = new WordDeck(Words(10), 3);
            var used = new HashSet<string>();
            for (int i = 0; i < 6; i++)
            {
                List<string> candidates = deck.Draw(4);
                foreach (string word in candidates)
                {
                    Assert.IsFalse(used.Contains(word), word + " was used already");
                }
                used.Add(candidates[0]);
                deck.Consume(candidates[0]);
            }
            Assert.AreEqual(4, deck.RemainingCount);
        }

        [Test]
        public void RefillsWhenTooFewWordsRemain()
        {
            var deck = new WordDeck(Words(8), 1);
            List<string> first = deck.Draw(8);
            foreach (string word in first)
            {
                deck.Consume(word);
            }
            Assert.AreEqual(0, deck.RemainingCount);
            Assert.AreEqual(8, deck.Draw(8).Count);
        }

        [Test]
        public void ThrowsWhenTheListIsSmallerThanTheWheel()
        {
            var deck = new WordDeck(Words(5), 1);
            Assert.Throws<System.InvalidOperationException>(() => deck.Draw(8));
        }

        [Test]
        public void ParseSkipsCommentsBlanksDuplicatesAndLongLines()
        {
            string text = "# header\n\nCat\r\n  dog  \ncat\nCAT\nthis line is far too long to fit\nBird\n";
            List<string> words = WordList.Parse(text);
            CollectionAssert.AreEqual(new[] { "Cat", "dog", "Bird" }, words);
        }

        [Test]
        public void ParseHandlesNullAndEmpty()
        {
            Assert.AreEqual(0, WordList.Parse(null).Count);
            Assert.AreEqual(0, WordList.Parse(string.Empty).Count);
        }

        [Test]
        public void RandomIsDeterministicAcrossPlatforms()
        {
            // Pinned values: if these change, recorded matches no longer replay.
            var random = new TwRandom(1);
            var drawn = new List<int>();
            for (int i = 0; i < 5; i++)
            {
                drawn.Add(random.NextInt(1000));
            }
            var again = new TwRandom(1);
            for (int i = 0; i < 5; i++)
            {
                Assert.AreEqual(drawn[i], again.NextInt(1000));
            }
            Assert.AreEqual(PINNED, string.Join(",", drawn));
        }

        private const string PINNED = "338,925,343,522,279";
    }
}
