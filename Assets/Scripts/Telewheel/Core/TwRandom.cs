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

namespace Telewheel
{
    /// <summary>
    /// Small deterministic random generator (SplitMix64). System.Random differs between runtimes,
    /// so recorded matches would not replay identically; this one is the same everywhere.
    /// </summary>
    public sealed class TwRandom
    {
        private ulong m_State;

        public TwRandom(int seed)
        {
            m_State = (ulong)(uint)seed * 0x9E3779B97F4A7C15UL + 0x1234567UL;
        }

        public ulong NextUInt64()
        {
            m_State += 0x9E3779B97F4A7C15UL;
            ulong z = m_State;
            z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
            z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
            return z ^ (z >> 31);
        }

        /// <summary>Uniform integer in [0, exclusiveMax).</summary>
        public int NextInt(int exclusiveMax)
        {
            if (exclusiveMax <= 0)
            {
                throw new ArgumentOutOfRangeException("exclusiveMax");
            }
            return (int)(NextUInt64() % (ulong)exclusiveMax);
        }

        /// <summary>Uniform float in [0, 1).</summary>
        public float NextFloat()
        {
            return (NextUInt64() >> 40) / (float)(1UL << 24);
        }

        /// <summary>Uniform float in [min, max).</summary>
        public float NextRange(float min, float max)
        {
            return min + (max - min) * NextFloat();
        }
    }
}
