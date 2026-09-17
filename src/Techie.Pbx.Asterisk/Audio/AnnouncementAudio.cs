namespace Techie.Pbx.Asterisk.Audio
{
    /// <summary>
    /// What is actually on disk for one announcement. Read from the file rather than stored in the
    /// database, so the table can never show a size or a length for audio that is not there (D55).
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
