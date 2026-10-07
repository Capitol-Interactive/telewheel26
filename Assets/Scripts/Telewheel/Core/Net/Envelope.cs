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
    /// in the sender's sequence, and the payload, compressed when that helps. Whatever arrives from the
    /// network is untrusted, so unpacking never throws and never expands past a fixed size.
    /// </summary>
    public static class Envelope
    {
        /// <summary>A <see cref="NetMessage"/> encoded with <see cref="NetCodec"/>.</summary>
        public const byte KindMessage = 1;

        /// <summary>The host saying "I am the host" to a peer that has just arrived (no payload).</summary>
        public const byte KindHostHello = 2;

        /// <summary>Payloads bigger than this are compressed (when it makes them smaller).</summary>
        public const int CompressAbove = 1024;

        /// <summary>The most a payload may be once unpacked. Drawings are well under this.</summary>
        public const int MaxPayloadBytes = 8 * 1024 * 1024;

        private const byte FlagCompressed = 1;
        private const int HeaderBytes = 6;

        public static byte[] Pack(byte kind, uint sequence, byte[] payload)
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
            frame[2] = (byte)sequence;
            frame[3] = (byte)(sequence >> 8);
            frame[4] = (byte)(sequence >> 16);
            frame[5] = (byte)(sequence >> 24);
            Buffer.BlockCopy(body, 0, frame, HeaderBytes, body.Length);
            return frame;
        }

        /// <summary>Opens a frame. Returns false for anything that is not a well-formed frame.</summary>
        public static bool TryUnpack(byte[] frame, out byte kind, out uint sequence, out byte[] payload)
        {
            kind = 0;
            sequence = 0;
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
            sequence = (uint)(frame[2] | (frame[3] << 8) | (frame[4] << 16) | (frame[5] << 24));
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
