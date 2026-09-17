namespace Techie.Pbx.Asterisk.Audio
{
    /// <summary>
    /// The upload containers this system recognises, which is the list browsers and phones
    /// actually produce: what a desktop user picks off disk, and what MediaRecorder hands back
    /// (WebM on Chrome and Firefox, MP4 on Safari and iPhones).
    ///
    /// The container is worked out from the file's first bytes, never from its name, and anything
    /// not on this list is refused before ffmpeg is ever started (D55).
    /// </summary>
    public enum AudioContainer
    {
        /// <summary>.mp3</summary>
        Mp3,

        /// <summary>.mp4, .m4a and the .mov an iPhone camera produces: all one container.</summary>
        Mp4,

        /// <summary>.ogg, .oga, .opus</summary>
        Ogg,

        /// <summary>.wav</summary>
        Wav,

        /// <summary>.webm, and the Matroska it is a profile of</summary>
        WebM,
    }
}
