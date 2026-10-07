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
using System.IO;

namespace Telewheel
{
    public enum NetKind : byte
    {
        // Player to host.
        SpinResult = 1,
        Ready = 2,
        SubmitDrawing = 3,
        SubmitGuess = 4,
        Vote = 5,
        Advance = 6,

        /// <summary>First message a player sends: who they are. Everything else waits for the welcome.</summary>
        Hello = 7,

        // Host to players.
        PhaseChanged = 20,
        WheelWords = 21,
        TurnStart = 22,
        PresentItem = 23,
        VoteOpen = 24,
        VoteResult = 25,
        RoundEnd = 26,
        GameEnd = 27,
        LobbyState = 28,
        Rejected = 29,
        PlayerLeft = 30,
        Environment = 31,
        Progress = 32,
        MatchEnded = 33,
    }

    /// <summary>Why the host turned a player away.</summary>
    public enum RejectReason
    {
        RoomFull = 0,
        MatchStarted = 1,
        WrongVersion = 2,
    }

    /// <summary>Why a match stopped before its last round.</summary>
    public enum EndReason
    {
        HostEnded = 0,
        NotEnoughPlayers = 1,
    }

    /// <summary>
    /// One message between a player and the match host. The same small set of fields serves every
    /// kind (so one codec covers them all); use the factory methods, which say what each field means.
    /// </summary>
    public sealed class NetMessage
    {
        public NetKind Kind;
        public int A;
        public int B;
        public int C;
        public int D;
        public float X;
        public float Y;
        public string Text;
        public byte[] Data;
        public string[] Words;
        public int[] Numbers;

        // ----- Player to host -----

        /// <summary>The wheel stopped on <paramref name="segment"/>.</summary>
        public static NetMessage SpinResult(int segment)
        {
            return new NetMessage { Kind = NetKind.SpinResult, A = segment };
        }

        /// <summary>The player has seen their word and is ready for the first turn.</summary>
        public static NetMessage Ready()
        {
            return new NetMessage { Kind = NetKind.Ready };
        }

        public static NetMessage SubmitDrawing(byte[] drawing)
        {
            return new NetMessage { Kind = NetKind.SubmitDrawing, Data = drawing };
        }

        public static NetMessage SubmitGuess(string text)
        {
            return new NetMessage { Kind = NetKind.SubmitGuess, Text = text };
        }

        public static NetMessage Vote(bool yes)
        {
            return new NetMessage { Kind = NetKind.Vote, A = yes ? 1 : 0 };
        }

        /// <summary>The chain owner moves the reveal on to the next item.</summary>
        public static NetMessage Advance()
        {
            return new NetMessage { Kind = NetKind.Advance };
        }

        // ----- Host to players -----

        /// <summary>The match entered <paramref name="phase"/> in <paramref name="round"/>.</summary>
        public static NetMessage PhaseChanged(OnlinePhase phase, int round)
        {
            return new NetMessage { Kind = NetKind.PhaseChanged, A = (int)phase, B = round };
        }

        /// <summary>The words on this player's wheel (sent only to them).</summary>
        public static NetMessage WheelWords(string[] words)
        {
            return new NetMessage { Kind = NetKind.WheelWords, Words = words };
        }

        /// <summary>
        /// A turn starts for this player. <paramref name="prompt"/> is the word or previous guess to
        /// draw (Draw turns); <paramref name="promptDrawing"/> is the drawing to guess (Guess turns).
        /// </summary>
        public static NetMessage TurnStart(
            int turn, StageKind kind, float countdownSeconds, float seconds, string prompt, byte[] promptDrawing)
        {
            return new NetMessage
            {
                Kind = NetKind.TurnStart,
                A = turn,
                B = (int)kind,
                X = countdownSeconds,
                Y = seconds,
                Text = prompt,
                Data = promptDrawing,
            };
        }

        /// <summary>One item of a chain's reveal. <paramref name="count"/> is how many items the chain has.</summary>
        public static NetMessage PresentItem(
            int chain, int index, int count, PresentItemKind kind, int player, string text, byte[] drawing, float seconds)
        {
            return new NetMessage
            {
                Kind = NetKind.PresentItem,
                A = chain,
                B = index,
                C = (int)kind,
                D = player,
                X = seconds,
                Text = text,
                Data = drawing,
                Numbers = new[] { count },
            };
        }

        /// <summary>Everyone votes on the final guess of <paramref name="chain"/>; <paramref name="word"/> was the original word.</summary>
        public static NetMessage VoteOpen(int chain, int guesser, string guess, string word, float seconds)
        {
            return new NetMessage
            {
                Kind = NetKind.VoteOpen,
                A = chain,
                D = guesser,
                Text = guess,
                Words = new[] { word },
                X = seconds,
            };
        }

        public static NetMessage VoteResult(int chain, bool landed, int[] scores)
        {
            return new NetMessage { Kind = NetKind.VoteResult, A = chain, B = landed ? 1 : 0, Numbers = scores };
        }

        public static NetMessage RoundEnd(int round, bool isFinal, int[] scores)
        {
            return new NetMessage { Kind = NetKind.RoundEnd, A = round, B = isFinal ? 1 : 0, Numbers = scores };
        }

        public static NetMessage GameEnd(int[] scores)
        {
            return new NetMessage { Kind = NetKind.GameEnd, Numbers = scores };
        }

