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
using System.Collections.Generic;
using System.Globalization;
using TMPro;
using UnityEngine;

namespace Telewheel
{
    /// <summary>A player's coloured badge: their icon colour with the first letter of their name.</summary>
    public static class TwBadge
    {
        public static GameObject Create(
            Transform parent, int icon, string name, float radiusMeters, Vector3 localPosition)
        {
            var root = new GameObject("Badge");
            root.transform.SetParent(parent, false);
            root.transform.localPosition = localPosition;
            TwGfx.Shape(root.transform, "Rim", TwGfx.Disc(radiusMeters + TwUi.Px(4f)), TwTokens.Deep,
                new Vector3(0f, 0f, TwUi.ZBorder));
            uint color = TwTokens.PlayerColors[PlayerProfile.CleanIcon(icon)];
            TwGfx.Shape(root.transform, "Disc", TwGfx.Disc(radiusMeters), color, new Vector3(0f, 0f, TwUi.ZFace));
            TwUi.Text(root.transform, Initial(name), radiusMeters * 1.15f, TwTokens.Deep, TwFonts.Display, Vector3.zero);
            return root;
        }

        // The first letter, taking care not to cut an emoji in half.
        private static string Initial(string name)
        {
            if (string.IsNullOrEmpty(name))
            {
                return "?";
            }
            return StringInfo.GetNextTextElement(name).ToUpperInvariant();
        }
    }

    /// <summary>What the Play Online menu shows and does.</summary>
    public sealed class TwOnlineMenuOptions
    {
        public PlayerProfile Profile;

        /// <summary>Null when online play works; otherwise why it does not.</summary>
        public string UnavailableReason;

        public bool PracticeVisible;
        public Action OnHost;
        public Action OnJoin;
        public Action OnProfile;
        public Action OnPracticeHost;
        public Action OnPracticeJoin;
        public Action OnBack;
    }

    /// <summary>Play Online: pick who you are, then host a room or join one.</summary>
    public sealed class TwOnlineMenuScreen : TwScreen
    {
        public TwOnlineMenuScreen(TwOnlineMenuOptions options)
            : base("Online menu", 1.5f, 0f)
        {
            bool available = options.UnavailableReason == null;
            Backdrop(1.0f, 1.3f);
            Title(TwCopy.PlayOnline, 0.53f);

            // Who you are, with the badge beside it.
            string shown = options.Profile.Name.Length == 0 ? TwCopy.SetYourName : options.Profile.Name;
            TwBadge.Create(T, options.Profile.Icon, options.Profile.Name, 0.04f, new Vector3(-0.36f, 0.4f, 0f));
            Button(shown, 0.05f, 0.4f, 0.56f, 0.09f, TwButton.Style.Secondary, options.OnProfile);

            if (!available)
            {
                Body(options.UnavailableReason, 0.26f, 0.85f);
            }

            TwButton host = Button(TwCopy.PrivateRoom, 0f, 0.14f, 0.64f, 0.12f, TwButton.Style.Primary, options.OnHost);
            TwButton join = Button(TwCopy.JoinWithCode, 0f, -0.02f, 0.64f, 0.12f, TwButton.Style.Secondary, options.OnJoin);
            TwButton publicGame = Button(TwCopy.PublicGame, 0f, -0.18f, 0.64f, 0.12f, TwButton.Style.Secondary, null);
            host.SetInteractable(available);
            join.SetInteractable(available);
            publicGame.SetInteractable(false);
            Label(TwCopy.ComingSoon, 0f, -0.27f);

            if (options.PracticeVisible)
            {
                Button(TwCopy.PracticeHost, -0.22f, -0.38f, 0.4f, 0.08f, TwButton.Style.Ghost, options.OnPracticeHost);
                Button(TwCopy.PracticeJoin, 0.22f, -0.38f, 0.4f, 0.08f, TwButton.Style.Ghost, options.OnPracticeJoin);
            }
            Button(TwCopy.Back, 0f, -0.55f, 0.3f, 0.08f, TwButton.Style.Ghost, options.OnBack);
        }
    }

