// Mirrors, line for line, the Fusion calls made by Telewheel's edits to
// Assets/Scripts/Multiplayer/Photon/PhotonManager.cs and PhotonRPC.cs. If this file stops compiling after
// a Fusion upgrade, those edits need the same change.
using System;
using System.Collections.Generic;
using Fusion;
using Fusion.Photon.Realtime;
using Fusion.Sockets;

namespace Telewheel.PhotonCheck
{
    public static class PhotonSignatures
    {
        // A game message to one player: PhotonManager.SendCustomData.
        public static void SendCustom(NetworkRunner runner, int playerId, byte[] data)
        {
            bool shutdown = runner.IsShutdown;
            var key = ReliableKey.FromInts(0x54574C31, 1, 0, -7);
            runner.SendReliableDataToPlayer(PlayerRef.FromEncoded(playerId), key, data);
            if (shutdown)
            {
                return;
            }
        }

        // Telling a game message from stroke sync: PhotonManager.OnReliableDataReceived.
        public static bool IsCustom(ReliableKey key, PlayerRef player, ReadOnlySpan<byte> data, out int from, out byte[] copy)
        {
            int keyMagic;
            int percentage;
            key.GetInts(out keyMagic, out _, out _, out percentage);
            from = player.RawEncoded;
            copy = data.IsEmpty ? null : data.ToArray();
            return percentage == -7 && keyMagic == 0x54574C31 && data.Length <= 16 * 1024 * 1024;
        }

        // Everyone else in the room: PhotonManager.GetRemotePlayerIds.
        public static IList<int> RemoteIds(NetworkRunner runner)
        {
            var ids = new List<int>();
            foreach (PlayerRef player in runner.ActivePlayers)
            {
                if (player != runner.LocalPlayer)
                {
                    ids.Add(player.RawEncoded);
                }
            }
            return ids;
        }

        // Closing the room to newcomers: PhotonManager.SetRoomOpen.
        public static void SetOpen(NetworkRunner runner, bool open)
        {
            runner.SessionInfo.IsOpen = open;
        }

        // Private rooms: PhotonManager.JoinRoom builds these StartGameArgs.
        public static StartGameArgs Args(string name, bool isPrivate, int? players, FusionAppSettings settings)
        {
            return new StartGameArgs
            {
                GameMode = GameMode.Shared,
                SessionName = name,
                CustomPhotonAppSettings = settings,
                PlayerCount = players,
                IsVisible = !isPrivate,
            };
        }

        // Not checked here: PhotonManager.Connect and JoinRoom assign FixedRegion on the app settings. That
        // member comes from Photon.Realtime.AppSettings, which Unity builds from source and this project does
        // not; the original code already sets it in an object initializer, so a plain assignment is the same.

        // The room list: PhotonManager.OnSessionListUpdated.
        public static bool Listed(SessionInfo session)
        {
            return !session.IsVisible && session.IsOpen && session.PlayerCount > 0 && session.MaxPlayers > 0;
        }

        // Who sent an RPC: PhotonRPC.SenderIsRoomOwner reads info.Source.RawEncoded.
        public static int Sender(RpcInfo info)
        {
            return info.Source.RawEncoded;
        }

        // Disconnecting: PhotonManager.Disconnect keeps these locals across the shutdown.
        public static bool Shutdown(NetworkRunner runner, out string userId)
        {
            userId = runner.UserId;
            return runner.IsShutdown;
        }
    }
}
