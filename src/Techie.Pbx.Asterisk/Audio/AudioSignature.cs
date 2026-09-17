namespace Techie.Pbx.Asterisk.Audio
{
    /// <summary>
    /// What an uploaded file actually is, read from its first bytes rather than from its name.
    /// A file called "greeting.wav" is whatever its contents say it is, and ffmpeg is a large
    /// program to hand an arbitrary file to, so anything unrecognised is refused before it runs
    /// (D55).
    ///
    /// This is a gate, not a parser: it answers "is this one of the five containers we accept",
    /// and ffmpeg does the real work of deciding whether the file is any good.
    /// </summary>
    public static class AudioSignature
    {
        /// <summary>How much of the file has to be read before the question can be answered.</summary>
        public const int HeaderBytes = 16;

        /// <summary>
        /// What the file picker should offer. Advisory only — the browser's filter is a
        /// convenience, and <see cref="Detect(Stream)"/> is what actually decides.
        /// </summary>
        public static string AcceptAttribute => ".mp3,.mp4,.m4a,.wav,.webm,.ogg,audio/*,video/mp4";

        /// <summary>The container's usual name, for a message an admin reads.</summary>
        public static string Describe(AudioContainer container) => container switch
        {
            AudioContainer.Mp3 => "MP3",
            AudioContainer.Mp4 => "MP4/M4A",
            AudioContainer.Ogg => "Ogg",
            AudioContainer.Wav => "WAV",
            AudioContainer.WebM => "WebM",
            _ => container.ToString(),
        };

        /// <summary>
        /// The container this file is in, or null for anything we do not accept. Pure function
        /// over the first bytes, so it can be given a header read off a stream.
        /// </summary>
        public static AudioContainer? Detect(ReadOnlySpan<byte> header)
        {
            if (header.Length < HeaderBytes)
                return null;

            // RIFF....WAVE
            if (Matches(header, 0, "RIFF") && Matches(header, 8, "WAVE"))
                return AudioContainer.Wav;

            if (Matches(header, 0, "OggS"))
                return AudioContainer.Ogg;

            // The EBML header Matroska and WebM share.
            if (header[0] == 0x1A && header[1] == 0x45 && header[2] == 0xDF && header[3] == 0xA3)
                return AudioContainer.WebM;

            // An ISO base media file: the box size, then "ftyp". .mp4, .m4a and .mov all land
            // here, which is what Safari and iPhones produce.
            if (Matches(header, 4, "ftyp"))
                return AudioContainer.Mp4;

            if (Matches(header, 0, "ID3"))
                return AudioContainer.Mp3;

            // A bare MPEG audio frame: eleven sync bits. Enough to tell an MP3 from junk; ffmpeg
            // decides whether it is a whole one.
            if (header[0] == 0xFF && (header[1] & 0xE0) == 0xE0)
                return AudioContainer.Mp3;

            return null;
        }

        /// <summary>
        /// Reads the header off the start of a seekable stream and leaves the position where it
        /// found it, so the caller can still copy the whole thing afterwards.
        /// </summary>
        public static AudioContainer? Detect(Stream stream)
        {
            if (!stream.CanSeek)
                throw new ArgumentException("The upload has to be seekable to be sniffed.", nameof(stream));

            var start = stream.Position;
            var header = new byte[HeaderBytes];
            var read = stream.ReadAtLeast(header, HeaderBytes, throwOnEndOfStream: false);
            stream.Position = start;

            return Detect(header.AsSpan(0, read));
        }

        private static bool Matches(ReadOnlySpan<byte> header, int offset, string ascii)
        {
            for (var i = 0; i < ascii.Length; i++)
            {
                if (header[offset + i] != (byte)ascii[i])
                    return false;
            }

            return true;
        }
    }
}