    /// <summary>Pick an icon and type a name. They are remembered and shown to the other players.</summary>
    public sealed class TwProfileScreen : TwScreen
    {
        private readonly PlayerProfile m_Profile;
        private readonly TwGuessField m_Field;
        private readonly TwKeyboard m_Keyboard;
        private readonly TwButton m_Save;
        private readonly GameObject m_Selection;
        private float m_Time;

        public TwProfileScreen(PlayerProfile current, Action<PlayerProfile> onSave, Action onBack)
            : base("Profile", 1.2f, 0f)
        {
            m_Profile = new PlayerProfile(current.Name, current.Icon);
            Backdrop(1.3f, 1.25f);
            Title(TwCopy.YourProfile, 0.52f);
            Label(TwCopy.PickAnIcon, 0f, 0.4f);

            const float pitch = 0.14f;
            float x0 = -pitch * (PlayerProfile.IconCount - 1) * 0.5f;
            m_Selection = TwGfx.Shape(T, "Selected", TwGfx.Disc(0.062f), TwTokens.Sun, new Vector3(0f, 0.26f, 0.005f));
            for (int i = 0; i < PlayerProfile.IconCount; i++)
            {
                int icon = i;
                var position = new Vector3(x0 + pitch * i, 0.26f, 0f);
                TwBadge.Create(T, icon, m_Profile.Name, 0.05f, position);
                TwButton.Create(T, string.Empty, 0.12f, 0.12f, TwButton.Style.Ghost, position, () => ChooseIcon(icon));
            }
            ChooseIcon(m_Profile.Icon);

            m_Field = new TwGuessField(T, new Vector3(-0.1f, 0.08f, 0f), 0.78f, TwCopy.TypeYourName);
            m_Keyboard = new TwKeyboard(T, new Vector3(0f, -0.06f, 0f), PlayerProfile.MaxNameLength);
            m_Keyboard.Changed += OnTyped;
            m_Keyboard.Submitted += text => Save(onSave);
            m_Keyboard.SetText(m_Profile.Name);
            m_Save = Button(TwCopy.Save, 0.52f, 0.08f, 0.26f, 0.09f, TwButton.Style.Primary, () => Save(onSave));
            Button(TwCopy.Back, 0f, -0.52f, 0.3f, 0.08f, TwButton.Style.Ghost, onBack);
            m_Keyboard.StartListening();
        }

        public override void Tick(float dt)
        {
            m_Time += dt;
            m_Field.Tick(m_Time);
            m_Save.SetInteractable(PlayerProfile.CleanName(m_Keyboard.Text).Length > 0);
        }

        public override void Dispose()
        {
            m_Keyboard.StopListening();
            base.Dispose();
        }

        private void ChooseIcon(int icon)
        {
            m_Profile.Icon = PlayerProfile.CleanIcon(icon);
            const float pitch = 0.14f;
            float x0 = -pitch * (PlayerProfile.IconCount - 1) * 0.5f;
            m_Selection.transform.localPosition = new Vector3(x0 + pitch * m_Profile.Icon, 0.26f, 0.005f);
        }

        private void OnTyped(string text)
        {
            m_Field.SetValue(PlayerProfile.Capitalize(text));
        }

        private void Save(Action<PlayerProfile> onSave)
        {
            string name = PlayerProfile.Capitalize(PlayerProfile.CleanName(m_Keyboard.Text));
            if (name.Length == 0)
            {
                return;
            }
            onSave(new PlayerProfile(name, m_Profile.Icon));
        }
    }

    /// <summary>Type the four-letter room code a friend read out.</summary>
    public sealed class TwJoinCodeScreen : TwScreen
    {
        private readonly TwGuessField m_Field;
        private readonly TwKeyboard m_Keyboard;
        private readonly TwButton m_Join;
        private float m_Time;

