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
    public enum ContentFilter
    {
        Family,
        Raunchy,
    }

    /// <summary>The rules a host picks before a match, plus timings from the product spec.</summary>
    public sealed class MatchSettings
    {
        public const int MinPlayers = 2;
        public const int MaxPlayers = 8;
        public const int MinRounds = 1;
        public const int MaxRounds = 5;

        public int PlayerCount = 4;
        public int Rounds = 2;
        public ContentFilter Filter = ContentFilter.Family;
        public int Seed = 1;
        public int WheelSegments = 8;

        /// <summary>
        /// One shared yes/no tap for the whole room (Pass & Play). When false every player votes
        /// and a strict majority of all players is needed.
        /// </summary>
        public bool SharedVote = true;

        public float CountdownSeconds = 5f;
        public float DrawSeconds = 60f;
        public float GuessSeconds = 60f;
        public float PresentItemSeconds = 15f;
        public float PresentFinalSeconds = 30f;
        public float VoteResultSeconds = 4f;

        // Online play only (everyone plays at once, so the host needs a few extra timings).
        public float SpinSeconds = 40f;
        public float RoundEndSeconds = 8f;

        /// <summary>Extra time after a turn's clock for the drawing to reach the host.</summary>
        public float TurnGraceSeconds = 3f;

        /// <summary>
        /// How long the host waits for every player to receive a turn's prompt (a drawing can be large)
        /// before starting the clock anyway.
        /// </summary>
        public float TurnLoadSeconds = 30f;

        /// <summary>Optional display names; missing entries fall back to "Player N".</summary>
        public string[] PlayerNames = new string[0];

        public string NameOf(int player)
        {
            if (PlayerNames != null && player >= 0 && player < PlayerNames.Length
                && !string.IsNullOrWhiteSpace(PlayerNames[player]))
            {
                return PlayerNames[player].Trim();
            }
            return TwCopy.DefaultPlayerName(player);
        }

        public int VoterCount
        {
            get { return SharedVote ? 1 : PlayerCount; }
        }

        public void Validate()
        {
            if (PlayerCount < MinPlayers || PlayerCount > MaxPlayers)
            {
                throw new ArgumentException(
                    "PlayerCount must be " + MinPlayers + ".." + MaxPlayers + ", was " + PlayerCount);
            }
            if (Rounds < MinRounds || Rounds > MaxRounds)
            {
                throw new ArgumentException(
                    "Rounds must be " + MinRounds + ".." + MaxRounds + ", was " + Rounds);
            }
            if (WheelSegments < 2)
            {
                throw new ArgumentException("WheelSegments must be at least 2");
            }
            if (DrawSeconds <= 0 || GuessSeconds <= 0 || CountdownSeconds < 0
                || PresentItemSeconds <= 0 || PresentFinalSeconds <= 0 || VoteResultSeconds < 0
                || SpinSeconds <= 0 || RoundEndSeconds < 0 || TurnGraceSeconds < 0 || TurnLoadSeconds <= 0)
            {
                throw new ArgumentException("Durations must be positive");
            }
        }
    }
}
