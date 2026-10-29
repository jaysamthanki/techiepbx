using Techie.Pbx.Core.Ai;

namespace Techie.Pbx.Tests.Ai
{
    /// <summary>The AudioSocket wire format (D200): kind, big-endian length, payload.</summary>
    public class AudioSocketProtocolTests
    {
        [Fact]
        public async Task An_audio_frame_round_trips()
        {
            var samples = new short[] { 0, 1, -1, short.MaxValue, short.MinValue, 1234 };
            using var stream = new MemoryStream();

            await AudioSocketProtocol.WriteAudioAsync(stream, samples, CancellationToken.None);
            stream.Position = 0;
            var frame = await AudioSocketProtocol.ReadFrameAsync(stream, CancellationToken.None);

            Assert.NotNull(frame);
            Assert.Equal(AudioSocketProtocol.Kinds.Audio, frame.Kind);
            Assert.Equal(samples, AudioSocketProtocol.Samples(frame.Payload));
        }

        [Fact]
        public async Task The_length_is_big_endian_and_samples_are_little_endian()
        {
            using var stream = new MemoryStream();

            await AudioSocketProtocol.WriteAudioAsync(stream, new short[160], CancellationToken.None);
            var bytes = stream.ToArray();

            Assert.Equal(3 + 320, bytes.Length);
            Assert.Equal(new byte[] { 0x10, 0x01, 0x40 }, bytes[..3]);

            stream.SetLength(0);
            await AudioSocketProtocol.WriteAudioAsync(stream, new short[] { 0x0102 }, CancellationToken.None);
            Assert.Equal(new byte[] { 0x10, 0x00, 0x02, 0x02, 0x01 }, stream.ToArray());
        }

        [Fact]
        public async Task A_uuid_frame_is_read_with_its_sixteen_bytes()
        {
            var uuid = Enumerable.Range(1, 16).Select(i => (byte)i).ToArray();
            using var stream = new MemoryStream(new byte[] { 0x01, 0x00, 0x10 }.Concat(uuid).ToArray());

            var frame = await AudioSocketProtocol.ReadFrameAsync(stream, CancellationToken.None);

            Assert.NotNull(frame);
            Assert.Equal(AudioSocketProtocol.Kinds.Uuid, frame.Kind);
            Assert.Equal(uuid, frame.Payload);
        }

        [Fact]
        public async Task A_hangup_has_no_payload()
        {
            using var stream = new MemoryStream();

            await AudioSocketProtocol.WriteHangupAsync(stream, CancellationToken.None);
            Assert.Equal(new byte[] { 0x00, 0x00, 0x00 }, stream.ToArray());

            stream.Position = 0;
            var frame = await AudioSocketProtocol.ReadFrameAsync(stream, CancellationToken.None);

            Assert.NotNull(frame);
            Assert.Equal(AudioSocketProtocol.Kinds.Hangup, frame.Kind);
            Assert.Empty(frame.Payload);
        }

        [Fact]
        public async Task A_length_over_the_cap_is_refused_before_anything_is_allocated()
        {
            using var stream = new MemoryStream(new byte[] { 0x10, 0xFF, 0xFF });

            await Assert.ThrowsAsync<InvalidDataException>(() =>
                AudioSocketProtocol.ReadFrameAsync(stream, CancellationToken.None));
        }

        [Fact]
        public async Task The_end_of_the_stream_is_null()
        {
            using var stream = new MemoryStream();

            Assert.Null(await AudioSocketProtocol.ReadFrameAsync(stream, CancellationToken.None));
        }

        [Fact]
        public async Task A_stream_that_ends_inside_a_frame_is_null()
        {
            using var stream = new MemoryStream(new byte[] { 0x10, 0x01, 0x40, 0x00, 0x00 });

            Assert.Null(await AudioSocketProtocol.ReadFrameAsync(stream, CancellationToken.None));
        }
    }
}
