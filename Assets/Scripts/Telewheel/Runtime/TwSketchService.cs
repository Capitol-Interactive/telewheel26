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

using TiltBrush;
using UnityEngine;

namespace Telewheel
{
    /// <summary>
    /// Saves a finished drawing as bytes, wipes the canvas, and later shows a saved drawing,
    /// display-only, at a spot in the room. Drawings only ever exist as bytes between turns.
    /// </summary>
    public sealed class TwSketchService
    {
        private CanvasScript m_Stage;

        /// <summary>True while a saved drawing is on show.</summary>
        public bool IsShowing
        {
            get { return m_Stage != null; }
        }

        public int StageStrokeCount
        {
            get { return m_Stage == null ? 0 : OpenBrushFacade.StrokeCountOn(m_Stage); }
        }

        /// <summary>The current drawing as bytes (empty for a blank canvas).</summary>
        public byte[] Capture()
        {
            return OpenBrushFacade.SerializeActiveStrokes();
        }

        /// <summary>Removes the drawing, the undo history and any shown stage.</summary>
        public void Clear()
        {
            // Open Brush's reset is heavy (it unloads assets), so skip it when there is nothing to wipe.
            bool needed = m_Stage != null || OpenBrushFacade.ActiveStrokeCount > 0 || OpenBrushFacade.CanUndo;
            m_Stage = null;
            if (needed)
            {
                OpenBrushFacade.ClearEverything();
            }
        }

        /// <summary>
        /// Shows a saved drawing fitted into a box of <paramref name="sizeMeters"/> centred on the
        /// world position <paramref name="worldCenter"/> (in Open Brush units). Replaces whatever
        /// was shown. Returns the stroke count.
        /// </summary>
        public int Show(byte[] drawing, Vector3 worldCenter, float sizeMeters)
        {
            Clear();
            System.Collections.Generic.List<Stroke> strokes = OpenBrushFacade.ReadStrokes(drawing);
            if (strokes == null || strokes.Count == 0)
            {
                return 0;
            }
            return OpenBrushFacade.ShowStrokesOnStage(
                strokes, worldCenter, TwLayout.ToUnits(sizeMeters), out m_Stage);
        }
    }
}
