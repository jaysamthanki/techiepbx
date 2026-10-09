using System.Runtime.InteropServices;

namespace Techie.Pbx.Backup
{
    /// <summary>
    /// What this CLI needs from libc and .NET does not offer: who it is running as, the IDs behind
    /// an account or group name, and setting a file's owner. The same calls the helper makes, for
    /// the same reasons (D142); lookups check the name that comes back against the name asked
    /// for, so a wrongly unpacked struct fails rather than handing out a wrong ID.
    /// </summary>
    internal static class Native
    {
        /// <summary>Sets a path's owner and group, which .NET can read but not write.</summary>
        public static void Chown(string path, uint uid, uint gid)
        {
            if (chown(path, uid, gid) != 0)
                throw new IOException($"Could not set the owner of {path}: errno {Marshal.GetLastPInvokeError()}.");
        }

        /// <summary>The effective UID: 0 is root, which restore insists on.</summary>
        public static uint EffectiveUid() => geteuid();

        /// <summary>The GID of the group with this name, or null when there is none.</summary>
        public static uint? LookupGroup(string name)
        {
            var entry = getgrnam(name);
            if (entry == IntPtr.Zero)
                return null;

            var group = Marshal.PtrToStructure<Group>(entry);
            var found = Marshal.PtrToStringUTF8(group.Name);

            if (found != name)
                throw new InvalidOperationException($"The group database returned '{found}' for '{name}'; refusing to trust it.");

            return group.Gid;
        }

        /// <summary>The UID of the account with this name, or null when there is none.</summary>
        public static uint? LookupUser(string name)
        {
            var entry = getpwnam(name);
            if (entry == IntPtr.Zero)
                return null;

            var passwd = Marshal.PtrToStructure<Passwd>(entry);
            var found = Marshal.PtrToStringUTF8(passwd.Name);

            if (found != name)
                throw new InvalidOperationException($"The password database returned '{found}' for '{name}'; refusing to trust it.");

            return passwd.Uid;
        }

        [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int chown(string path, uint owner, uint group);

        [DllImport("libc")]
        private static extern uint geteuid();

        [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr getgrnam(string name);

        [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr getpwnam(string name);

        /// <summary>POSIX <c>struct group</c>. Only the first three fields are ever read.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Group
        {
            public IntPtr Name;
            public IntPtr Password;
            public uint Gid;
            public IntPtr Members;
        }

        /// <summary>POSIX <c>struct passwd</c>. Only the first four fields are ever read.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Passwd
        {
            public IntPtr Name;
            public IntPtr Password;
            public uint Uid;
            public uint Gid;
            public IntPtr Gecos;
            public IntPtr Directory;
            public IntPtr Shell;
        }
    }
}
