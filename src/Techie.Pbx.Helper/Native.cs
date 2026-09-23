using System.Net.Sockets;
using System.Runtime.InteropServices;

namespace Techie.Pbx.Helper
{
    /// <summary>
    /// The three things this process needs from libc and .NET does not offer: who is on the other
    /// end of a Unix socket, what UID a named account has, and setting a file's group.
    ///
    /// <see cref="PeerUid"/> is the whole authentication model (D142), so it is written to fail
    /// rather than guess: a getsockopt that does not succeed throws, and the caller closes the
    /// connection. There is no path through here that returns "probably fine".
    /// </summary>
    internal static class Native
    {
        /// <summary>SO_PEERCRED on Linux. Not portable, and this process only ever runs on Linux.</summary>
        private const int SoPeerCred = 17;

        private const int SolSocket = 1;

        /// <summary>Sets a path's owner and group, which .NET can read but not write.</summary>
        public static void Chown(string path, uint uid, uint gid)
        {
            if (chown(path, uid, gid) != 0)
                throw new IOException($"Could not set the owner of {path}: errno {Marshal.GetLastPInvokeError()}.");
        }

        /// <summary>
        /// The account with this name, or null when there is none. The name that comes back is
        /// checked against the name asked for: if the struct this unpacks were ever wrong, that
        /// check fails long before a wrong UID could become the one allowed to talk to us.
        /// </summary>
        public static SystemUser? LookupUser(string name)
        {
            var entry = getpwnam(name);
            if (entry == IntPtr.Zero)
                return null;

            var passwd = Marshal.PtrToStructure<Passwd>(entry);
            var found = Marshal.PtrToStringUTF8(passwd.Name);

            if (found != name)
                throw new InvalidOperationException($"The password database returned '{found}' for '{name}'; refusing to trust it.");

            return new SystemUser(name, passwd.Uid, passwd.Gid);
        }

        /// <summary>
        /// The UID of the process on the other end of an accepted Unix socket, as the kernel
        /// recorded it when the connection was made. It cannot be forged by the caller, which is
        /// what makes it worth being the only credential the Helper asks for.
        /// </summary>
        public static uint PeerUid(Socket connection)
        {
            var credentials = default(Ucred);
            var size = Marshal.SizeOf<Ucred>();

            if (getsockopt((int)connection.Handle, SolSocket, SoPeerCred, ref credentials, ref size) != 0)
                throw new IOException($"SO_PEERCRED failed: errno {Marshal.GetLastPInvokeError()}.");

            if (size != Marshal.SizeOf<Ucred>())
                throw new IOException($"SO_PEERCRED returned {size} bytes, expected {Marshal.SizeOf<Ucred>()}.");

            return credentials.Uid;
        }

        [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern int chown(string path, uint owner, uint group);

        [DllImport("libc", CharSet = CharSet.Ansi, SetLastError = true)]
        private static extern IntPtr getpwnam(string name);

        [DllImport("libc", SetLastError = true)]
        private static extern int getsockopt(int descriptor, int level, int option, ref Ucred value, ref int length);

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

        /// <summary>Linux <c>struct ucred</c>, what SO_PEERCRED fills in.</summary>
        [StructLayout(LayoutKind.Sequential)]
        private struct Ucred
        {
            public int Pid;
            public uint Uid;
            public uint Gid;
        }
    }
}
