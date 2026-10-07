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
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// Something that can host or join an online room. The game only knows this interface: the
    /// practice room (computer players, no network) and, once it is set up, the Photon transport.
    /// Both hand back an <see cref="OnlineSession"/>.
    /// </summary>
    public interface ITwOnlineBackend
    {
        /// <summary>Null when online play works; otherwise why it does not, in words for a player.</summary>
        string UnavailableReason { get; }

        void Host(PlayerProfile profile, MatchSettings settings, Action<OnlineSession> ready, Action<string> failed);

        void Join(string code, PlayerProfile profile, Action<OnlineSession> ready, Action<string> failed);
    }

    /// <summary>Stands in when online play cannot work, so the menu can say so instead of hanging.</summary>
    public sealed class TwUnavailableBackend : ITwOnlineBackend
    {
        private readonly string m_Reason;

        public TwUnavailableBackend(string reason)
        {
            m_Reason = reason;
        }

        public string UnavailableReason
        {
            get { return m_Reason; }
        }

        public void Host(PlayerProfile profile, MatchSettings settings, Action<OnlineSession> ready, Action<string> failed)
        {
            failed(m_Reason);
        }

        public void Join(string code, PlayerProfile profile, Action<OnlineSession> ready, Action<string> failed)
        {
            failed(m_Reason);
        }
    }

    /// <summary>A local room full of computer players, for trying the online screens with no network.</summary>
    public sealed class TwPracticeBackend : ITwOnlineBackend
    {
        private const int Bots = 3;

        private readonly Func<byte[]> m_Drawing;

        /// <param name="drawing">What the computer players hand in as their drawing (a person's last one, so they have something to copy).</param>
        public TwPracticeBackend(Func<byte[]> drawing)
        {
            m_Drawing = drawing;
        }

        public string UnavailableReason
        {
            get { return null; }
        }

        public void Host(PlayerProfile profile, MatchSettings settings, Action<OnlineSession> ready, Action<string> failed)
        {
            ready(PracticeRoom.Host(profile, settings, TwWords.Load, Bots, m_Drawing, settings.Seed));
        }

        public void Join(string code, PlayerProfile profile, Action<OnlineSession> ready, Action<string> failed)
        {
            MatchSettings settings = TwGame.DefaultSettings();
            ready(PracticeRoom.Join(profile, code, settings, TwWords.Load, Bots, m_Drawing, settings.Seed));
        }
    }

    /// <summary>Which online backend the game uses.</summary>
    public static class TwOnline
    {
        private static ITwOnlineBackend s_Backend;

        // Domain reload is off in this project, so forget the backend when Play starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Backend = null;
        }

        /// <summary>The real backend. Until one is set (the Photon transport), online play says it is unavailable.</summary>
        public static ITwOnlineBackend Backend
        {
            get
            {
                return s_Backend ?? new TwUnavailableBackend(
                    "Online play is not set up in this build. The Photon app ids are missing.");
            }
            set { s_Backend = value; }
        }

        /// <summary>The practice buttons show in the editor, in development builds and with --Telewheel.FakeOnline.</summary>
        public static bool PracticeVisible
        {
            get
            {
                return Application.isEditor || Debug.isDebugBuild
                    || (OpenBrushFacade.ConfigLoaded && OpenBrushFacade.Settings.FakeOnline);
            }
        }
    }
}
