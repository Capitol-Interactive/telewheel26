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

namespace Telewheel
{
    /// <summary>One person in the room.</summary>
    public sealed class RoomMember
    {
        /// <summary>The transport's id for this connection (<see cref="OnlineRoomHost.LocalPeer"/> for the host).</summary>
        public int Peer;

        public string Name;
        public int Icon;

        /// <summary>Position in the room, which is also the seat in the match. The host is always 0.</summary>
        public int Seat;

        /// <summary>The player left after the match started; the host plays their turns.</summary>
        public bool Left;
    }

    /// <summary>
    /// The host's side of a room: who has joined, the rules, and then the match. It sits between the
    /// transport and <see cref="OnlineMatchHost"/>: it welcomes players, turns peer ids into seats,
    /// and passes messages both ways. The person hosting plays too, through <see cref="LocalPort"/>.
    /// </summary>
    public sealed class OnlineRoomHost
    {
        /// <summary>The host's own player.</summary>
        public const int LocalPeer = 0;

        /// <summary>Bumped when the wire format changes; players on another version are turned away.</summary>
        public const int ProtocolVersion = 1;

        private readonly INetHostPort m_Port;
        private readonly MatchSettings m_Settings;
        private readonly Func<ContentFilter, IList<string>> m_Words;
        private readonly List<RoomMember> m_Members = new List<RoomMember>();
        private readonly LocalLink m_Local;

        private OnlineMatchHost m_Match;
        private string m_Environment = string.Empty;
        private string m_StartProblem = string.Empty;
        private bool m_Closed;

        /// <param name="port">The connections to the other players.</param>
        /// <param name="settings">The rules; the host can change rounds and content filter in the lobby.</param>
        /// <param name="hostProfile">Who the host is.</param>
        /// <param name="words">Gives the word list for a content filter.</param>
        public OnlineRoomHost(
            INetHostPort port, MatchSettings settings, PlayerProfile hostProfile,
            Func<ContentFilter, IList<string>> words)
        {
            m_Port = port;
            m_Settings = settings;
            m_Words = words;
            m_Local = new LocalLink(this);
            m_Members.Add(new RoomMember
            {
                Peer = LocalPeer,
                Name = CleanName(hostProfile == null ? null : hostProfile.Name, 0),
                Icon = hostProfile == null ? 0 : PlayerProfile.CleanIcon(hostProfile.Icon),
                Seat = 0,
            });
            m_Port.FromPeer += OnFromPeer;
            m_Port.PeerLeft += OnPeerLeft;
        }

        /// <summary>Raised when someone joins or leaves, or the rules change.</summary>
        public event Action RosterChanged;

        /// <summary>The host's own connection to the room. Hand it to an <see cref="OnlineMatchClient"/>.</summary>
        public INetClientPort LocalPort
        {
            get { return m_Local; }
        }

        public IReadOnlyList<RoomMember> Members
        {
            get { return m_Members; }
        }

        public MatchSettings Settings
        {
            get { return m_Settings; }
        }

        /// <summary>Null until the match starts.</summary>
        public OnlineMatchHost Match
        {
            get { return m_Match; }
        }

        public bool InMatch
        {
            get { return m_Match != null; }
        }

        public bool Closed
        {
            get { return m_Closed; }
        }

        public string Environment
        {
            get { return m_Environment; }
        }

        /// <summary>Why <see cref="Start"/> last refused, in words a player can read.</summary>
        public string StartProblem
        {
            get { return m_StartProblem; }
        }

        public bool CanStart
        {
            get { return !m_Closed && m_Match == null && m_Members.Count >= MatchSettings.MinPlayers; }
        }

        public int ActiveCount
        {
            get
            {
                int count = 0;
                foreach (RoomMember member in m_Members)
                {
                    if (!member.Left)
                    {
                        count++;
                    }
                }
                return count;
            }
        }

        // ----- Lobby -----

        /// <summary>Sets the rounds and the content filter. Only before the match starts.</summary>
        public void SetRules(int rounds, ContentFilter filter)
        {
            if (m_Match != null || m_Closed)
            {
                return;
            }
            m_Settings.Rounds = Math.Max(MatchSettings.MinRounds, Math.Min(MatchSettings.MaxRounds, rounds));
            m_Settings.Filter = filter;
            SendLobbyToAll();
        }

