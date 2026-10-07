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
using TMPro;
using UnityEngine;

namespace Telewheel
{
    /// <summary>"Pass it to X." while the headset changes hands. Nothing secret is on screen.</summary>
    public sealed class TwHandoffScreen : TwScreen
    {
        public TwHandoffScreen(string playerName, string roundLabel, Action onReady)
            : base("Handoff", 1.4f, 0f)
        {
            Backdrop(1.0f, 0.75f);
            Label(roundLabel, 0f, 0.28f);
            TwUi.Text(T, TwCopy.PassTo(playerName).ToUpperInvariant(), TwUi.Px(TwTokens.DisplayLg),
                TwTokens.Sun, TwFonts.Display, new Vector3(0f, 0.1f, 0f), TextAlignmentOptions.Center, 0.9f);
            Body("Hand over the headset, then tap ready.", -0.06f);
            Button(TwCopy.Ready, 0f, -0.23f, 0.5f, 0.12f, TwButton.Style.Primary, onReady);
        }
    }

    /// <summary>The wheel. The player flicks it (or taps SPIN IT), then sees their secret word.</summary>
    public sealed class TwSpinScreen : TwScreen
    {
        private readonly TwWheelView m_Wheel;
        private readonly TwButton m_SpinButton;
        private readonly TwButton m_GotItButton;
        private readonly TwWordCard m_Card;
        private readonly TextMeshPro m_Secret;
        private readonly TwRandom m_Random;
        private readonly Action<int> m_OnStopped;
        private bool m_Done;

        public TwSpinScreen(MatchMachine machine, TwRandom random, Action<int> onStopped, Action onGotIt)
            : base("Spin", 1.5f, 0f)
        {
            m_Random = random;
            m_OnStopped = onStopped;
            Label(TwCopy.RoundLabel(machine.Round, machine.Settings.Rounds)
                + "   " + machine.Settings.NameOf(machine.ActivePlayer), 0f, 0.56f);

            m_Wheel = new TwWheelView(T, new Vector3(0f, 0.04f, 0f), 0.38f, machine.Settings.WheelSegments);
            m_Wheel.Spin.Stopped += OnWheelStopped;
            m_Wheel.Ticked += () => TwAudio.Play(TwSound.Tick, 0.35f);

            m_SpinButton = Button(TwCopy.Spin, 0f, -0.5f, 0.46f, 0.12f, TwButton.Style.Primary, SpinNow);

            m_Card = new TwWordCard(T, new Vector3(0f, 0.04f, -0.01f), 0.7f, 0.3f);
            m_Card.Root.SetActive(false);
            m_Secret = TwUi.Text(T, TwCopy.KeepItSecret, TwUi.Px(TwTokens.Heading), TwTokens.Ink, TwFonts.Bold,
                new Vector3(0f, -0.2f, 0f));
            m_Secret.gameObject.SetActive(false);
            m_GotItButton = Button(TwCopy.GotIt, 0f, -0.5f, 0.46f, 0.12f, TwButton.Style.Primary, onGotIt);
            m_GotItButton.gameObject.SetActive(false);
        }

        public TwWheelView Wheel
        {
            get { return m_Wheel; }
        }

        /// <summary>Flicks the wheel at a random speed (the SPIN IT button, and bots).</summary>
        public void SpinNow()
        {
            if (m_Done || m_Wheel.Spinning)
            {
                return;
            }
            float speed = m_Random.NextRange(WheelPhysics.MinFling + 200f, WheelPhysics.MaxFling);
            m_Wheel.Fling(m_Random.NextInt(2) == 0 ? speed : -speed);
            m_SpinButton.SetInteractable(false);
        }

        /// <summary>Reveals the word the wheel landed on, to the player holding the headset.</summary>
        public void ShowWord(string word)
        {
            m_Done = true;
            m_Card.Set(TwCopy.YourWord, word);
            m_Card.Root.SetActive(true);
            m_Secret.gameObject.SetActive(true);
            m_SpinButton.gameObject.SetActive(false);
            m_GotItButton.gameObject.SetActive(true);
        }

        public override void Tick(float dt)
        {
            m_Wheel.Tick(dt);
            if (m_Done)
            {
                return;
            }
            Ray ray;
            if (OpenBrushFacade.TryGetPointerRay(out ray))
            {
                bool grabbing = m_Wheel.UpdateGrab(
                    ray, OpenBrushFacade.PrimaryPressedThisFrame, OpenBrushFacade.PrimaryHeld, dt);
                if (grabbing)
                {
                    m_SpinButton.SetInteractable(false);
                }
            }
        }

        private void OnWheelStopped(int segment)
        {
            if (m_Done)
            {
                return;
            }
            m_Wheel.HighlightWinner(segment);
            Action<int> handler = m_OnStopped;
            if (handler != null)
            {
                handler(segment);
            }
        }
    }

