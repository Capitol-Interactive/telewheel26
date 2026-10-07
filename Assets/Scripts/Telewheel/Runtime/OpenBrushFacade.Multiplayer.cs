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
using System.Threading.Tasks;
using OpenBrush.Multiplayer;
using TiltBrush;
using UnityEngine;

namespace Telewheel
{
    /// <summary>Where Open Brush's multiplayer connection stands, as far as starting a Telewheel room goes.</summary>
    public enum MultiplayerStage
    {
        /// <summary>Not connected to the online service yet; <see cref="OpenBrushFacade.ConnectMultiplayer"/> comes first.</summary>
        NeedsConnect,

        /// <summary>Connected, in the lobby, ready to join a room.</summary>
        InLobby,

        /// <summary>Connecting, joining, leaving or in a room: not now.</summary>
        Busy,

        /// <summary>An earlier connection failed; <see cref="OpenBrushFacade.ResetMultiplayer"/> clears it and then a connect can be tried.</summary>
        NeedsReset,
    }

    /// <summary>
    /// The part of the facade that reaches Open Brush's multiplayer code (Photon). A Telewheel match
    /// uses an Open Brush room only as a pipe for its own messages: the sketch is not shared, so these
    /// calls switch sharing off, set who the player is, and join and leave the room.
    /// </summary>
    public static partial class OpenBrushFacade
    {
        private static MultiplayerManager Manager
        {
            get { return MultiplayerManager.m_Instance; }
        }

        public static bool MultiplayerExists
        {
            get { return Manager != null; }
        }

        public static string MultiplayerError
        {
            get { return Manager == null ? null : Manager.LastError; }
        }

        public static bool InMultiplayerRoom
        {
            get { return Manager != null && Manager.State == ConnectionState.IN_ROOM; }
        }

        /// <summary>True when Open Brush's multiplayer could not even start (no Photon, no secrets): nothing to retry.</summary>
        public static bool MultiplayerBroken
        {
            get { return Manager == null || !Manager.HasConnectionHandler; }
        }

        public static MultiplayerStage MultiplayerStage
        {
            get
            {
                if (MultiplayerBroken)
                {
                    return MultiplayerStage.Busy;
                }
                switch (Manager.State)
                {
                    case ConnectionState.INITIALIZED:
                    case ConnectionState.DISCONNECTED:
                        return MultiplayerStage.NeedsConnect;
                    case ConnectionState.IN_LOBBY:
                        return MultiplayerStage.InLobby;
                    case ConnectionState.ERROR:
                        return MultiplayerStage.NeedsReset;
                    default:
                        return MultiplayerStage.Busy;
                }
            }
        }

        /// <summary>
        /// Clears a failed connection (the manager stays in ERROR until it is disconnected) so that a
        /// new one can be tried. True when it worked.
        /// </summary>
        public static Task<bool> ResetMultiplayer()
        {
            return Manager.Disconnect();
        }

        /// <summary>
        /// Gets Open Brush's multiplayer ready to carry a Telewheel match: no stroke sharing, voice
        /// that may fail without stopping the game, the player's name and icon, and the region.
        /// Call before connecting; undo with <see cref="EndMatchMultiplayer"/>.
        /// </summary>
        public static void BeginMatchMultiplayer(PlayerProfile profile, bool microphone, string region)
        {
            MultiplayerManager manager = Manager;
            ConnectionUserInfo info = manager.UserInfo;
            manager.UserInfo = new ConnectionUserInfo
            {
                UserId = info.UserId,
                Role = info.Role,
                Nickname = string.IsNullOrEmpty(profile.Name) ? "Player" : profile.Name,
            };
            manager.AvatarIconIndex = profile.Icon;
            MultiplayerManager.AvatarIconColor = icon => TwGfx.ToColor(TwTokens.PlayerColors[PlayerProfile.CleanIcon(icon)]);
            manager.Region = region ?? string.Empty;
            manager.SuppressCommandSharing = true;
            manager.VoiceIsOptional = true;
            manager.StartMicrophoneOnJoin = microphone;
        }

        /// <summary>Puts Open Brush's multiplayer back to how it behaves outside a Telewheel match.</summary>
        public static void EndMatchMultiplayer()
        {
            MultiplayerManager manager = Manager;
            if (manager == null)
            {
                return;
            }
            SetRemoteAvatarsVisible(true);
            manager.SuppressCommandSharing = false;
            manager.VoiceIsOptional = false;
            manager.StartMicrophoneOnJoin = true;
            manager.AvatarIconIndex = -1;
            manager.Region = string.Empty;
        }

        public static Task<bool> ConnectMultiplayer()
        {
            return Manager.Connect();
        }

        /// <summary>Joins the room called <paramref name="code"/>, making it if there is none. Private: not in any room list.</summary>
        public static Task<bool> JoinMultiplayerRoom(string code)
        {
            return Manager.JoinRoom(new RoomCreateData
            {
                roomName = code,
                @private = true,
                maxPlayers = MatchSettings.MaxPlayers,
                silentRoom = false,
                viewOnlyRoom = false,
            });
        }

        public static Task<bool> LeaveMultiplayerRoom()
        {
            return Manager.LeaveRoom();
        }

        public static int OtherPlayersInRoom
        {
            get { return Manager == null ? 0 : Manager.GetRemotePlayerIds().Count; }
        }

        public static void CloseRoomToNewcomers()
        {
            if (Manager != null)
            {
                Manager.SetRoomOpen(false);
            }
        }

        // ----- Voice and avatars -----

