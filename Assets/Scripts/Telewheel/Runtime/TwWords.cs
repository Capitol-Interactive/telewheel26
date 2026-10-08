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

using System.Collections.Generic;
using UnityEngine;

namespace Telewheel
{
    /// <summary>Loads the word lists shipped in Assets/Resources/Telewheel.</summary>
    public static class TwWords
    {
        private static readonly Dictionary<ContentFilter, List<string>> s_Cache =
            new Dictionary<ContentFilter, List<string>>();

        // Domain reload is off in this project, so drop the cache when Play starts.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            s_Cache.Clear();
        }

        public static string ResourceName(ContentFilter filter)
        {
            return filter == ContentFilter.Raunchy
                ? "Telewheel/words_raunchy"
                : "Telewheel/words_family";
        }

        /// <summary>The parsed word list, or an empty list when the resource is missing.</summary>
        public static List<string> Load(ContentFilter filter)
        {
            List<string> words;
            if (s_Cache.TryGetValue(filter, out words))
            {
                return words;
            }
            TextAsset asset = Resources.Load<TextAsset>(ResourceName(filter));
            words = asset == null ? new List<string>() : WordList.Parse(asset.text);
            s_Cache[filter] = words;
            return words;
        }
    }
}