    /// <summary>
    /// The heads-up display while drawing: the word, the clock and a Submit button. The drawing
    /// itself happens in the open space in front of the player with Open Brush's own tools.
    /// </summary>
    public sealed class TwDrawScreen : TwScreen
    {
        private readonly MatchMachine m_Machine;
        private readonly TwClockView m_Clock;
        private readonly TwButton m_Submit;
        private readonly TextMeshPro m_GetReady;

        public TwDrawScreen(MatchMachine machine, Action onSubmit)
            : base("Draw", 1.5f, 0.4f)
        {
            m_Machine = machine;
            Stage stage = machine.CurrentStage;
            new TwChainTrack(T, new Vector3(0f, 0.22f, 0f), machine.Planner, stage.Turn);
            m_Clock = new TwClockView(T, new Vector3(0f, 0.04f, 0f), 0.34f);
            m_GetReady = TwUi.Text(T, TwCopy.GetReady, TwUi.Px(TwTokens.Label) * 1.3f, TwTokens.Sun,
                TwFonts.Bold, new Vector3(0f, 0.11f, 0f));
            var card = new TwWordCard(T, new Vector3(0f, -0.15f, 0f), 0.56f, 0.15f);
            card.Set(TwCopy.Draw.TrimEnd('.'), machine.PromptText ?? string.Empty);
            m_Submit = Button(TwCopy.Submit, 0.58f, -0.04f, 0.3f, 0.1f, TwButton.Style.Primary, onSubmit);
            m_Submit.SetInteractable(false);
        }

        public override void Tick(float dt)
        {
            TurnClock clock = m_Machine.Clock;
            bool counting = m_Machine.Phase == MatchPhase.Countdown;
            m_GetReady.gameObject.SetActive(counting);
            m_Submit.SetInteractable(m_Machine.Phase == MatchPhase.Turn);
            float fraction = clock.Total > 0f ? clock.Remaining / clock.Total : 0f;
            m_Clock.Set(clock.WholeSeconds, fraction, !counting && clock.Warning);
        }
    }

    /// <summary>
    /// A guess turn: the previous player's drawing floats in front, and the player types what it is
    /// with the on-screen keyboard (or a physical one on a desktop).
    /// </summary>
    public sealed class TwGuessScreen : TwScreen
    {
        private readonly MatchMachine m_Machine;
        private readonly TwClockView m_Clock;
        private readonly TwGuessField m_Field;
        private readonly TwKeyboard m_Keyboard;
        private readonly TwButton m_Guess;
        private float m_Time;

        public TwGuessScreen(MatchMachine machine, Action<string> onGuess)
            : base("Guess", 1.2f, 0f)
        {
            m_Machine = machine;
            Stage stage = machine.CurrentStage;
            new TwChainTrack(T, new Vector3(0f, 0.5f, 0f), machine.Planner, stage.Turn);
            m_Clock = new TwClockView(T, new Vector3(-0.5f, 0.34f, 0f), 0.3f);
            var card = new TwWordCard(T, new Vector3(0.5f, 0.34f, 0f), 0.4f, 0.16f);
            card.Set(TwCopy.Guess.TrimEnd('.'), TwCopy.WhatIsThis);

            m_Field = new TwGuessField(T, new Vector3(-0.1f, -0.28f, 0f), 0.78f);
            m_Keyboard = new TwKeyboard(T, new Vector3(0f, -0.42f, 0f));
            m_Keyboard.Changed += m_Field.SetValue;
            m_Keyboard.Submitted += onGuess;
            m_Guess = Button("Guess", 0.52f, -0.28f, 0.26f, 0.09f, TwButton.Style.Primary, m_Keyboard.Submit);
            m_Keyboard.StartListening();
        }

        public override void Tick(float dt)
        {
            m_Time += dt;
            TurnClock clock = m_Machine.Clock;
            float fraction = clock.Total > 0f ? clock.Remaining / clock.Total : 0f;
            m_Clock.Set(clock.WholeSeconds, fraction, clock.Warning);
            m_Field.Tick(m_Time);
            m_Guess.SetInteractable(GuessNormalizer.IsAcceptable(m_Keyboard.Text));
        }

        /// <summary>Says so when the drawing to guess turned out to be a blank canvas.</summary>
        public void ShowBlank()
        {
            TwUi.Text(T, TwCopy.NoDrawing, TwUi.Px(TwTokens.DisplayMd), TwTokens.InkMuted, TwFonts.Bold,
                new Vector3(0f, 0.12f, 0f));
        }

        /// <summary>What has been typed so far (used when the turn times out).</summary>
        public string TypedText
        {
            get { return m_Keyboard.Text; }
        }

        public override void Dispose()
        {
            m_Keyboard.StopListening();
            base.Dispose();
        }
    }
}