        public TwJoinCodeScreen(Action<string> onJoin, Action onBack)
            : base("Join code", 1.2f, 0f)
        {
            Backdrop(1.3f, 1.0f);
            Title(TwCopy.EnterRoomCode, 0.4f);
            m_Field = new TwGuessField(T, new Vector3(-0.1f, 0.2f, 0f), 0.78f, TwCopy.TypeTheCode);
            m_Keyboard = new TwKeyboard(T, new Vector3(0f, 0.06f, 0f), RoomCode.Length, lettersOnly: true);
            m_Keyboard.Changed += text => m_Field.SetValue(RoomCode.Normalize(text));
            m_Keyboard.Submitted += text => TryJoin(onJoin);
            m_Join = Button(TwCopy.Join, 0.52f, 0.2f, 0.26f, 0.09f, TwButton.Style.Primary, () => TryJoin(onJoin));
            Button(TwCopy.Back, 0f, -0.4f, 0.3f, 0.08f, TwButton.Style.Ghost, onBack);
            m_Keyboard.StartListening();
        }

        public override void Tick(float dt)
        {
            m_Time += dt;
            m_Field.Tick(m_Time);
            m_Join.SetInteractable(RoomCode.IsValid(RoomCode.Normalize(m_Keyboard.Text)));
        }

        public override void Dispose()
        {
            m_Keyboard.StopListening();
            base.Dispose();
        }

        private void TryJoin(Action<string> onJoin)
        {
            string code = RoomCode.Normalize(m_Keyboard.Text);
            if (RoomCode.IsValid(code))
            {
                onJoin(code);
            }
        }
    }

    /// <summary>The room before the match: who is here, the rules, and the host's START.</summary>
    public sealed class TwLobbyScreen : TwScreen
    {
        private readonly OnlineSession m_Session;
        private readonly TwButton m_Start;
        private readonly TextMeshPro m_StartNote;
        private GameObject m_Dynamic;
        private int m_Version = -1;

        public TwLobbyScreen(OnlineSession session, Action onStart, Action onLeave)
            : base("Lobby", 1.5f, 0f)
        {
            m_Session = session;
            Backdrop(1.3f, 1.2f);
            Title(TwCopy.RoomLabel(session.JoinCode), 0.5f);
            if (session.IsPractice)
            {
                Body(TwCopy.PracticeNote, 0.4f, 1.1f);
            }

            if (session.IsHost)
            {
                m_Start = Button(TwCopy.Start, 0f, -0.4f, 0.54f, 0.12f, TwButton.Style.Primary, onStart);
                m_Start.SetInteractable(false);
            }
            m_StartNote = Body(string.Empty, -0.5f, 1.0f);
            Button(TwCopy.LeaveRoom, 0f, -0.55f, 0.4f, 0.07f, TwButton.Style.Ghost, onLeave);
            Rebuild();
        }

        public override void Tick(float dt)
        {
            if (m_Session.Client.LobbyVersion != m_Version)
            {
                Rebuild();
            }
            if (m_Start != null)
            {
                bool canStart = m_Session.Room.CanStart;
                m_Start.SetInteractable(canStart);
                m_StartNote.text = canStart ? string.Empty : TwCopy.WaitingForPlayers;
            }
            else
            {
                m_StartNote.text = TwCopy.WaitingForHost;
            }
        }

