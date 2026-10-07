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
using System.IO.Compression;

namespace Telewheel
{
    /// <summary>
    /// The frame around every message on a real connection: what kind of frame it is, where it falls
    /// in the sender's sequence, who the sender says it is and the secret that proves it, and the
    /// payload, compressed when that helps. Whatever arrives from the network is untrusted, so
    /// unpacking never throws and never expands past a fixed size.
    /// </summary>
    public static class Envelope
    {
        /// <summary>A <see cref="NetMessage"/> encoded with <see cref="NetCodec"/>.</summary>
        public const byte KindMessage = 1;

        /// <summary>The host saying "I am the host, and this is your secret" to a new arrival (no payload).</summary>
        public const byte KindHostHello = 2;

        /// <summary>Payloads bigger than this are compressed (when it makes them smaller).</summary>
        public const int CompressAbove = 1024;

        /// <summary>The most a payload may be once unpacked. Drawings are well under this.</summary>
        public const int MaxPayloadBytes = 8 * 1024 * 1024;

        private const byte FlagCompressed = 1;

        /// <summary>kind, flags, sequence (4), sender (4), token (8).</summary>
        public const int HeaderBytes = 18;

        public static byte[] Pack(byte kind, uint sequence, int sender, ulong token, byte[] payload)
        {
            byte[] body = payload ?? new byte[0];
            byte flags = 0;
            if (body.Length > CompressAbove)
            {
                byte[] packed = Compress(body);
                if (packed != null && packed.Length < body.Length)
                {
                    body = packed;
                    flags = FlagCompressed;
                }
            }
            var frame = new byte[HeaderBytes + body.Length];
            frame[0] = kind;
            frame[1] = flags;
            WriteUInt32(frame, 2, sequence);
            WriteUInt32(frame, 6, unchecked((uint)sender));
            WriteUInt32(frame, 10, (uint)token);
            WriteUInt32(frame, 14, (uint)(token >> 32));
            Buffer.BlockCopy(body, 0, frame, HeaderBytes, body.Length);
            return frame;
        }

        /// <summary>Opens a frame. Returns false for anything that is not a well-formed frame.</summary>
        public static bool TryUnpack(
            byte[] frame, out byte kind, out uint sequence, out int sender, out ulong token, out byte[] payload)
        {
            kind = 0;
            sequence = 0;
            sender = 0;
            token = 0;
            payload = null;
            if (frame == null || frame.Length < HeaderBytes)
            {
                return false;
            }
            kind = frame[0];
            byte flags = frame[1];
            if ((kind != KindMessage && kind != KindHostHello) || (flags & ~FlagCompressed) != 0)
            {
                return false;
            }
            sequence = ReadUInt32(frame, 2);
            sender = unchecked((int)ReadUInt32(frame, 6));
            token = ReadUInt32(frame, 10) | ((ulong)ReadUInt32(frame, 14) << 32);
            int length = frame.Length - HeaderBytes;
            if ((flags & FlagCompressed) != 0)
            {
                payload = Decompress(frame, HeaderBytes, length);
                return payload != null;
            }
            if (length > MaxPayloadBytes)
            {
                return false;
            }
            payload = new byte[length];
            Buffer.BlockCopy(frame, HeaderBytes, payload, 0, length);
            return true;
        }

        private static void WriteUInt32(byte[] buffer, int offset, uint value)
        {
            buffer[offset] = (byte)value;
            buffer[offset + 1] = (byte)(value >> 8);
            buffer[offset + 2] = (byte)(value >> 16);
            buffer[offset + 3] = (byte)(value >> 24);
        }

        private static uint ReadUInt32(byte[] buffer, int offset)
        {
            return (uint)(buffer[offset] | (buffer[offset + 1] << 8) | (buffer[offset + 2] << 16) | (buffer[offset + 3] << 24));
        }

        private static byte[] Compress(byte[] data)
        {
            try
            {
                using (var output = new MemoryStream())
                {
                    using (var brotli = new BrotliStream(output, CompressionLevel.Fastest, true))
                    {
                        brotli.Write(data, 0, data.Length);
                    }
                    return output.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        // Stops at the size cap, so a tiny frame cannot unpack into gigabytes.
        private static byte[] Decompress(byte[] frame, int offset, int length)
        {
            try
            {
                using (var input = new MemoryStream(frame, offset, length))
                using (var brotli = new BrotliStream(input, CompressionMode.Decompress))
                using (var output = new MemoryStream())
                {
                    var buffer = new byte[16 * 1024];
                    int read;
                    while ((read = brotli.Read(buffer, 0, buffer.Length)) > 0)
                    {
                        if (output.Length + read > MaxPayloadBytes)
                        {
                            return null;
                        }
                        output.Write(buffer, 0, read);
                    }
                    return output.ToArray();
                }
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
