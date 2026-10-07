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
    /// The raw bytes between this device and the others in a room, with nothing else: a transport
    /// (Photon, a test network) implements this, and <see cref="WireHost"/> and <see cref="WireClient"/>
    /// build the Telewheel connection on top. Peer ids are the transport's own.
    /// </summary>
    public interface IByteLink
    {
        /// <summary>Bytes arrived from a peer. The array belongs to the receiver.</summary>
        event Action<int, byte[]> Received;

        /// <summary>Another device arrived in the room.</summary>
        event Action<int> PeerJoined;

        /// <summary>Another device left the room (or its connection is gone).</summary>
        event Action<int> PeerLeft;

        void Send(int peer, byte[] bytes);
    }
}
