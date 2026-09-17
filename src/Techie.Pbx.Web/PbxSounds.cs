using log4net;
using Techie.Pbx.Asterisk.Audio;

namespace Techie.Pbx.Web
{
    /// <summary>
    /// The one <see cref="AnnouncementStore"/> the web app uses, built once at startup from
    /// configuration. A static holder for the same reason <see cref="PbxDatabase"/> is one (D22):
    /// pages construct what they need with <c>new</c> rather than taking it from the container.
    ///
    /// The store is only a base path and an ffmpeg command, so sharing one instance costs nothing
    /// and keeps "where does announcement audio live" a single answer.
    /// </summary>
    public static class PbxSounds
    {
        /// <summary>The configuration key for the base path. Unset means the Asterisk default.</summary>
        public const string PathSetting = "Announcements:SoundsPath";

        private static readonly ILog Log = LogManager.GetLogger(typeof(PbxSounds));

        private static AnnouncementStore? store;

        public static AnnouncementStore Current =>
            store ?? throw new InvalidOperationException("The sounds store is not open yet; PbxSounds.Open runs at startup.");

        /// <summary>
        /// Reads the base path out of configuration and remembers the store. A relative path is
        /// relative to the install rather than to whatever directory the service happened to start
        /// in, the same rule the database path follows (D25); a real install configures an
        /// absolute one under Asterisk's sounds directory.
        ///
        /// Nothing is created on disk here: the directory is made when the first announcement's
        /// audio is saved, because a system with no announcements should not be making directories
        /// in Asterisk's data.
        /// </summary>
        public static void Open(IConfiguration configuration, string contentRootPath)
        {
            var configured = configuration[PathSetting];
            var path = string.IsNullOrWhiteSpace(configured) ? AnnouncementStore.DefaultSoundsPath : configured.Trim();

            if (!Path.IsPathRooted(path))
                path = Path.Combine(contentRootPath, path);

            store = new AnnouncementStore(path);
            Log.Info($"Announcement audio lives in {store.SoundsPath}");
        }
    }
}
