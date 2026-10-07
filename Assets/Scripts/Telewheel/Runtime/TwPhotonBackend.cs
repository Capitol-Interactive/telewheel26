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

        /// <summary>A guest alone in the room this long, with no host, has typed a code nobody made.</summary>
        private const float EmptyRoomSeconds = 3f;

        /// <summary>How long a new room is watched for earlier arrivals before it counts as ours alone.</summary>
        private const float SettleSeconds = 1f;

        private const int CodeAttempts = 3;

        // Each match that takes over Open Brush's multiplayer gets a number, so that the tidy-up of one
        // match (which finishes in the background) cannot undo the set-up of the next.
        private static int s_Generation;

        // Leaving a Photon room takes a moment and nothing waits for it, except the next match.
        private static Task s_Leaving = Task.CompletedTask;

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
                StartLeaving(s_Generation);
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
            int generation = s_Generation;

            var random = new TwRandom(System.Environment.TickCount);
            for (int attempt = 0; attempt < CodeAttempts; attempt++)
            {
                string code = RoomCode.Generate(random);
                bool joined = await OpenBrushFacade.JoinMultiplayerRoom(code);
                if (!joined)
                {
                    Debug.LogWarning("[Telewheel] Could not make room " + code + ": " + OpenBrushFacade.MultiplayerError);
                    StartLeaving(generation);
                    failed(TwCopy.CouldNotConnect);
                    return;
                }
                bool alone = await RoomStaysEmptyAsync();
                if (!OpenBrushFacade.InMultiplayerRoom)
                {
                    StartLeaving(generation);
                    failed(TwCopy.CouldNotConnect);
                    return;
                }
                if (alone)
                {
                    ready(BuildHostSession(profile, settings, code, generation));
                    return;
                }
                // Somebody is already in a room with that name: joining one by name makes it if there
                // is none, so a taken code looks just like a free one. Try another.
                if (!await OpenBrushFacade.LeaveMultiplayerRoom())
                {
                    StartLeaving(generation);
                    failed(TwCopy.CouldNotConnect);
                    return;
                }
            }
            StartLeaving(generation);
            failed(TwCopy.NoFreeCode);
        }

        // Newcomers show up a moment after the room is joined, so look for a second before deciding.
        private static async Task<bool> RoomStaysEmptyAsync()
        {
            float waited = 0f;
            while (waited < SettleSeconds && OpenBrushFacade.InMultiplayerRoom)
            {
                if (OpenBrushFacade.OtherPlayersInRoom > 0)
                {
                    return false;
                }
                await Task.Yield();
                waited += Time.unscaledDeltaTime;
            }
            return OpenBrushFacade.OtherPlayersInRoom == 0;
        }

        private static OnlineSession BuildHostSession(PlayerProfile profile, MatchSettings settings, string code, int generation)
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
            session.AddCleanup(() => TearDown(watch, link, wire, generation));
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
            int generation = s_Generation;

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
                StartLeaving(generation);
                failed(TwCopy.CouldNotConnect);
                return;
            }

            float waited = 0f;
            float alone = 0f;
            while (!hostFound && waited < HostWaitSeconds && alone < EmptyRoomSeconds && OpenBrushFacade.InMultiplayerRoom)
            {
                link.Poll();
                await Task.Yield();
                float dt = Time.unscaledDeltaTime;
                waited += dt;
                alone = OpenBrushFacade.OtherPlayersInRoom == 0 ? alone + dt : 0f;
            }
            if (!hostFound || !OpenBrushFacade.InMultiplayerRoom)
            {
                // Joining a name makes the room if it is not there, so a mistyped code gets here. (Read
                // whether we are still in a room before leaving it, or the answer is always no.)
                bool stillInRoom = OpenBrushFacade.InMultiplayerRoom;
                Discard(link, wire, client);
                StartLeaving(generation);
                failed(stillInRoom ? TwCopy.NoSuchRoom : TwCopy.CouldNotConnect);
                return;
            }

            var session = new OnlineSession(client, null, code);
            session.AddTicker(dt =>
            {
                link.Poll();
                wire.Tick(dt);
            });
            IDisposable watch = OpenBrushFacade.WatchRoomConnection(client.ConnectionLost);
            session.AddCleanup(() => TearDown(watch, link, wire, generation));
            ready(session);
        }

        // ----- Shared steps -----

        // Sets Open Brush up for a match and gets to the lobby. Returns a message if it cannot.
        private static async Task<string> PrepareAsync(PlayerProfile profile)
        {
            // The last match may still be on its way out of the room.
            await s_Leaving;

            string problem = TwOnlineAvailability.MissingPiece();
            if (problem != null)
            {
                return problem;
            }
            MultiplayerStage stage = OpenBrushFacade.MultiplayerStage;
            if (stage == MultiplayerStage.Busy)
            {
                return TwCopy.OnlineBusy;
            }
            s_Generation++;
            OpenBrushFacade.BeginMatchMultiplayer(
                profile, TwPrefs.VoiceEnabled, OpenBrushFacade.Settings.Region);

            if (stage == MultiplayerStage.NeedsReset)
            {
                // An earlier connection failed and Open Brush stays in its error state until told otherwise.
                Debug.LogWarning("[Telewheel] Clearing an earlier multiplayer error: " + OpenBrushFacade.MultiplayerError);
                await OpenBrushFacade.ResetMultiplayer();
                stage = OpenBrushFacade.MultiplayerStage;
                if (stage == MultiplayerStage.NeedsReset || stage == MultiplayerStage.Busy)
                {
                    StartLeaving(s_Generation);
                    return TwCopy.CouldNotConnect;
                }
            }
            if (stage == MultiplayerStage.NeedsConnect)
            {
                bool connected = await OpenBrushFacade.ConnectMultiplayer();
                if (!connected)
                {
                    Debug.LogWarning("[Telewheel] Could not connect: " + OpenBrushFacade.MultiplayerError);
                    StartLeaving(s_Generation);
                    return TwCopy.CouldNotConnect;
                }
            }
            return null;
        }

        private static void Discard(OpenBrushFacade.PhotonLink link, WireClient wire, OnlineMatchClient client)
        {
            client.Dispose();
            wire.Dispose();
            link.Dispose();
        }

        // Runs when the session is left, however that happens.
        private static void TearDown(IDisposable watch, OpenBrushFacade.PhotonLink link, IDisposable wire, int generation)
        {
            watch.Dispose();
            wire.Dispose();
            link.Dispose();
            StartLeaving(generation);
        }

        // Leaves whatever room is joined and, if no newer match has started since, puts Open Brush back as
        // it was. Happens in the background; the next match waits for it (see PrepareAsync).
        private static void StartLeaving(int generation)
        {
            s_Leaving = LeaveAsync(s_Leaving, generation);
        }

        private static async Task LeaveAsync(Task previous, int generation)
        {
            try
            {
                await previous;
                if (OpenBrushFacade.InMultiplayerRoom)
                {
                    await OpenBrushFacade.LeaveMultiplayerRoom();
                }
            }
            catch (Exception e)
            {
                Debug.LogException(e);
            }
            if (generation == s_Generation)
            {
                OpenBrushFacade.EndMatchMultiplayer();
            }
        }
    }
}
