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
        public const string OnlineSoon = "Online play is coming soon.";

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
