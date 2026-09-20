namespace Techie.Pbx.Asterisk.Audio
{
    /// <summary>
    /// What is actually on disk for one piece of stored audio: an announcement (D55) or a music on
    /// hold track (D119) — both are the same converted WAV, so both are described the same way.
    /// Read from the file rather than stored in the database, so a table can never show a size or
    /// a length for audio that is not there.
    /// </summary>
    public class AnnouncementAudio
    {
        /// <summary>
        /// The WAV header <see cref="AudioConverter"/> produces: RIFF, fmt and data, and no
        /// metadata chunk because the conversion strips tags. Used to turn a file size into a
        /// length, which is close enough to display and costs nothing to work out.
        /// </summary>
        private const int HeaderBytes = 44;

        public long Bytes { get; set; }

        public string FileName { get; set; } = "";

        /// <summary>
        /// Roughly how long the message runs, from the size: one channel of 16-bit samples at
        /// 8 kHz is a fixed 16,000 bytes a second.
        /// </summary>
        public double Seconds =>
            Math.Max(0, this.Bytes - HeaderBytes) / (double)(AudioConverter.SampleRateHz * 2 * AudioConverter.Channels);
    }
}
