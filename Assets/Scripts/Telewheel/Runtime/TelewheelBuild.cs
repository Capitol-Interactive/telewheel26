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
    /// How this build of the app was made. The one place that reads the compile symbol, so the rest of
    /// the code asks a question instead of repeating an <c>#if</c>.
    /// </summary>
    public static class TelewheelBuild
    {
        /// <summary>
        /// True for the Telewheel Quest build: the CI's "Android Meta Quest" row, or a local build after
        /// Telewheel > Build > Set Up Quest Build, both of which define <c>USE_QUEST_PACKAGE_NAME</c>. That
        /// build keeps its data in the app's own folder, so it never asks for all-files access.
        /// </summary>
        public const bool IsQuestBuild =
#if USE_QUEST_PACKAGE_NAME
            true;
#else
            false;
#endif
    }
}
