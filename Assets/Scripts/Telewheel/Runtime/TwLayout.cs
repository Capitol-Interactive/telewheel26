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

using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// Where things go in the room. Open Brush works in decimetre units (10 per metre), so every
    /// value here is in metres and converted on the way out.
    /// </summary>
    public static class TwLayout
    {
        public const float MetersToUnits = 10f;

        /// <summary>The suggested drawing area on the floor: centre and side length, in metres.</summary>
        public static readonly Vector3 FloorSquareCenterMeters = new Vector3(0f, 0f, 1.4f);
        public const float FloorSquareSideMeters = 1.4f;

        /// <summary>Where the middle of a drawing sits while it is being made, in metres.</summary>
        public static readonly Vector3 DrawingCenterMeters = new Vector3(0f, 1.2f, 1.4f);

        /// <summary>Where the shared reveal stage sits, in metres (between the players).</summary>
        public static readonly Vector3 StageCenterMeters = new Vector3(0f, 1.3f, 2.2f);
        public const float StageSizeMeters = 1.1f;

        public static Vector3 ToUnits(Vector3 meters)
        {
            return meters * MetersToUnits;
        }

        public static float ToUnits(float meters)
        {
            return meters * MetersToUnits;
        }
    }
}
