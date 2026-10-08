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
    ///
    /// The link does NOT say who sent bytes that arrive: on Photon's Shared mode the sender reported with
    /// received data is always this device itself. Who sent a frame is inside the frame, and is checked
    /// there (see <see cref="Envelope"/>).
    /// </summary>
    public interface IByteLink
    {
        /// <summary>Bytes arrived from some other device. The array belongs to the receiver.</summary>
        event Action<byte[]> Received;

        /// <summary>Another device arrived in the room.</summary>
        event Action<int> PeerJoined;

        /// <summary>Another device left the room (or its connection is gone).</summary>
        event Action<int> PeerLeft;

        /// <summary>This device's own peer id, or -1 until the room has given it one.</summary>
        int LocalPeer { get; }

        /// <summary>True while that device is in the room right now.</summary>
        bool IsPresent(int peer);

        /// <summary>
        /// Sends bytes to a peer. Returns false if the transport cannot take them at the moment (not
        /// in the room yet, say); the caller keeps them and tries again.
        /// </summary>
        bool Send(int peer, byte[] bytes);
    }
}