        /// <summary>The host's environment pick (VR only); everyone applies it locally.</summary>
        public void SetEnvironment(string id)
        {
            if (m_Closed)
            {
                return;
            }
            m_Environment = id ?? string.Empty;
            SendToAllRemote(NetMessage.Environment(m_Environment));
            m_Local.Deliver(NetMessage.Environment(m_Environment));
        }

        /// <summary>Starts the match. Returns false, and says why in <see cref="StartProblem"/>, if it cannot.</summary>
        public bool Start()
        {
            m_StartProblem = string.Empty;
            if (m_Closed || m_Match != null)
            {
                return false;
            }
            if (m_Members.Count < MatchSettings.MinPlayers)
            {
                m_StartProblem = "Waiting for more players.";
                return false;
            }
            IList<string> words = m_Words(m_Settings.Filter);
            if (words == null || words.Count < m_Settings.WheelSegments)
            {
                m_StartProblem = "Not enough words for that mode yet.";
                return false;
            }
            SendLobbyToAll();
            m_Match = new OnlineMatchHost(m_Settings, new WordDeck(new List<string>(words), m_Settings.Seed));
            foreach (RoomMember member in m_Members)
            {
                m_Match.AddPlayer(member.Name);
            }
            m_Match.Send += OnMatchSend;
            m_Match.Start();
            return true;
        }

        /// <summary>Ends the room: tells everyone, and ignores everything after.</summary>
        public void Close(EndReason reason)
        {
            if (m_Closed)
            {
                return;
            }
            NetMessage ended = NetMessage.MatchEnded(reason);
            SendToAllRemote(ended);
            m_Local.Deliver(ended);
            m_Closed = true;
            m_Port.FromPeer -= OnFromPeer;
            m_Port.PeerLeft -= OnPeerLeft;
        }

        /// <summary>Runs the match clock and delivers what the host's own player is owed. Call every frame.</summary>
        public void Tick(float dt)
        {
            if (!m_Closed && m_Match != null)
            {
                m_Match.Tick(dt);
            }
            m_Local.Flush();
        }

        // ----- Messages in -----

        private void OnFromPeer(int peer, NetMessage message)
        {
            if (m_Closed || message == null)
            {
                return;
            }
            RoomMember member = FindByPeer(peer);
            if (message.Kind == NetKind.Hello)
            {
                OnHello(peer, member, message);
                return;
            }
            if (member != null && !member.Left && m_Match != null)
            {
                m_Match.Receive(member.Seat, message);
            }
        }

        private void OnHello(int peer, RoomMember existing, NetMessage message)
        {
            if (existing != null)
            {
                // Someone changing their mind about their name, or the host's own player checking in.
                if (m_Match == null)
                {
                    existing.Name = UniqueName(message.Text, existing.Seat, existing);
                    existing.Icon = PlayerProfile.CleanIcon(message.A);
                    SendLobbyToAll();
                }
                return;
            }
            if (message.B != ProtocolVersion)
            {
                SendTo(peer, NetMessage.Rejected(RejectReason.WrongVersion));
                return;
            }
            if (m_Match != null)
            {
                SendTo(peer, NetMessage.Rejected(RejectReason.MatchStarted));
                return;
            }
            if (m_Members.Count >= MatchSettings.MaxPlayers)
            {
                SendTo(peer, NetMessage.Rejected(RejectReason.RoomFull));
                return;
            }
            var member = new RoomMember { Peer = peer, Seat = m_Members.Count, Icon = PlayerProfile.CleanIcon(message.A) };
            member.Name = UniqueName(message.Text, member.Seat, null);
            m_Members.Add(member);
            SendLobbyToAll();
        }

        private void OnPeerLeft(int peer)
        {
            RoomMember member = FindByPeer(peer);
            if (m_Closed || member == null || member.Left || peer == LocalPeer)
            {
                return;
            }
            if (m_Match == null)
            {
                m_Members.Remove(member);
                for (int i = 0; i < m_Members.Count; i++)
                {
                    m_Members[i].Seat = i;
                }
                SendLobbyToAll();
                return;
            }
            member.Left = true;
            m_Match.RemovePlayer(member.Seat);
            SendToAll(NetMessage.PlayerLeft(member.Seat));
            RaiseRosterChanged();
            if (ActiveCount < MatchSettings.MinPlayers)
            {
                Close(EndReason.NotEnoughPlayers);
            }
        }

