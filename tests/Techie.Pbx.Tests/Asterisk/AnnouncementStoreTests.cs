using System.ComponentModel;
using System.Diagnostics;
using System.Text;
using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Where announcement audio lives and how it gets there (D55). The security half of this is
    /// that nothing from the browser reaches a path: the directory is an announcement ID and the
    /// file is derived from the announcement's name, and both are checked again on the way in.
    ///
    /// The tests that need a real ffmpeg say so and skip when it is not installed; everything
    /// else — the checks, the refusals and the "ffmpeg is missing" message itself — runs anywhere.
    /// </summary>
    public class AnnouncementStoreTests : IDisposable
    {
        /// <summary>A program name nothing will ever resolve, for the "ffmpeg is missing" path.</summary>
        private const string MissingProgram = "tnpbx-no-such-converter";

        private readonly string directory = Directory.CreateTempSubdirectory("tnpbx-sounds-").FullName;
        private readonly string soundsPath;
        private readonly AnnouncementStore store;

        public AnnouncementStoreTests()
        {
            this.soundsPath = Path.Combine(this.directory, "tnpbx");
            this.store = new AnnouncementStore(this.soundsPath, new AudioConverter(MissingProgram, 30));
        }

        public void Dispose() => Directory.Delete(this.directory, recursive: true);

        private static Announcement Sample(long id = 1, string name = "Welcome message") => new()
        {
            AnnouncementID = id,
            Name = name,
            AudioFile = Announcement.FileNameFor(name),
        };

        /// <summary>Whether this machine has the ffmpeg the real conversion needs.</summary>
        private static bool FfmpegIsInstalled()
        {
            try
            {
                using var process = Process.Start(new ProcessStartInfo
                {
                    FileName = AudioConverter.DefaultProgram,
                    ArgumentList = { "-version" },
                    CreateNoWindow = true,
                    RedirectStandardError = true,
                    RedirectStandardOutput = true,
                    UseShellExecute = false,
                });

                process?.WaitForExit(10_000);
                return process != null;
            }
            catch (Exception ex) when (ex is Win32Exception or FileNotFoundException)
            {
                return false;
            }
        }

        /// <summary>A real, if very short, 16-bit 8 kHz mono WAV: header then a little silence.</summary>
        private static byte[] SampleWav(int samples = 8000)
        {
            var data = samples * 2;
            var wav = new MemoryStream();
            var writer = new BinaryWriter(wav, Encoding.ASCII);

            writer.Write(Encoding.ASCII.GetBytes("RIFF"));
            writer.Write(36 + data);
            writer.Write(Encoding.ASCII.GetBytes("WAVEfmt "));
            writer.Write(16);                       // PCM header length
            writer.Write((short)1);                 // PCM
            writer.Write((short)1);                 // mono
            writer.Write(8000);                     // sample rate
            writer.Write(16000);                    // bytes per second
            writer.Write((short)2);                 // block align
            writer.Write((short)16);                // bits per sample
            writer.Write(Encoding.ASCII.GetBytes("data"));
            writer.Write(data);
            writer.Write(new byte[data]);
            writer.Flush();

            return wav.ToArray();
        }

        /// <summary>Puts a file where the store would have put one, without needing to convert.</summary>
        private string PlaceAudio(Announcement announcement, byte[]? content = null)
        {
            var path = this.store.PathFor(announcement.AnnouncementID, announcement.AudioFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            File.WriteAllBytes(path, content ?? SampleWav());

            return path;
        }

        [Fact]
        public void The_path_is_the_announcement_id_then_the_derived_file_name()
        {
            var path = this.store.PathFor(7, "welcome-message.wav");

            Assert.Equal(
                Path.Combine(this.soundsPath, AnnouncementStore.SubDirectory, "7", "welcome-message.wav"),
                path);
        }

        /// <summary>
        /// The last word on where a file may go. Nothing builds a path out of a browser-supplied
        /// name today, and this is what makes that still true the day something tries.
        /// </summary>
        [Theory]
        [InlineData("../escape.wav")]
        [InlineData("../../../etc/cron.d/evil.wav")]
        [InlineData("/etc/passwd")]
        [InlineData("welcome message.wav")]
        [InlineData("welcome.mp3")]
        [InlineData("")]
        public void A_file_name_we_would_not_have_written_never_reaches_a_path(string fileName)
        {
            Assert.Throws<InvalidOperationException>(() => this.store.PathFor(1, fileName));
        }

        [Fact]
        public void An_announcement_that_has_not_been_saved_yet_has_nowhere_to_put_audio()
        {
            Assert.Throws<InvalidOperationException>(() => this.store.PathFor(0, "welcome-message.wav"));
        }

        [Fact]
        public void The_playback_name_is_the_prefix_the_id_and_the_file_without_its_extension()
        {
            Assert.Equal(
                "tnpbx/announcements/1/welcome-message",
                AnnouncementStore.PlaybackName(Sample()));
        }

        [Fact]
        public void Describe_answers_nothing_when_there_is_no_audio()
        {
            Assert.Null(this.store.Describe(new Announcement { AnnouncementID = 1, Name = "Welcome" }));
        }

        /// <summary>
        /// A row and a file can drift apart — a failed write, a restored backup — and the UI has to
        /// be able to say so rather than showing a length for nothing.
        /// </summary>
        [Fact]
        public void Describe_answers_nothing_when_the_row_names_a_file_that_is_not_there()
        {
            Assert.Null(this.store.Describe(Sample()));
        }

        [Fact]
        public void Describe_reads_the_size_and_length_off_the_file()
        {
            var announcement = Sample();
            PlaceAudio(announcement);

            var audio = this.store.Describe(announcement)!;

            Assert.Equal("welcome-message.wav", audio.FileName);
            Assert.Equal(16044, audio.Bytes);

            // 8000 samples of 16-bit mono at 8 kHz is one second.
            Assert.Equal(1.0, audio.Seconds, 3);
        }

        [Fact]
        public void Renaming_an_announcement_renames_its_file()
        {
            var announcement = Sample();
            var before = PlaceAudio(announcement);

            this.store.Rename(announcement.AnnouncementID, announcement.AudioFile, "holiday-closure.wav");

            Assert.False(File.Exists(before));
            Assert.True(File.Exists(this.store.PathFor(1, "holiday-closure.wav")));
        }

        [Fact]
        public void Renaming_to_the_same_name_does_nothing()
        {
            var announcement = Sample();
            PlaceAudio(announcement);

            this.store.Rename(announcement.AnnouncementID, announcement.AudioFile, announcement.AudioFile);

            Assert.True(File.Exists(this.store.PathFor(1, announcement.AudioFile)));
        }

        [Fact]
        public void Deleting_an_announcement_takes_its_directory_with_it()
        {
            var announcement = Sample();
            var path = PlaceAudio(announcement);

            this.store.Delete(announcement);

            Assert.False(File.Exists(path));
            Assert.False(Directory.Exists(Path.GetDirectoryName(path)));
        }

        [Fact]
        public void Deleting_an_announcement_with_no_audio_is_not_an_error()
        {
            this.store.Delete(Sample(id: 42));
        }

        /// <summary>
        /// The one thing that must never happen quietly: no ffmpeg, no conversion, and the file is
        /// not stored as it arrived. The message names ffmpeg and how to install it (D55).
        /// </summary>
        [Fact]
        public void An_upload_fails_by_name_when_ffmpeg_is_not_installed()
        {
            using var upload = new MemoryStream(SampleWav());

            var ex = Assert.Throws<AudioUploadException>(() => this.store.Save(Sample(), upload));

            Assert.Contains("ffmpeg", ex.Message);
            Assert.Contains("apt install ffmpeg", ex.Message);
            Assert.False(Directory.Exists(Path.Combine(this.soundsPath, AnnouncementStore.SubDirectory, "1")));
        }

        /// <summary>
        /// The format check runs before the converter does, which is what keeps an arbitrary file
        /// from ever being handed to ffmpeg. The converter here cannot start at all, so a message
        /// about the format rather than about ffmpeg is the proof of the ordering.
        /// </summary>
        [Fact]
        public void An_upload_in_a_format_we_do_not_accept_is_refused_before_ffmpeg_runs()
        {
            using var upload = new MemoryStream(Encoding.ASCII.GetBytes(new string('x', 4096)));

            var ex = Assert.Throws<AudioUploadException>(() => this.store.Save(Sample(), upload));

            Assert.Contains("not an audio file this system recognises", ex.Message);
            Assert.DoesNotContain("ffmpeg", ex.Message);
        }

        [Fact]
        public void An_empty_upload_is_refused()
        {
            using var upload = new MemoryStream();

            var ex = Assert.Throws<AudioUploadException>(() => this.store.Save(Sample(), upload));

            Assert.Contains("empty", ex.Message);
        }

        [Fact]
        public void An_upload_over_the_cap_is_refused()
        {
            using var upload = new EndlessStream();

            var ex = Assert.Throws<AudioUploadException>(() => this.store.Save(Sample(), upload));

            Assert.Contains("20 MB", ex.Message);
        }

        /// <summary>
        /// The real thing, when the machine has ffmpeg: whatever went in comes out as one 8 kHz
        /// mono WAV, in the announcement's own directory, under the name we derived.
        /// </summary>
        [Fact]
        public void A_real_conversion_stores_one_wav_named_after_the_announcement()
        {
            // There is nothing to assert without the converter this test is about. A build machine
            // may not have ffmpeg; the server does, because the installer requires it (D55), and
            // the "it is missing" path is covered by its own test above either way.
            if (!FfmpegIsInstalled())
                return;

            var converting = new AnnouncementStore(this.soundsPath);
            var announcement = Sample();
            using var upload = new MemoryStream(SampleWav());

            var fileName = converting.Save(announcement, upload);

            Assert.Equal("welcome-message.wav", fileName);

            var audio = converting.Describe(announcement)!;
            Assert.Equal(1.0, audio.Seconds, 1);

            // One announcement, one file: nothing else is left in the directory.
            var stored = Directory.GetFiles(Path.Combine(this.soundsPath, AnnouncementStore.SubDirectory, "1"));
            Assert.Equal(new[] { converting.PathFor(1, fileName) }, stored);
        }

        /// <summary>Replacing the audio, or renaming in the same save, leaves nothing behind.</summary>
        [Fact]
        public void A_replacement_leaves_only_the_new_file()
        {
            // There is nothing to assert without the converter this test is about. A build machine
            // may not have ffmpeg; the server does, because the installer requires it (D55), and
            // the "it is missing" path is covered by its own test above either way.
            if (!FfmpegIsInstalled())
                return;

            var converting = new AnnouncementStore(this.soundsPath);
            var announcement = Sample();

            using (var first = new MemoryStream(SampleWav()))
                converting.Save(announcement, first);

            announcement.Name = "Holiday closure";
            announcement.AudioFile = Announcement.FileNameFor(announcement.Name);

            using (var second = new MemoryStream(SampleWav(4000)))
                converting.Save(announcement, second);

            var stored = Directory.GetFiles(Path.Combine(this.soundsPath, AnnouncementStore.SubDirectory, "1"))
                .Select(Path.GetFileName)
                .ToList();

            Assert.Equal(new[] { "holiday-closure.wav" }, stored);
        }

        /// <summary>A stream that never ends, to run the size cap into without allocating 20 MB.</summary>
        private sealed class EndlessStream : Stream
        {
            public override bool CanRead => true;
            public override bool CanSeek => false;
            public override bool CanWrite => false;
            public override long Length => throw new NotSupportedException();
            public override long Position { get => 0; set => throw new NotSupportedException(); }

            public override void Flush()
            {
            }

            public override int Read(byte[] buffer, int offset, int count)
            {
                // Looks like an Ogg, so the size cap is what stops it rather than the signature.
                if (offset == 0 && count >= 4)
                    Encoding.ASCII.GetBytes("OggS").CopyTo(buffer, 0);

                return count;
            }

            public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
            public override void SetLength(long value) => throw new NotSupportedException();
            public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
        }
    }
}
