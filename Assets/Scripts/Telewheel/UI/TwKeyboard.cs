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
using UnityEngine;
using UnityEngine.InputSystem;

namespace Telewheel
{
    /// <summary>
    /// A point-and-click QWERTY keyboard for typing a guess in VR, plus physical keyboard input on a
    /// desktop. The typed text lives in the keyboard; it raises <see cref="Changed"/> on every edit
    /// and <see cref="Submitted"/> when the player presses GUESS (or Enter).
    /// </summary>
    public sealed class TwKeyboard
    {
        private static readonly string[] Rows = { "qwertyuiop", "asdfghjkl'", "zxcvbnm-" };

        public readonly GameObject Root;
        public event Action<string> Changed;
        public event Action<string> Submitted;

        private readonly int m_MaxLength;
        private readonly bool m_LettersOnly;
        private string m_Text = string.Empty;
        private bool m_Listening;
        private bool m_PreviousShortcutsDisabled;

        public TwKeyboard(
            Transform parent, Vector3 localPosition, int maxLength = GuessNormalizer.MaxLength, bool lettersOnly = false)
        {
            m_MaxLength = maxLength;
            m_LettersOnly = lettersOnly;
            Root = new GameObject("Keyboard");
            Root.transform.SetParent(parent, false);
            Root.transform.localPosition = localPosition;

            float key = TwUi.Px(58f);
            float pitch = key + TwUi.Px(8f);
            for (int row = 0; row < Rows.Length; row++)
            {
                string keys = Rows[row];
                float x0 = -pitch * (keys.Length - 1) * 0.5f;
                for (int i = 0; i < keys.Length; i++)
                {
                    char c = keys[i];
                    TwButton.Create(Root.transform, c.ToString(), key, key, TwButton.Style.Secondary,
                        new Vector3(x0 + pitch * i, -pitch * row, 0f), () => Append(c));
                }
            }
            // Bottom row: space bar and backspace.
            TwButton.Create(Root.transform, "space", pitch * 5f, key, TwButton.Style.Secondary,
                new Vector3(-pitch * 1.2f, -pitch * 3f, 0f), () => Append(' '));
            TwButton.Create(Root.transform, "delete", pitch * 2.4f, key, TwButton.Style.Secondary,
                new Vector3(pitch * 3.4f, -pitch * 3f, 0f), Backspace);
        }

        public string Text
        {
            get { return m_Text; }
        }

        /// <summary>Replaces what has been typed (for editing a saved value).</summary>
        public void SetText(string text)
        {
            string value = text ?? string.Empty;
            m_Text = value.Length > m_MaxLength ? value.Substring(0, m_MaxLength) : value;
            RaiseChanged();
        }

        public void Clear()
        {
            m_Text = string.Empty;
            RaiseChanged();
        }

        /// <summary>Starts taking input from a physical keyboard and silences Open Brush's shortcuts.</summary>
        public void StartListening()
        {
            if (m_Listening)
            {
                return;
            }
            m_Listening = true;
            m_PreviousShortcutsDisabled = OpenBrushFacade.KeyboardShortcutsDisabled;
            OpenBrushFacade.KeyboardShortcutsDisabled = true;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                keyboard.onTextInput += OnTextInput;
            }
        }

        public void StopListening()
        {
            if (!m_Listening)
            {
                return;
            }
            m_Listening = false;
            Keyboard keyboard = Keyboard.current;
            if (keyboard != null)
            {
                keyboard.onTextInput -= OnTextInput;
            }
            OpenBrushFacade.KeyboardShortcutsDisabled = m_PreviousShortcutsDisabled;
        }

        /// <summary>Press GUESS: submits whatever has been typed.</summary>
        public void Submit()
        {
            Action<string> handler = Submitted;
            if (handler != null)
            {
                handler(m_Text);
            }
        }

        private void OnTextInput(char c)
        {
            if (c == '\b')
            {
                Backspace();
            }
            else if (c == '\r' || c == '\n')
            {
                Submit();
            }
            else if (!char.IsControl(c))
            {
                Append(c);
            }
        }

        private void Append(char c)
        {
            if (m_Text.Length >= m_MaxLength || (m_LettersOnly && !char.IsLetter(c)))
            {
                return;
            }
            m_Text += c;
            RaiseChanged();
        }

        private void Backspace()
        {
            if (m_Text.Length == 0)
            {
                return;
            }
            m_Text = m_Text.Substring(0, m_Text.Length - 1);
            RaiseChanged();
        }

        private void RaiseChanged()
        {
            Action<string> handler = Changed;
            if (handler != null)
            {
                handler(m_Text);
            }
        }
    }
}
