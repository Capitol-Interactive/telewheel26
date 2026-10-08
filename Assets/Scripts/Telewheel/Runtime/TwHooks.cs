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
using TiltBrush;

namespace Telewheel
{
    /// <summary>
    /// The few places where Open Brush asks Telewheel what to do. SketchControlsScript calls these
    /// at the top of IssueGlobalCommand and IsCommandAvailable; they do nothing until the game
    /// registers a handler, so plain Open Brush is unaffected.
    /// </summary>
    public static class TwHooks
    {
        /// <summary>Returns true when the game handled the command and Open Brush should skip it.</summary>
        public static Func<SketchControlsScript.GlobalCommands, bool> CommandHandler;

        /// <summary>Returns whether a repurposed command is available, or null to leave it to Open Brush.</summary>
        public static Func<SketchControlsScript.GlobalCommands, bool?> CommandAvailability;

        public static bool TryHandleGlobalCommand(SketchControlsScript.GlobalCommands command)
        {
            Func<SketchControlsScript.GlobalCommands, bool> handler = CommandHandler;
            return handler != null && handler(command);
        }

        public static bool TryGetCommandAvailable(
            SketchControlsScript.GlobalCommands command, out bool available)
        {
            Func<SketchControlsScript.GlobalCommands, bool?> provider = CommandAvailability;
            bool? result = provider != null ? provider(command) : null;
            available = result ?? false;
            return result.HasValue;
        }

        public static void Reset()
        {
            CommandHandler = null;
            CommandAvailability = null;
        }
    }
}
