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
    /// A player's connection to the match host. The host's own player has one too (see
    /// <see cref="OnlineRoomHost.LocalPort"/>), so every player is driven the same way. Transports
    /// (Photon, the in-memory test network) implement this and <see cref="INetHostPort"/>; nothing
    /// above them knows how the bytes travel. Messages arrive reliably and in order.
    /// </summary>
    public interface INetClientPort
    {
        /// <summary>A message from the host arrived.</summary>
        event Action<NetMessage> FromHost;

        /// <summary>The connection to the host is gone for good.</summary>
        event Action HostLost;

        void SendToHost(NetMessage message);
    }

    /// <summary>
    /// The host's connections to the other players. A peer id belongs to the transport and stays the
    /// same for the life of one connection; it is not a seat number.
    /// </summary>
    public interface INetHostPort
    {
        /// <summary>A message from a remote player arrived.</summary>
        event Action<int, NetMessage> FromPeer;

        /// <summary>A remote player's connection is gone.</summary>
        event Action<int> PeerLeft;

        void SendToPeer(int peer, NetMessage message);
    }
}