        public static void SetMicrophone(bool on)
        {
            if (Manager == null)
            {
                return;
            }
            if (on)
            {
                Manager.StartSpeaking();
            }
            else
            {
                Manager.StopSpeaking();
            }
        }

        /// <summary>
        /// Mutes or unmutes everyone else for this player only. Call again every so often: a player's voice
        /// can arrive after their avatar, and a mute that found no voice yet has to be applied again, so
        /// this does not skip players already marked muted.
        /// </summary>
        public static void MuteOtherPlayers(bool muted)
        {
            if (Manager == null || Manager.m_RemotePlayers == null)
            {
                return;
            }
            foreach (RemotePlayer player in Manager.m_RemotePlayers.List)
            {
                Manager.MutePlayerForMe(muted, player.PlayerId);
            }
        }

        public static void SetRemoteAvatarsVisible(bool visible)
        {
            if (Manager != null)
            {
                Manager.SetRemoteAvatarsVisible(visible);
            }
        }

        // Open Brush's own desktop join form (Escape key) would let a player into a room outside the game.
        private static void DisableOpenBrushMultiplayerUi()
        {
            NonVrMultiplayerUi form = UnityEngine.Object.FindFirstObjectByType<NonVrMultiplayerUi>(FindObjectsInactive.Include);
            if (form != null)
            {
                form.enabled = false;
            }
        }

        // ----- Carrying bytes -----

        /// <summary>The room as a byte pipe, for <see cref="WireHost"/> and <see cref="WireClient"/>.</summary>
        public static PhotonLink CreatePhotonLink()
        {
            return new PhotonLink(Manager);
        }

        /// <summary>Calls <paramref name="onLost"/> once if the room connection drops while it is being watched.</summary>
        public static IDisposable WatchRoomConnection(Action onLost)
        {
            return new RoomWatch(Manager, onLost);
        }

        private sealed class RoomWatch : IDisposable
        {
            private readonly MultiplayerManager m_Manager;
            private Action m_OnLost;

            public RoomWatch(MultiplayerManager manager, Action onLost)
            {
                m_Manager = manager;
                m_OnLost = onLost;
                m_Manager.StateUpdated += OnState;
            }

            public void Dispose()
            {
                m_Manager.StateUpdated -= OnState;
                m_OnLost = null;
            }

            private void OnState(ConnectionState state)
            {
                // Anything other than being in (or still joining) the room means the connection is gone.
                if (state == ConnectionState.IN_ROOM || state == ConnectionState.JOINING_ROOM)
                {
                    return;
                }
                Action lost = m_OnLost;
                m_OnLost = null;
                if (lost != null)
                {
                    lost();
                }
            }
        }

        /// <summary>
        /// Open Brush's room as an <see cref="IByteLink"/>. It forwards game messages and reports who comes
        /// and goes; call <see cref="Poll"/> every frame to notice arrivals and departures. Photon's
        /// Shared mode does not say who sent a message (see <see cref="IByteLink"/>), so it does not.
        /// </summary>
        public sealed class PhotonLink : IByteLink, IDisposable
        {
            private readonly MultiplayerManager m_Manager;
            private readonly HashSet<int> m_Present = new HashSet<int>(); // In the room at the last Poll.
            private readonly HashSet<int> m_Known = new HashSet<int>();   // Reported as arrived, not yet as gone.
            private readonly HashSet<int> m_Gone = new HashSet<int>();
            private readonly List<int> m_Missing = new List<int>();

            public PhotonLink(MultiplayerManager manager)
            {
                m_Manager = manager;
                m_Manager.customDataReceived += OnData;
                m_Manager.playerLeft += OnLeft;
            }

            public event Action<byte[]> Received;

            public event Action<int> PeerJoined;

            public event Action<int> PeerLeft;

            public int LocalPeer
            {
                get { return m_Manager.LocalNetworkPlayerId; }
            }

            public bool IsPresent(int peer)
            {
                return m_Present.Contains(peer);
            }

            public bool Send(int peer, byte[] bytes)
            {
                return m_Manager.SendCustomData(peer, bytes);
            }

            public void Poll()
            {
                IList<int> current = m_Manager.GetRemotePlayerIds();
                m_Present.Clear();
                foreach (int id in current)
                {
                    m_Present.Add(id);
                }
                foreach (int id in current)
                {
                    if (m_Known.Add(id))
                    {
                        m_Gone.Remove(id);
                        Action<int> joined = PeerJoined;
                        if (joined != null)
                        {
                            joined(id);
                        }
                    }
                }
                // Anyone we knew who is no longer in the room has left (the leave event may have been missed).
                m_Missing.Clear();
                foreach (int id in m_Known)
                {
                    if (!m_Present.Contains(id))
                    {
                        m_Missing.Add(id);
                    }
                }
                foreach (int id in m_Missing)
                {
                    OnLeft(id);
                }
            }

            public void Dispose()
            {
                m_Manager.customDataReceived -= OnData;
                m_Manager.playerLeft -= OnLeft;
            }

            private void OnData(byte[] bytes)
            {
                Action<byte[]> handler = Received;
                if (handler != null)
                {
                    handler(bytes);
                }
            }

            private void OnLeft(int id)
            {
                if (id == LocalPeer)
                {
                    return; // That is us leaving; the room watch deals with it.
                }
                m_Present.Remove(id);
                m_Known.Remove(id);
                if (m_Gone.Add(id))
                {
                    Action<int> left = PeerLeft;
                    if (left != null)
                    {
                        left(id);
                    }
                }
            }
        }
    }
}
