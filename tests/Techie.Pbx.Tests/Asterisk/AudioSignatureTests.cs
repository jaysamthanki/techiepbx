using System.Text;
using Techie.Pbx.Asterisk.Audio;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// What an upload actually is, decided from its first bytes rather than from its name (D55).
    /// The name is attacker-controlled and ffmpeg is a large program to hand an arbitrary file to,
    /// so this is the gate that runs first.
    /// </summary>
    public class AudioSignatureTests
    {
        /// <summary>
        /// A header of the right length, with the given bytes at the front. Real files are longer;
        /// the signature only ever looks at the first few.
        /// </summary>
        private static byte[] Header(params byte[] start)
        {
            var header = new byte[AudioSignature.HeaderBytes];
            start.CopyTo(header, 0);
            return header;
        }

        /// <summary>
        /// A header of the right length starting with this text. Longer text is cut to fit, since
        /// only the first few bytes are ever looked at.
        /// </summary>
        private static byte[] Ascii(string text, int offset = 0)
        {
            var header = new byte[AudioSignature.HeaderBytes];
            var bytes = Encoding.ASCII.GetBytes(text).AsSpan();

            bytes[..Math.Min(bytes.Length, header.Length - offset)].CopyTo(header.AsSpan(offset));
            return header;
        }

        [Fact]
        public void A_wav_is_riff_then_wave()
        {
            var header = Ascii("RIFF");
            Encoding.ASCII.GetBytes("WAVE").CopyTo(header, 8);

            Assert.Equal(AudioContainer.Wav, AudioSignature.Detect(header));
        }

        /// <summary>RIFF on its own is a container, not necessarily audio: AVI starts the same way.</summary>
        [Fact]
        public void Riff_without_wave_is_not_accepted()
        {
            var header = Ascii("RIFF");
            Encoding.ASCII.GetBytes("AVI ").CopyTo(header, 8);

            Assert.Null(AudioSignature.Detect(header));
        }

        [Fact]
        public void An_ogg_is_oggs()
        {
            Assert.Equal(AudioContainer.Ogg, AudioSignature.Detect(Ascii("OggS")));
        }

        /// <summary>What Chrome and Firefox hand back from MediaRecorder.</summary>
        [Fact]
        public void A_webm_is_the_ebml_header()
        {
            Assert.Equal(AudioContainer.WebM, AudioSignature.Detect(Header(0x1A, 0x45, 0xDF, 0xA3)));
        }

        /// <summary>What Safari and iPhones hand back, and what an iPhone clip off the camera is.</summary>
        [Fact]
        public void An_mp4_is_ftyp_after_the_box_size()
        {
            Assert.Equal(AudioContainer.Mp4, AudioSignature.Detect(Ascii("ftyp", offset: 4)));
        }

        [Fact]
        public void An_mp3_is_an_id3_tag_or_a_frame_sync()
        {
            Assert.Equal(AudioContainer.Mp3, AudioSignature.Detect(Ascii("ID3")));
            Assert.Equal(AudioContainer.Mp3, AudioSignature.Detect(Header(0xFF, 0xFB)));
        }

        [Theory]
        [InlineData("This is a text file, not audio at all.")]
        [InlineData("%PDF-1.7")]
        [InlineData("ELF")]
        [InlineData("<?xml version=\"1.0\"?>")]
        public void Anything_else_is_refused(string text)
        {
            Assert.Null(AudioSignature.Detect(Ascii(text)));
        }

        /// <summary>A file too short to have a signature cannot be one we accept.</summary>
        [Fact]
        public void A_file_shorter_than_a_header_is_refused()
        {
            Assert.Null(AudioSignature.Detect(Array.Empty<byte>()));
            Assert.Null(AudioSignature.Detect(Encoding.ASCII.GetBytes("RIFF")));
        }

        /// <summary>The caller still has to copy the whole upload afterwards.</summary>
        [Fact]
        public void Sniffing_a_stream_leaves_the_position_where_it_found_it()
        {
            var header = Ascii("OggS");
            using var stream = new MemoryStream(header.Concat(new byte[1000]).ToArray());
            stream.Position = 0;

            Assert.Equal(AudioContainer.Ogg, AudioSignature.Detect(stream));
            Assert.Equal(0, stream.Position);
        }

        [Fact]
        public void A_stream_that_cannot_be_sniffed_is_an_error_rather_than_a_guess()
        {
            using var stream = new UnseekableStream();

            Assert.Throws<ArgumentException>(() => AudioSignature.Detect(stream));
        }

        [Fact]
        public void Every_container_has_a_name_worth_showing()
        {
            foreach (var container in Enum.GetValues<AudioContainer>())
                Assert.False(string.IsNullOrWhiteSpace(AudioSignature.Describe(container)));
        }

        /// <summary>The file picker's filter names every container the server accepts.</summary>
        [Fact]
        public void The_accept_attribute_covers_the_formats_the_form_promises()
        {
            foreach (var extension in new[] { ".mp3", ".mp4", ".m4a", ".wav", ".webm", ".ogg" })
                Assert.Contains(extension, AudioSignature.AcceptAttribute);
        }

        private sealed class UnseekableStream : MemoryStream
        {
            public override bool CanSeek => false;
        }
    }
}
