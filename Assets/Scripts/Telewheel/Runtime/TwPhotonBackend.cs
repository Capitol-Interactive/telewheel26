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
using System.Threading.Tasks;
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// Plays Telewheel over Photon, through Open Brush's multiplayer room. Open Brush's room is only a
    /// pipe: the match itself is Telewheel's own (see <see cref="OnlineRoomHost"/>), carried as plain
    /// messages by <see cref="OpenBrushFacade.PhotonLink"/> wrapped in <see cref="WireHost"/> or
    /// <see cref="WireClient"/>. The host is whoever made the room; the room code is its name.
    /// </summary>
    public sealed class TwPhotonBackend : ITwOnlineBackend
    {
        /// <summary>How long a guest waits for the host to answer before deciding there is no such room.</summary>
        private const float HostWaitSeconds = 8f;

        private const int CodeAttempts = 3;

        public string UnavailableReason
        {
            get { return TwOnlineAvailability.MissingPiece(); }
        }

        public void Host(PlayerProfile profile, MatchSettings settings, Action<OnlineSession> ready, Action<string> failed)
        {
            Run(HostAsync(profile, settings, ready, failed), failed);
        }

        public void Join(string code, PlayerProfile profile, Action<OnlineSession> ready, Action<string> failed)
        {
            Run(JoinAsync(code, profile, ready, failed), failed);
        }

        // Nothing here may escape as an unobserved exception: whatever goes wrong becomes a message.
        private static async void Run(Task task, Action<string> failed)
        {
            try
            {
                await task;
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                OpenBrushFacade.EndMatchMultiplayer();
                failed(TwCopy.CouldNotConnect);
            }
        }

        // ----- Hosting -----

        private static async Task HostAsync(
            PlayerProfile profile, MatchSettings settings, Action<OnlineSession> ready, Action<string> failed)
        {
            string problem = await PrepareAsync(profile);
            if (problem != null)
            {
                failed(problem);
                return;
            }

            var random = new TwRandom(System.Environment.TickCount);
            for (int attempt = 0; attempt < CodeAttempts; attempt++)
            {
                string code = RoomCode.Generate(random);
                bool joined = await OpenBrushFacade.JoinMultiplayerRoom(code);
                if (!joined)
                {
                    Debug.LogWarning("[Telewheel] Could not make room " + code + ": " + OpenBrushFacade.MultiplayerError);
                    await Abandon();
                    failed(TwCopy.CouldNotConnect);
                    return;
                }
                if (OpenBrushFacade.OtherPlayersInRoom == 0)
                {
                    ready(BuildHostSession(profile, settings, code));
                    return;
                }
                // Somebody is already in a room with that name: joining one by name makes it if there
                // is none, so a taken code looks just like a free one. Try another.
                await OpenBrushFacade.LeaveMultiplayerRoom();
            }
            await Abandon();
            failed(TwCopy.NoFreeCode);
        }

        private static OnlineSession BuildHostSession(PlayerProfile profile, MatchSettings settings, string code)
        {
            OpenBrushFacade.PhotonLink link = OpenBrushFacade.CreatePhotonLink();
            var wire = new WireHost(link);
            var room = new OnlineRoomHost(wire, settings, profile, TwWords.Load);
            var client = new OnlineMatchClient(room.LocalPort, profile);
            var session = new OnlineSession(client, room, code);
            session.AddTicker(dt =>
            {
                link.Poll();
                wire.Tick(dt);
            });
            // Late arrivals are turned away by the game anyway; this stops them even reaching it.
            room.Started += OpenBrushFacade.CloseRoomToNewcomers;
            IDisposable watch = OpenBrushFacade.WatchRoomConnection(() => room.Close(EndReason.ConnectionLost));
            session.AddCleanup(() => TearDown(watch, link, wire));
            client.Join();
            return session;
        }

        // ----- Joining -----

        private static async Task JoinAsync(
            string code, PlayerProfile profile, Action<OnlineSession> ready, Action<string> failed)
        {
            string problem = await PrepareAsync(profile);
            if (problem != null)
            {
                failed(problem);
                return;
            }

            // Listen before joining, so the host's greeting cannot arrive unheard.
            OpenBrushFacade.PhotonLink link = OpenBrushFacade.CreatePhotonLink();
            var wire = new WireClient(link);
            var client = new OnlineMatchClient(wire, profile);
            bool hostFound = false;
            wire.HostFound += peer =>
            {
                hostFound = true;
                client.Join();
            };

            bool joined = await OpenBrushFacade.JoinMultiplayerRoom(code);
            if (!joined)
            {
                Debug.LogWarning("[Telewheel] Could not join room " + code + ": " + OpenBrushFacade.MultiplayerError);
                Discard(link, wire, client);
                await Abandon();
                failed(TwCopy.CouldNotConnect);
                return;
            }

            float waited = 0f;
            while (!hostFound && waited < HostWaitSeconds && OpenBrushFacade.InMultiplayerRoom)
            {
                link.Poll();
                await Task.Yield();
                waited += Time.unscaledDeltaTime;
            }
            if (!hostFound)
            {
                // Joining a name makes the room if it is not there, so a mistyped code gets here.
                Discard(link, wire, client);
                await Abandon();
                failed(OpenBrushFacade.InMultiplayerRoom ? TwCopy.NoSuchRoom : TwCopy.CouldNotConnect);
                return;
            }

            var session = new OnlineSession(client, null, code);
            session.AddTicker(dt => link.Poll());
            IDisposable watch = OpenBrushFacade.WatchRoomConnection(client.ConnectionLost);
            session.AddCleanup(() => TearDown(watch, link, wire));
            ready(session);
        }

        // ----- Shared steps -----

        // Sets Open Brush up for a match and gets to the lobby. Returns a message if it cannot.
        private static async Task<string> PrepareAsync(PlayerProfile profile)
        {
            string problem = TwOnlineAvailability.MissingPiece();
            if (problem != null)
            {
                return problem;
            }
            if (OpenBrushFacade.MultiplayerStage == MultiplayerStage.Busy)
            {
                return TwCopy.OnlineBusy;
            }
            OpenBrushFacade.BeginMatchMultiplayer(
                profile, TwPrefs.VoiceEnabled, OpenBrushFacade.Settings.Region);
            if (OpenBrushFacade.MultiplayerStage == MultiplayerStage.NeedsConnect)
            {
                bool connected = await OpenBrushFacade.ConnectMultiplayer();
                if (!connected)
                {
                    Debug.LogWarning("[Telewheel] Could not connect: " + OpenBrushFacade.MultiplayerError);
                    OpenBrushFacade.EndMatchMultiplayer();
                    return TwCopy.CouldNotConnect;
                }
            }
            return null;
        }

        // Leaves whatever was joined and puts Open Brush back as it was.
        private static async Task Abandon()
        {
            if (OpenBrushFacade.InMultiplayerRoom)
            {
                await OpenBrushFacade.LeaveMultiplayerRoom();
            }
            OpenBrushFacade.EndMatchMultiplayer();
        }

        private static void Discard(OpenBrushFacade.PhotonLink link, WireClient wire, OnlineMatchClient client)
        {
            client.Dispose();
            wire.Dispose();
            link.Dispose();
        }

        // Runs when the session is left, however that happens.
        private static void TearDown(IDisposable watch, OpenBrushFacade.PhotonLink link, IDisposable wire)
        {
            watch.Dispose();
            wire.Dispose();
            link.Dispose();
            // Leaving the Photon room takes a moment and nothing waits for it.
            LeaveInBackground();
        }

        private static async void LeaveInBackground()
        {
            try
            {
                await Abandon();
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
        }
    }
}
