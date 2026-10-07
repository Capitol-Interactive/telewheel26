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
    /// <summary>Counts yes/no votes on a final guess. A chain "lands" when yes is a strict majority.</summary>
    public sealed class VoteTally
    {
        private readonly bool[] m_Cast;
        private readonly bool[] m_Yes;
        private int m_YesCount;
        private int m_NoCount;

        public VoteTally(int voters)
        {
            Voters = voters < 1 ? 1 : voters;
            m_Cast = new bool[Voters];
            m_Yes = new bool[Voters];
        }

        public int Voters { get; private set; }

        public int Yes
        {
            get { return m_YesCount; }
        }

        public int No
        {
            get { return m_NoCount; }
        }

        public int CastCount
        {
            get { return m_YesCount + m_NoCount; }
        }

        public bool Complete
        {
            get { return CastCount >= Voters; }
        }

        /// <summary>
        /// Records a vote. A voter can change their mind until the tally is resolved; returns false
        /// for an out-of-range voter.
        /// </summary>
        public bool Cast(int voter, bool yes)
        {
            if (voter < 0 || voter >= Voters)
            {
                return false;
            }
            if (m_Cast[voter])
            {
                if (m_Yes[voter])
                {
                    m_YesCount--;
                }
                else
                {
                    m_NoCount--;
                }
            }
            m_Cast[voter] = true;
            m_Yes[voter] = yes;
            if (yes)
            {
                m_YesCount++;
            }
            else
            {
                m_NoCount++;
            }
            return true;
        }

        /// <summary>Strict majority of all voters (not just those who voted) said yes.</summary>
        public bool Landed
        {
            get { return m_YesCount * 2 > Voters; }
        }
    }
}
