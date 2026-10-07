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

using OpenBrush.Multiplayer;
using TiltBrush;

namespace Telewheel
{
    /// <summary>
    /// Works out whether online play can run, so the menu can say what is missing instead of
    /// hanging on a connection that was never going to happen. Open Brush's multiplayer code
    /// misbehaves without its Photon secrets (a missing voice id loops forever, a missing Fusion id
    /// throws), so Telewheel checks them first.
    /// </summary>
    public static class TwOnlineAvailability
    {
        /// <summary>Null when the SDK, both Photon app ids and Open Brush's multiplayer manager are in place.</summary>
        public static string MissingPiece()
        {
#if MP_PHOTON
            if (App.Config == null)
            {
                return TwCopy.OnlineNoManager;
            }
            if (IsMissing(App.Config.PhotonFusionSecrets))
            {
                return TwCopy.OnlineNoFusionId;
            }
            if (IsMissing(App.Config.PhotonVoiceSecrets))
            {
                return TwCopy.OnlineNoVoiceId;
            }
            MultiplayerManager manager = MultiplayerManager.m_Instance;
            if (manager == null)
            {
                return TwCopy.OnlineNoManager;
            }
            if (manager.State == ConnectionState.ERROR)
            {
                return TwCopy.OnlineError;
            }
            return null;
#else
            return TwCopy.OnlineNoSdk;
#endif
        }

        /// <summary>True when Open Brush's multiplayer manager is idle and can start a connection right now.</summary>
        public static bool CanConnectNow()
        {
            MultiplayerManager manager = MultiplayerManager.m_Instance;
            return manager != null && manager.IsConnectable();
        }

#if MP_PHOTON
        private static bool IsMissing(SecretsConfig.ServiceAuthData data)
        {
            return data == null || string.IsNullOrEmpty(data.ClientId);
        }
#endif
    }
}
