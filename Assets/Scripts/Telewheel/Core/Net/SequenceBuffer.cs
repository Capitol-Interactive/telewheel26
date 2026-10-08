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
    /// <summary>
    /// Hands one sender's items on in the order they were sent, however they arrive. Items that come
    /// early wait for the ones before them; repeats and old ones are dropped. If a gap never fills (too
    /// many items, or too many bytes, are waiting), it gives up on the gap rather than stall for ever.
    /// </summary>
    public sealed class SequenceBuffer<T>
    {
        private readonly SortedDictionary<uint, T> m_Held = new SortedDictionary<uint, T>();
        private readonly int m_MaxHeld;
        private readonly long m_MaxHeldBytes;
        private readonly Func<T, int> m_SizeOf;
        private long m_HeldBytes;
        private uint m_Next;

        /// <param name="maxHeld">How many early items may wait before the gap is given up on.</param>
        /// <param name="maxHeldBytes">The same, in bytes (needs <paramref name="sizeOf"/>); stops a peer filling memory.</param>
        /// <param name="sizeOf">How big an item is, for the byte limit.</param>
        public SequenceBuffer(int maxHeld = 64, long maxHeldBytes = long.MaxValue, Func<T, int> sizeOf = null)
        {
            m_MaxHeld = maxHeld < 1 ? 1 : maxHeld;
            m_MaxHeldBytes = maxHeldBytes;
            m_SizeOf = sizeOf;
        }

        /// <summary>The sequence number expected next.</summary>
        public uint Next
        {
            get { return m_Next; }
        }

        public int HeldCount
        {
            get { return m_Held.Count; }
        }

        public long HeldBytes
        {
            get { return m_HeldBytes; }
        }

        /// <summary>Takes an item and returns whatever can now be delivered, in order (often empty).</summary>
        public List<T> Accept(uint sequence, T item)
        {
            var ready = new List<T>();
            if (sequence < m_Next || m_Held.ContainsKey(sequence))
            {
                return ready;
            }
            m_Held[sequence] = item;
            m_HeldBytes += SizeOf(item);
            Drain(ready);
            if (m_Held.Count > m_MaxHeld || m_HeldBytes > m_MaxHeldBytes)
            {
                // The missing item is not coming. Carry on from the earliest one we do have.
                foreach (uint first in m_Held.Keys)
                {
                    m_Next = first;
                    break;
                }
                Drain(ready);
            }
            return ready;
        }

        private int SizeOf(T item)
        {
            return m_SizeOf == null ? 0 : m_SizeOf(item);
        }

        private void Drain(List<T> ready)
        {
            T item;
            while (m_Held.TryGetValue(m_Next, out item))
            {
                m_Held.Remove(m_Next);
                m_HeldBytes -= SizeOf(item);
                ready.Add(item);
                m_Next++;
            }
        }
    }
}
