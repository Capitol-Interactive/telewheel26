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

namespace Telewheel
{
    /// <summary>
    /// Every player-facing string. Short, verb-first, and never scolding (see the design system's
    /// content fundamentals). Hard-coded English until localization tables are added.
    /// </summary>
    public static class TwCopy
    {
        public const string Title = "TELEWHEEL";

        // Actions.
        public const string Spin = "Spin it.";
        public const string Draw = "Draw it.";
        public const string Guess = "Guess it.";
        public const string Pass = "Pass it.";
        public const string Submit = "SUBMIT";
        public const string Done = "DONE";
        public const string Ready = "READY";
        public const string GotIt = "GOT IT";
        public const string Skip = "SKIP";
        public const string Next = "NEXT";
        public const string Yes = "YES";
        public const string No = "NO";
        public const string Undo = "UNDO";
        public const string Clear = "CLEAR";
        public const string Back = "BACK";

        // Main menu.
        public const string PassAndPlay = "PASS & PLAY";
        public const string PlayOnline = "PLAY ONLINE";
        public const string Settings = "SETTINGS";
        public const string Players = "PLAYERS";
        public const string Rounds = "ROUNDS";
        public const string Start = "START";
        public const string FamilyFriendly = "FAMILY FRIENDLY";
        public const string Raunchy = "RAUNCHY 18+";

        // Settings.
        public const string MixedReality = "MIXED REALITY";
        public const string LeftHanded = "LEFT HANDED";
        public const string RightHanded = "RIGHT HANDED";
        public const string Controllers = "CONTROLLERS";
        public const string Hands = "HANDS";
        public const string HandsSoon = "Hand tracking is coming soon.";
        public const string ExitMatch = "EXIT MATCH";
        public const string Photo = "PHOTO";
        public const string Invite = "INVITE";
        public const string OnlineFriends = "ONLINE FRIENDS";
        public const string On = "ON";
        public const string Off = "OFF";

        // Turn hints.
        public const string KeepItSecret = "Keep your word secret.";
        public const string YourWord = "YOUR WORD";
        public const string WhatIsThis = "WHAT IS THIS?";
        public const string TypeYourGuess = "Type your guess";
        public const string DrawHint = "Draw it in 3D. The floor square is a suggestion.";
        public const string GuessHint = "Guess what the last player drew.";
        public const string GetReady = "GET READY";
        public const string NoGuess = "...";
        public const string NoDrawing = "(blank)";

        // Presenting.
        public const string ItDrifted = "IT DRIFTED!";
        public const string NailedIt = "NAILED IT!";
        public const string DidItLand = "Did it land?";
        public const string DriftedLine = "Wow. That drifted.";
        public const string PointLine = "Nice one. +1 point.";
        public const string RoundOver = "ROUND OVER";
        public const string GameOver = "GAME OVER";
        public const string PlayAgainNew = "PLAY AGAIN (NEW ARTISTS)";
        public const string PlayAgainSame = "PLAY AGAIN (SAME ARTISTS)";
        public const string MainMenu = "MAIN MENU";

