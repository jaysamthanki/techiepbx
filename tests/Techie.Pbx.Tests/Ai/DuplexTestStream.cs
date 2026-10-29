using System.Threading.Channels;
using Techie.Pbx.Core.Ai;

namespace Techie.Pbx.Tests.Ai
{
    /// <summary>
    /// An in-memory stand-in for an AudioSocket connection: the test queues what "Asterisk" sends,
    /// and everything the session writes is kept for the test to read back. Reads and writes may
    /// happen at the same time, as they do on a real socket.
    /// </summary>
    internal sealed class DuplexTestStream : Stream
    {
        private readonly Channel<byte[]> inbound = Channel.CreateUnbounded<byte[]>();
        private readonly MemoryStream outbound = new();

        private byte[] current = [];
        private int currentOffset;

        public override bool CanRead => true;

        public override bool CanSeek => false;

        public override bool CanWrite => true;

        public bool IsDisposed { get; private set; }

        public override long Length => throw new NotSupportedException();

        public override long Position
        {
            get => throw new NotSupportedException();
            set => throw new NotSupportedException();
        }

        /// <summary>Every frame the session has written so far.</summary>
        public List<AudioSocketFrame> WrittenFrames
        {
            get
            {
                var frames = new List<AudioSocketFrame>();
                using var stream = new MemoryStream(this.Written);

                while (AudioSocketProtocol.ReadFrameAsync(stream, CancellationToken.None).GetAwaiter().GetResult() is { } frame)
                    frames.Add(frame);

                return frames;
            }
        }

        public byte[] Written
        {
            get
            {
                lock (this.outbound)
                    return this.outbound.ToArray();
            }
        }

        /// <summary>The far end closes: reads see the end of the stream once the queue is empty.</summary>
        public void CompleteInbound() => this.inbound.Writer.TryComplete();

        public override void Flush()
        {
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            this.ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override async ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (this.currentOffset == this.current.Length)
            {
                if (!await this.inbound.Reader.WaitToReadAsync(cancellationToken) || !this.inbound.Reader.TryRead(out var next))
                    return 0;

                this.current = next;
                this.currentOffset = 0;
            }

            var count = Math.Min(buffer.Length, this.current.Length - this.currentOffset);
            this.current.AsMemory(this.currentOffset, count).CopyTo(buffer);
            this.currentOffset += count;

            return count;
        }

        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();

        /// <summary>Queues samples as 20 ms audio frames.</summary>
        public void SendAudio(short[] samples)
        {
            for (var offset = 0; offset < samples.Length; offset += Endpointer.WindowSamples)
            {
                using var frame = new MemoryStream();
                var length = Math.Min(Endpointer.WindowSamples, samples.Length - offset);

                AudioSocketProtocol.WriteAudioAsync(frame, samples.AsSpan(offset, length), CancellationToken.None).GetAwaiter().GetResult();
                this.inbound.Writer.TryWrite(frame.ToArray());
            }
        }

        public void SendHangup() => this.inbound.Writer.TryWrite(new byte[] { AudioSocketProtocol.Kinds.Hangup, 0, 0 });

        public override void SetLength(long value) => throw new NotSupportedException();

        /// <summary>Waits until the session has written at least this many frames.</summary>
        public async Task WaitForFramesAsync(int count)
        {
            var deadline = DateTime.UtcNow.AddSeconds(10);

            while (this.WrittenFrames.Count < count)
            {
                if (DateTime.UtcNow > deadline)
                    throw new TimeoutException($"Only {this.WrittenFrames.Count} of {count} frames were written.");

                await Task.Delay(5);
            }
        }

        public override void Write(byte[] buffer, int offset, int count)
        {
            if (this.IsDisposed)
                throw new IOException("The stream is closed.");

            lock (this.outbound)
                this.outbound.Write(buffer, offset, count);
        }

        public override ValueTask WriteAsync(ReadOnlyMemory<byte> buffer, CancellationToken cancellationToken = default)
        {
            this.Write(buffer.ToArray(), 0, buffer.Length);
            return ValueTask.CompletedTask;
        }

        protected override void Dispose(bool disposing)
        {
            this.IsDisposed = true;
            this.inbound.Writer.TryComplete();
            base.Dispose(disposing);
        }
    }
}
