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
    /// A plain on-screen panel (IMGUI) that shows what the game is doing and the self-test result.
    /// It only appears on a desktop window, which is where this is meant to be tested without a
    /// headset. F9 hides or shows it.
    /// </summary>
    public sealed class TwDebugOverlay : MonoBehaviour
    {
        private const float Width = 620f;

        private bool m_Visible = true;
        private GUIStyle m_Style;
        private GUIStyle m_BoxStyle;

        public bool Visible
        {
            get { return m_Visible; }
            set { m_Visible = value; }
        }

        private void OnGUI()
        {
            TwDirector director = TwDirector.Instance;
            if (!m_Visible || director == null)
            {
                return;
            }
            if (m_Style == null)
            {
                m_Style = new GUIStyle(GUI.skin.label) { fontSize = 14, richText = true, wordWrap = true };
                m_BoxStyle = new GUIStyle(GUI.skin.box);
            }

            GUILayout.BeginArea(new Rect(10f, 10f, Width, Screen.height - 20f), m_BoxStyle);
            GUILayout.Label("<b>TELEWHEEL</b>  <i>F9 hide, F8 run self-test</i>", m_Style);
            foreach (string line in director.OverlayLines())
            {
                GUILayout.Label(line, m_Style);
            }
            GUILayout.EndArea();
        }
    }
}