        // ----- Messages out -----

        private void OnMatchSend(int seat, NetMessage message)
        {
            if (seat == OnlineMatchHost.Everyone)
            {
                SendToAll(message);
                return;
            }
            RoomMember member = FindBySeat(seat);
            if (member != null && !member.Left)
            {
                SendTo(member.Peer, message);
            }
        }

        private void SendLobbyToAll()
        {
            var names = new string[m_Members.Count];
            var icons = new int[m_Members.Count];
            for (int i = 0; i < m_Members.Count; i++)
            {
                names[i] = m_Members[i].Name;
                icons[i] = m_Members[i].Icon;
            }
            foreach (RoomMember member in m_Members)
            {
                SendTo(member.Peer, NetMessage.LobbyState(
                    member.Seat, m_Settings.Rounds, m_Settings.Filter, names, icons, m_Environment));
            }
            RaiseRosterChanged();
        }

        private void SendTo(int peer, NetMessage message)
        {
            if (peer == LocalPeer)
            {
                m_Local.Deliver(message);
            }
            else
            {
                m_Port.SendToPeer(peer, message);
            }
        }

        private void SendToAll(NetMessage message)
        {
            foreach (RoomMember member in m_Members)
            {
                if (!member.Left)
                {
                    SendTo(member.Peer, message);
                }
            }
        }

        private void SendToAllRemote(NetMessage message)
        {
            foreach (RoomMember member in m_Members)
            {
                if (!member.Left && member.Peer != LocalPeer)
                {
                    m_Port.SendToPeer(member.Peer, message);
                }
            }
        }

        private void RaiseRosterChanged()
        {
            Action handler = RosterChanged;
            if (handler != null)
            {
                handler();
            }
        }

        // ----- Helpers -----

        private RoomMember FindByPeer(int peer)
        {
            foreach (RoomMember member in m_Members)
            {
                if (member.Peer == peer)
                {
                    return member;
                }
            }
            return null;
        }

        private RoomMember FindBySeat(int seat)
        {
            return seat >= 0 && seat < m_Members.Count ? m_Members[seat] : null;
        }

        private static string CleanName(string raw, int seat)
        {
            string clean = PlayerProfile.CleanName(raw);
            return clean.Length == 0 ? TwCopy.DefaultPlayerName(seat) : clean;
        }

        // Two players with the same name would be indistinguishable in the reveal, so the later one gets a number.
        private string UniqueName(string raw, int seat, RoomMember self)
        {
            string baseName = CleanName(raw, seat);
            string candidate = baseName;
            for (int suffix = 2; NameTaken(candidate, self); suffix++)
            {
                string tail = " " + suffix;
                int room = PlayerProfile.MaxNameLength - tail.Length;
                candidate = (baseName.Length > room ? baseName.Substring(0, room).TrimEnd() : baseName) + tail;
            }
            return candidate;
        }

        private bool NameTaken(string name, RoomMember self)
        {
            foreach (RoomMember member in m_Members)
            {
                if (member != self && string.Equals(member.Name, name, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }
            return false;
        }

        /// <summary>
        /// The host player's connection. Messages for it are queued and handed over on <see cref="Flush"/>
        /// so the game never reacts to a message in the middle of the host's own processing.
        /// </summary>
        private sealed class LocalLink : INetClientPort
        {
            private readonly OnlineRoomHost m_Room;
            private readonly Queue<NetMessage> m_Inbox = new Queue<NetMessage>();

            public LocalLink(OnlineRoomHost room)
            {
                m_Room = room;
            }

            public event Action<NetMessage> FromHost;

            // The host's own connection cannot be lost; it ends with the room (MatchEnded).
            public event Action HostLost
            {
                add { }
                remove { }
            }

            public void SendToHost(NetMessage message)
            {
                m_Room.OnFromPeer(LocalPeer, message);
            }

            public void Deliver(NetMessage message)
            {
                m_Inbox.Enqueue(message);
            }

            public void Flush()
            {
                while (m_Inbox.Count > 0)
                {
                    NetMessage message = m_Inbox.Dequeue();
                    Action<NetMessage> handler = FromHost;
                    if (handler != null)
                    {
                        handler(message);
                    }
                }
            }
        }
    }
}