        // The roster and rules change as people come and go, so they are rebuilt whenever the room changes.
        private void Rebuild()
        {
            if (m_Dynamic != null)
            {
                m_Dynamic.SetActive(false);
                UnityEngine.Object.Destroy(m_Dynamic);
            }
            OnlineMatchClient client = m_Session.Client;
            m_Version = client.LobbyVersion;
            m_Dynamic = new GameObject("Roster");
            m_Dynamic.transform.SetParent(T, false);

            Transform d = m_Dynamic.transform;
            TwUi.Text(d, TwCopy.Players + "  " + client.Names.Count + "/" + MatchSettings.MaxPlayers,
                TwUi.Px(TwTokens.Label) * 1.15f, TwTokens.InkMuted, TwFonts.Bold,
                new Vector3(-0.58f, 0.3f, 0f), TextAlignmentOptions.Left);
            for (int seat = 0; seat < client.Names.Count; seat++)
            {
                float y = 0.2f - 0.07f * seat;
                TwBadge.Create(d, client.IconOf(seat), client.Names[seat], 0.027f, new Vector3(-0.55f, y, 0f));
                uint color = seat == client.LocalSeat ? TwTokens.Sun : TwTokens.Ink;
                TwUi.Text(d, client.Names[seat], TwUi.Px(TwTokens.Body), color, TwFonts.Bold,
                    new Vector3(-0.49f, y, 0f), TextAlignmentOptions.Left);
                string tag = seat == 0 ? TwCopy.HostTag : (seat == client.LocalSeat ? TwCopy.YouTag : string.Empty);
                if (seat == 0 && seat == client.LocalSeat)
                {
                    tag = TwCopy.HostTag + " / " + TwCopy.YouTag;
                }
                if (tag.Length > 0)
                {
                    TwUi.Text(d, tag, TwUi.Px(TwTokens.Label), TwTokens.InkMuted, TwFonts.Bold,
                        new Vector3(-0.02f, y, 0f), TextAlignmentOptions.Right);
                }
            }

            BuildRules(d, client);
        }

        private void BuildRules(Transform d, OnlineMatchClient client)
        {
            TwUi.Text(d, "RULES", TwUi.Px(TwTokens.Label) * 1.15f, TwTokens.InkMuted, TwFonts.Bold,
                new Vector3(0.12f, 0.3f, 0f), TextAlignmentOptions.Left);
            TwUi.Text(d, TwCopy.Rounds, TwUi.Px(TwTokens.Label), TwTokens.InkMuted, TwFonts.Bold,
                new Vector3(0.12f, 0.2f, 0f), TextAlignmentOptions.Left);
            TwUi.Text(d, "WORDS", TwUi.Px(TwTokens.Label), TwTokens.InkMuted, TwFonts.Bold,
                new Vector3(0.12f, 0.06f, 0f), TextAlignmentOptions.Left);
            string words = client.Filter == ContentFilter.Family ? TwCopy.FamilyFriendly : TwCopy.Raunchy;

            if (m_Session.IsHost)
            {
                OnlineRoomHost room = m_Session.Room;
                TwButton.Create(d, "-", 0.09f, 0.09f, TwButton.Style.Secondary, new Vector3(0.36f, 0.2f, 0f),
                    () => room.SetRules(room.Settings.Rounds - 1, room.Settings.Filter));
                TwUi.Display(d, client.RoundCount.ToString(), TwUi.Px(TwTokens.DisplayLg), TwTokens.Sun,
                    new Vector3(0.47f, 0.2f, 0f));
                TwButton.Create(d, "+", 0.09f, 0.09f, TwButton.Style.Secondary, new Vector3(0.58f, 0.2f, 0f),
                    () => room.SetRules(room.Settings.Rounds + 1, room.Settings.Filter));
                TwButton.Create(d, words, 0.5f, 0.09f, TwButton.Style.Secondary, new Vector3(0.38f, -0.06f, 0f),
                    () => room.SetRules(room.Settings.Rounds, client.Filter == ContentFilter.Family
                        ? ContentFilter.Raunchy : ContentFilter.Family));
            }
            else
            {
                TwUi.Display(d, client.RoundCount.ToString(), TwUi.Px(TwTokens.DisplayLg), TwTokens.Sun,
                    new Vector3(0.47f, 0.2f, 0f));
                TwUi.Text(d, words, TwUi.Px(TwTokens.Body), TwTokens.Ink, TwFonts.Bold,
                    new Vector3(0.12f, -0.02f, 0f), TextAlignmentOptions.Left);
            }
            BuildEnvironmentPicker(d, client);
        }

