namespace Techie.Pbx.Backup
{
    /// <summary>
    /// The owner and mode an archive recorded for one entry, which restore puts back on the
    /// restored copy. Names are preferred over IDs when there are names: a backup restored onto a
    /// freshly installed box should land owned by its asterisk user, whatever UID that got.
    /// </summary>
    public class ArchivedEntry
    {
        public int Gid { get; init; }

        public string GroupName { get; init; } = "";

        public UnixFileMode Mode { get; init; }

        public int Uid { get; init; }

        public string UserName { get; init; } = "";
    }
}
