using System.Buffers.Binary;

namespace Techie.Pbx.Core.Ai
{
    /// <summary>
    /// Asterisk's AudioSocket wire format (D199, D200): one byte of kind, a two-byte big-endian
    /// payload length, then the payload. Asterisk sends the call's UUID first, then 20 ms of
    /// 16-bit signed linear 8 kHz mono audio at a time; the media service answers with audio in
    /// the same format. Samples are little-endian, the byte order Asterisk's signed linear uses on
    /// the machines it runs on here.
    /// </summary>
    public static class AudioSocketProtocol
    {
        /// <summary>
        /// The longest payload accepted. A 20 ms frame is 320 bytes at 8 kHz and 7,680 at the
        /// highest rate AudioSocket has (192 kHz), so 16 KB is generous; anything longer is a peer
        /// that is not speaking AudioSocket, and is not worth allocating for.
        /// </summary>
        public const int MaxPayloadLength = 16 * 1024;

        private const int HeaderLength = 3;

        /// <summary>
        /// Reads the next frame, or returns null at the end of the stream — including a stream that
        /// ends part-way through a frame, which is a caller gone as surely as a clean close. Throws
        /// <see cref="InvalidDataException"/> for a length over <see cref="MaxPayloadLength"/>.
        /// </summary>
        public static async Task<AudioSocketFrame?> ReadFrameAsync(Stream stream, CancellationToken cancellationToken)
        {
            var header = new byte[HeaderLength];

            if (await stream.ReadAtLeastAsync(header, HeaderLength, throwOnEndOfStream: false, cancellationToken) < HeaderLength)
                return null;

            var length = BinaryPrimitives.ReadUInt16BigEndian(header.AsSpan(1));

            if (length > MaxPayloadLength)
                throw new InvalidDataException($"AudioSocket frame of {length} bytes is over the {MaxPayloadLength} byte limit.");

            var payload = new byte[length];

            if (length > 0 && await stream.ReadAtLeastAsync(payload, length, throwOnEndOfStream: false, cancellationToken) < length)
                return null;

            return new AudioSocketFrame(header[0], payload);
        }

        /// <summary>The samples in an audio frame's payload. An odd trailing byte is not a sample and is dropped.</summary>
        public static short[] Samples(byte[] payload)
        {
            var samples = new short[payload.Length / 2];

            for (var i = 0; i < samples.Length; i++)
                samples[i] = BinaryPrimitives.ReadInt16LittleEndian(payload.AsSpan(i * 2));

            return samples;
        }

        /// <summary>Writes the samples as one <see cref="Kinds.Audio"/> frame.</summary>
        public static Task WriteAudioAsync(Stream stream, ReadOnlySpan<short> samples, CancellationToken cancellationToken)
        {
            var frame = new byte[HeaderLength + samples.Length * 2];

            frame[0] = Kinds.Audio;
            BinaryPrimitives.WriteUInt16BigEndian(frame.AsSpan(1), checked((ushort)(samples.Length * 2)));

            for (var i = 0; i < samples.Length; i++)
                BinaryPrimitives.WriteInt16LittleEndian(frame.AsSpan(HeaderLength + i * 2), samples[i]);

            return stream.WriteAsync(frame, cancellationToken).AsTask();
        }

        /// <summary>Writes a hangup frame, which has no payload.</summary>
        public static Task WriteHangupAsync(Stream stream, CancellationToken cancellationToken) =>
            stream.WriteAsync(new byte[] { Kinds.Hangup, 0, 0 }, cancellationToken).AsTask();

        /// <summary>The frame kinds this service understands, as verified in Asterisk 22's res_audiosocket.h.</summary>
        public static class Kinds
        {
            /// <summary>16-bit signed linear, 8 kHz, mono: the only audio rate this service speaks.</summary>
            public const byte Audio = 0x10;

            /// <summary>Asterisk reports an error. Nothing to act on, so it is logged and ignored.</summary>
            public const byte Error = 0xFF;

            /// <summary>The call is over. No payload.</summary>
            public const byte Hangup = 0x00;

            /// <summary>The call's UUID, 16 raw bytes, always the first frame.</summary>
            public const byte Uuid = 0x01;
        }
    }
}
