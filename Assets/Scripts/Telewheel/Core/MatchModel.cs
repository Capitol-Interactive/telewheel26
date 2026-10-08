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

namespace Telewheel
{
    /// <summary>One Draw or Guess turn that has been played on a chain.</summary>
    public sealed class ChainEntry
    {
        public int Player;
        public StageKind Kind;

        /// <summary>Serialized 3D drawing (Draw turns). Empty when the player drew nothing.</summary>
        public byte[] Drawing;

        /// <summary>The typed guess (Guess turns).</summary>
        public string Text;
    }

    /// <summary>One word's trip around the table.</summary>
    public sealed class ChainState
    {
        public int Owner;
        public string Word;
        public readonly List<ChainEntry> Entries = new List<ChainEntry>();

        /// <summary>Set once the vote on the final guess has resolved.</summary>
        public bool VoteResolved;
        public bool Landed;
    }

    public enum PresentItemKind
    {
        Word,
        Drawing,
        Guess,
    }

    /// <summary>One thing shown in the reveal: the spun word, a drawing, or a guess.</summary>
    public sealed class PresentItem
    {
        public PresentItemKind Kind;
        public int Player;
        public string Text;
        public byte[] Drawing;
    }
}