        // The host picks the scenery for everyone; guests just see what was picked.
        private void BuildEnvironmentPicker(Transform d, OnlineMatchClient client)
        {
            List<string> environments = OpenBrushFacade.SelectableEnvironmentNames();
            if (environments.Count < 2)
            {
                return;
            }
            string current = client.Environment.Length == 0 ? TwCopy.DefaultEnvironment : client.Environment;
            TwUi.Text(d, TwCopy.EnvironmentLabel, TwUi.Px(TwTokens.Label), TwTokens.InkMuted, TwFonts.Bold,
                new Vector3(0.12f, -0.14f, 0f), TextAlignmentOptions.Left);
            if (m_Session.IsHost)
            {
                OnlineRoomHost room = m_Session.Room;
                TwButton.Create(d, current, 0.5f, 0.09f, TwButton.Style.Secondary, new Vector3(0.38f, -0.26f, 0f),
                    () =>
                    {
                        // Read the pick at click time: the screen is rebuilt after each change.
                        int next = (environments.IndexOf(client.Environment) + 1) % environments.Count;
                        room.SetEnvironment(environments[next]);
                    });
            }
            else
            {
                TwUi.Text(d, current.ToUpperInvariant(), TwUi.Px(TwTokens.Body), TwTokens.Ink, TwFonts.Bold,
                    new Vector3(0.12f, -0.22f, 0f), TextAlignmentOptions.Left);
            }
        }
    }

    /// <summary>Shown after you finish your part: who the room is still waiting for.</summary>
    public sealed class TwWaitingScreen : TwScreen
    {
        private readonly IMatchView m_View;
        private readonly TextMeshPro m_Line;
        private int m_Version = -1;

        public TwWaitingScreen(IMatchView view)
            : base("Waiting", 1.6f, 0.05f)
        {
            m_View = view;
            Backdrop(1.0f, 0.4f);
            Label("Your part is done", 0f, 0.1f);
            m_Line = Heading(string.Empty, -0.02f, 0.9f);
            Refresh();
        }

        public override void Tick(float dt)
        {
            Refresh();
        }

        private void Refresh()
        {
            // The list only changes when someone finishes or leaves, so do not rebuild it every frame.
            if (m_View.StepVersion == m_Version)
            {
                return;
            }
            m_Version = m_View.StepVersion;
            var names = new List<string>();
            foreach (int seat in m_View.WaitingFor)
            {
                names.Add(m_View.NameOf(seat));
            }
            m_Line.text = TwCopy.WaitingFor(names);
        }
    }

    /// <summary>A message that needs an answer: the room was full, the host left, and so on.</summary>
    public sealed class TwNoticeScreen : TwScreen
    {
        public TwNoticeScreen(string title, string body, string buttonLabel, Action onClose)
            : base("Notice", 1.5f, 0f)
        {
            Backdrop(1.0f, 0.7f);
            Title(title, 0.2f);
            Body(body, 0.04f, 0.85f);
            Button(buttonLabel, 0f, -0.2f, 0.4f, 0.11f, TwButton.Style.Primary, onClose);
        }
    }

    /// <summary>A brief message that goes away by itself ("Ann left").</summary>
    public sealed class TwToastScreen : TwScreen
    {
        private float m_Remaining;

        public TwToastScreen(string text, float seconds)
            : base("Toast", 1.4f, 0.62f)
        {
            m_Remaining = seconds;
            Backdrop(1.0f, 0.12f);
            Heading(text, 0f, 0.9f);
        }

        public bool Expired
        {
            get { return m_Remaining <= 0f; }
        }

        public override void Tick(float dt)
        {
            m_Remaining -= dt;
        }
    }
}