        // Online.
        public const string PrivateRoom = "PRIVATE ROOM";
        public const string PublicGame = "PUBLIC GAME";
        public const string JoinWithCode = "JOIN WITH CODE";
        public const string PracticeHost = "PRACTICE: HOST";
        public const string PracticeJoin = "PRACTICE: JOIN";
        public const string ComingSoon = "Coming soon.";
        public const string YourProfile = "YOUR PROFILE";
        public const string PickAnIcon = "PICK AN ICON";
        public const string TypeYourName = "Type your name";
        public const string SetYourName = "Set your name";
        public const string Save = "SAVE";
        public const string EnterRoomCode = "ENTER THE ROOM CODE";
        public const string TypeTheCode = "Type the 4-letter code";
        public const string Join = "JOIN";
        public const string LeaveRoom = "LEAVE ROOM";
        public const string HostTag = "HOST";
        public const string YouTag = "YOU";
        public const string WaitingForHost = "Waiting for the host to start.";
        public const string WaitingForPlayers = "Waiting for more players.";
        public const string Connecting = "Connecting...";
        public const string ResultsComing = "Final results in a moment.";
        public const string NextRoundComing = "Next round in a moment.";
        public const string HereWeGo = "Here we go...";
        public const string PassingDrawings = "Passing the drawings...";
        public const string TestLink = "TEST LINK";
        public const string TestingLink = "Testing the connection...";
        public const string Okay = "OK";
        public const string PracticeNote = "Practice room: the other players are computer players.";
        public const string PracticeRoomTitle = "PRACTICE ROOM";
        public const string OnlineUnavailable = "Online play is unavailable.";
        public const string OnlineNoSdk = "This build has no Photon SDK, so online play is off.";
        public const string OnlineNoFusionId = "The Photon Fusion app id is missing from the Secrets asset.";
        public const string OnlineNoVoiceId = "The Photon Voice app id is missing from the Secrets asset.";
        public const string OnlineNoManager = "Online play is still starting up. Try again in a moment.";
        public const string OnlineError = "Online play hit a problem. Restart the game and try again.";
        public const string OnlineBusy = "Online play is busy. Try again in a moment.";
        public const string NoSuchRoom = "No room with that code. Check the code, and that you are both online.";
        public const string CouldNotConnect = "Couldn't reach the online service. Check your connection and try again.";
        public const string NoFreeCode = "Couldn't find a free room code. Try again.";
        public const string MicOn = "MIC: ON";
        public const string MicOff = "MIC: OFF";
        public const string OthersHeard = "OTHERS: HEARD";
        public const string OthersMuted = "OTHERS: MUTED";
        public const string MixedRealityStuck = "Can't leave mixed reality while other players are in the room.";
        public const string EnvironmentLabel = "ENVIRONMENT";
        public const string DefaultEnvironment = "DEFAULT";

        public static string RoomLabel(string code)
        {
            return "ROOM " + code;
        }

        public static string OwnerMoves(string name)
        {
            return "Waiting for " + name;
        }

        /// <summary>"Waiting for Ann", "Waiting for Ann and Bo", "Waiting for Ann, Bo and 2 more".</summary>
        public static string WaitingFor(System.Collections.Generic.IList<string> names)
        {
            if (names == null || names.Count == 0)
            {
                return HereWeGo;
            }
            if (names.Count == 1)
            {
                return "Waiting for " + names[0];
            }
            if (names.Count == 2)
            {
                return "Waiting for " + names[0] + " and " + names[1];
            }
            if (names.Count == 3)
            {
                return "Waiting for " + names[0] + ", " + names[1] + " and " + names[2];
            }
            return "Waiting for " + names[0] + ", " + names[1] + " and " + (names.Count - 2) + " more";
        }

        public static string PlayerLeftLine(string name)
        {
            return name + " left. The host is playing their turns.";
        }

        public static string RejectedLine(RejectReason reason)
        {
            switch (reason)
            {
                case RejectReason.RoomFull:
                    return "That room is full.";
                case RejectReason.MatchStarted:
                    return "That match has already started.";
                default:
                    return "That room is running a different version of the game.";
            }
        }

        public static string EndedLine(EndReason reason)
        {
            switch (reason)
            {
                case EndReason.NotEnoughPlayers:
                    return "Not enough players are left to carry on.";
                case EndReason.ConnectionLost:
                    return "You lost your connection to the room.";
                default:
                    return "The host left, so the match ended.";
            }
        }

        public static string PassTo(string playerName)
        {
            return "Pass it to " + playerName + ".";
        }

        public static string DefaultPlayerName(int index)
        {
            return "Player " + (index + 1);
        }

        public static string RoundLabel(int round, int rounds)
        {
            return "ROUND " + round + " OF " + rounds;
        }

        public static string TurnLabel(int turn, int turns)
        {
            return "TURN " + (turn + 1) + " OF " + turns;
        }

        public static string WinnerLine(string names)
        {
            return names + " wins!";
        }

        public static string OwnerLine(string name)
        {
            return name + "'s word";
        }

        public static string DrawnBy(string name)
        {
            return "Drawn by " + name;
        }

        public static string GuessedBy(string name)
        {
            return "Guessed by " + name;
        }
    }
}