        /// <summary>
        /// The room as <paramref name="yourSeat"/> sees it. Seats are positions in
        /// <paramref name="names"/>; the host is always seat 0.
        /// </summary>
        public static NetMessage LobbyState(
            int yourSeat, int rounds, ContentFilter filter, string[] names, int[] icons, string environment)
        {
            return new NetMessage
            {
                Kind = NetKind.LobbyState,
                A = yourSeat,
                B = rounds,
                C = (int)filter,
                Words = names,
                Numbers = icons,
                Text = environment,
            };
        }

        public static NetMessage Rejected(RejectReason reason)
        {
            return new NetMessage { Kind = NetKind.Rejected, A = (int)reason };
        }

        /// <summary>A player left mid-match; the host is playing their turns now.</summary>
        public static NetMessage PlayerLeft(int seat)
        {
            return new NetMessage { Kind = NetKind.PlayerLeft, A = seat };
        }

        /// <summary>The host picked the environment everyone should see (VR only).</summary>
        public static NetMessage Environment(string id)
        {
            return new NetMessage { Kind = NetKind.Environment, Text = id };
        }

        /// <summary>Who has finished the current step, one bit per seat (for the "waiting for" list).</summary>
        public static NetMessage Progress(int doneMask)
        {
            return new NetMessage { Kind = NetKind.Progress, A = doneMask };
        }

        public static NetMessage MatchEnded(EndReason reason)
        {
            return new NetMessage { Kind = NetKind.MatchEnded, A = (int)reason };
        }

        // ----- Player to host (continued) -----

        /// <summary>Introduces a player to the room. <paramref name="version"/> is the protocol version.</summary>
        public static NetMessage Hello(string name, int icon, int version)
        {
            return new NetMessage { Kind = NetKind.Hello, Text = name, A = icon, B = version };
        }
    }

    /// <summary>Turns a <see cref="NetMessage"/> into bytes and back, for whatever transport carries it.</summary>
    public static class NetCodec
    {
        public static byte[] Encode(NetMessage message)
        {
            using (var stream = new MemoryStream())
            using (var writer = new BinaryWriter(stream))
            {
                writer.Write((byte)message.Kind);
                writer.Write(message.A);
                writer.Write(message.B);
                writer.Write(message.C);
                writer.Write(message.D);
                writer.Write(message.X);
                writer.Write(message.Y);
                WriteString(writer, message.Text);
                if (message.Data == null)
                {
                    writer.Write(-1);
                }
                else
                {
                    writer.Write(message.Data.Length);
                    writer.Write(message.Data);
                }
                if (message.Words == null)
                {
                    writer.Write(-1);
                }
                else
                {
                    writer.Write(message.Words.Length);
                    foreach (string word in message.Words)
                    {
                        WriteString(writer, word);
                    }
                }
                if (message.Numbers == null)
                {
                    writer.Write(-1);
                }
                else
                {
                    writer.Write(message.Numbers.Length);
                    foreach (int number in message.Numbers)
                    {
                        writer.Write(number);
                    }
                }
                writer.Flush();
                return stream.ToArray();
            }
        }

        /// <summary>Reads a message. Throws <see cref="InvalidDataException"/> if the bytes are not one.</summary>
        public static NetMessage Decode(byte[] bytes)
        {
            try
            {
                using (var stream = new MemoryStream(bytes))
                using (var reader = new BinaryReader(stream))
                {
                    var message = new NetMessage();
                    message.Kind = (NetKind)reader.ReadByte();
                    message.A = reader.ReadInt32();
                    message.B = reader.ReadInt32();
                    message.C = reader.ReadInt32();
                    message.D = reader.ReadInt32();
                    message.X = reader.ReadSingle();
                    message.Y = reader.ReadSingle();
                    message.Text = ReadString(reader);
                    int dataLength = reader.ReadInt32();
                    if (dataLength >= 0)
                    {
                        message.Data = ReadBytes(reader, dataLength);
                    }
                    int wordCount = reader.ReadInt32();
                    if (wordCount >= 0)
                    {
                        CheckCount(reader, wordCount, 1);
                        message.Words = new string[wordCount];
                        for (int i = 0; i < wordCount; i++)
                        {
                            message.Words[i] = ReadString(reader);
                        }
                    }
                    int numberCount = reader.ReadInt32();
                    if (numberCount >= 0)
                    {
                        CheckCount(reader, numberCount, 4);
                        message.Numbers = new int[numberCount];
                        for (int i = 0; i < numberCount; i++)
                        {
                            message.Numbers[i] = reader.ReadInt32();
                        }
                    }
                    return message;
                }
            }
            catch (EndOfStreamException e)
            {
                throw new InvalidDataException("Truncated message", e);
            }
        }

        private static void WriteString(BinaryWriter writer, string value)
        {
            writer.Write(value != null);
            if (value != null)
            {
                writer.Write(value);
            }
        }

        private static string ReadString(BinaryReader reader)
        {
            return reader.ReadBoolean() ? reader.ReadString() : null;
        }

        private static byte[] ReadBytes(BinaryReader reader, int length)
        {
            CheckCount(reader, length, 1);
            byte[] bytes = reader.ReadBytes(length);
            if (bytes.Length != length)
            {
                throw new InvalidDataException("Truncated message");
            }
            return bytes;
        }

        // A bad length must not make us allocate gigabytes: it cannot exceed what is left to read.
        private static void CheckCount(BinaryReader reader, int count, int bytesEach)
        {
            long remaining = reader.BaseStream.Length - reader.BaseStream.Position;
            if (count < 0 || (long)count * bytesEach > remaining)
            {
                throw new InvalidDataException("Bad length in message");
            }
        }
    }
}
