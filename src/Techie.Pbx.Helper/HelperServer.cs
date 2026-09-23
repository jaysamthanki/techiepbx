using System.Net.Sockets;
using System.Text;
using log4net;
using Techie.Pbx.Contracts;
using Techie.Pbx.Helper.Firewall;

namespace Techie.Pbx.Helper
{
    /// <summary>
    /// The listener. A Unix socket, never TCP; one accepted connection carries one request and one
    /// reply and is then closed (D142).
    ///
    /// <b>The authentication model, in full:</b> the kernel tells us the UID of the process that
    /// connected (SO_PEERCRED), and it must be the UID of the <c>tnpbx</c> system user. Nothing
    /// else — no token, no password, nothing the caller says about itself. Two things back that
    /// up rather than replace it: the socket's directory is 0750 root:tnpbx so only that group can
    /// reach the socket at all, and the socket itself is 0660 root:tnpbx.
    ///
    /// Connections are served one at a time on purpose. A firewall apply runs two nft commands
    /// against one kernel ruleset, so two at once is not a thing worth being able to do, and a
    /// single-threaded loop is a loop that can be read end to end. Both socket directions carry a
    /// timeout so one stalled caller cannot hold that loop.
    /// </summary>
    public class HelperServer
    {
        /// <summary>0750 root:tnpbx.</summary>
        private const UnixFileMode DirectoryMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute |
            UnixFileMode.GroupRead | UnixFileMode.GroupExecute;

        /// <summary>0660 root:tnpbx.</summary>
        private const UnixFileMode SocketMode =
            UnixFileMode.UserRead | UnixFileMode.UserWrite |
            UnixFileMode.GroupRead | UnixFileMode.GroupWrite;

        private static readonly ILog Log = LogManager.GetLogger(typeof(HelperServer));

        private readonly NftFirewall firewall;
        private readonly SystemUser owner;

        public HelperServer(SystemUser owner, NftFirewall firewall)
        {
            this.firewall = firewall;
            this.owner = owner;
        }

        /// <summary>
        /// Binds the socket and serves connections until the token is cancelled. Blocks: this is
        /// what the process does.
        /// </summary>
        public void Listen(CancellationToken cancellation)
        {
            using var listener = this.Bind();

            Log.Info($"Listening on {HelperSocket.Path}; only uid {this.owner.Uid} ({this.owner.Name}) may connect");

            using var stopping = cancellation.Register(listener.Dispose);

            while (!cancellation.IsCancellationRequested)
            {
                Socket connection;

                try
                {
                    connection = listener.Accept();
                }
                catch (Exception ex) when (ex is ObjectDisposedException or SocketException)
                {
                    if (cancellation.IsCancellationRequested)
                        break;

                    Log.Error($"Could not accept a connection: {ex.Message}");
                    continue;
                }

                using (connection)
                {
                    try
                    {
                        this.Serve(connection);
                    }
                    catch (Exception ex)
                    {
                        // One bad connection is never allowed to end the Helper: the firewall page
                        // failing is an inconvenience, a Helper that has exited is a box nobody
                        // can change the firewall on.
                        Log.Error($"Connection failed: {ex.Message}");
                    }
                }
            }

            this.Remove();
            Log.Info("Stopped listening");
        }

        /// <summary>
        /// The socket, with its directory and its own permissions set. The directory is made
        /// 0750 root:tnpbx <b>before</b> the socket is bound, which is what closes the moment
        /// between bind (which creates the socket with the umask's permissions) and the chmod
        /// that follows it: for that moment the socket exists, but nothing outside the tnpbx
        /// group can so much as look into the directory holding it.
        /// </summary>
        private Socket Bind()
        {
            Directory.CreateDirectory(HelperSocket.DirectoryPath);
            File.SetUnixFileMode(HelperSocket.DirectoryPath, DirectoryMode);
            Native.Chown(HelperSocket.DirectoryPath, 0, this.owner.Gid);

            // A socket file left behind by a killed Helper would make bind fail. It is in a
            // root-owned directory, so there is nothing here anybody else could have put there.
            this.Remove();

            var listener = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
            listener.Bind(new UnixDomainSocketEndPoint(HelperSocket.Path));

            File.SetUnixFileMode(HelperSocket.Path, SocketMode);
            Native.Chown(HelperSocket.Path, 0, this.owner.Gid);

            listener.Listen(backlog: 16);

            return listener;
        }

        /// <summary>
        /// One validated request into one reply. Every message type is listed here and there is no
        /// default that does anything: an unknown type never gets this far, because
        /// <see cref="HelperRequest.Validate"/> refused it.
        /// </summary>
        private HelperReply Dispatch(HelperRequest request)
        {
            var errors = request.Validate();
            if (errors.Count > 0)
            {
                Log.Warn($"Refused a '{request.Type}' message: {string.Join(" ", errors)}");
                return HelperReply.Failed(string.Join(" ", errors));
            }

            try
            {
                switch (request.Type)
                {
                    case HelperMessageTypes.FirewallApply:
                        return HelperReply.Succeeded(this.firewall.Apply(request.Rules));

                    case HelperMessageTypes.FirewallStatus:
                        return HelperReply.Succeeded(this.firewall.Current());

                    case HelperMessageTypes.Ping:
                        return HelperReply.Succeeded(new PingResult { Version = HelperSocket.ProtocolVersion });

                    default:
                        return HelperReply.Failed($"'{request.Type}' is not a message this helper accepts.");
                }
            }
            catch (FirewallException ex)
            {
                // Expected and worth reading: nft said no, or nftables is not installed.
                Log.Warn($"{request.Type} failed: {ex.Message}");
                return HelperReply.Failed(ex.Message);
            }
            catch (Exception ex)
            {
                // Unexpected, so the detail goes to the journal and the caller gets a sentence
                // that sends an admin there rather than one about our own internals.
                Log.Error($"{request.Type} failed unexpectedly: {ex}");
                return HelperReply.Failed($"The helper could not complete {request.Type}. See the journal: journalctl -u tnpbx-helper.");
            }
        }

        /// <summary>
        /// One line of JSON, up to <see cref="HelperSocket.MaxRequestBytes"/>. A caller that never
        /// sends a newline runs into that limit or the receive timeout, and either way the
        /// connection ends rather than the Helper waiting on it.
        /// </summary>
        private static string? ReadLine(Socket connection)
        {
            var buffer = new byte[4096];
            var line = new MemoryStream();

            while (line.Length < HelperSocket.MaxRequestBytes)
            {
                var read = connection.Receive(buffer, SocketFlags.None);
                if (read == 0)
                    return null;

                for (var index = 0; index < read; index++)
                {
                    if (buffer[index] == (byte)'\n')
                    {
                        line.Write(buffer, 0, index);
                        return Encoding.UTF8.GetString(line.ToArray());
                    }
                }

                line.Write(buffer, 0, read);
            }

            throw new InvalidDataException($"The request was longer than {HelperSocket.MaxRequestBytes} bytes without a newline.");
        }

        /// <summary>Deletes the socket file, if it is there. Called before binding and after stopping.</summary>
        private void Remove()
        {
            try
            {
                File.Delete(HelperSocket.Path);
            }
            catch (IOException ex)
            {
                Log.Warn($"Could not remove {HelperSocket.Path}: {ex.Message}");
            }
        }

        /// <summary>
        /// The peer check and then the one exchange. Nothing is read from a connection whose UID
        /// is wrong — it is logged and closed before a single byte is taken off it.
        /// </summary>
        private void Serve(Socket connection)
        {
            var uid = Native.PeerUid(connection);

            if (uid != this.owner.Uid)
            {
                Log.Warn($"Refused a connection from uid {uid}: only uid {this.owner.Uid} ({this.owner.Name}) may use this socket.");
                return;
            }

            connection.ReceiveTimeout = HelperSocket.TimeoutSeconds * 1000;
            connection.SendTimeout = HelperSocket.TimeoutSeconds * 1000;

            HelperReply reply;

            try
            {
                var line = ReadLine(connection);

                if (line == null)
                {
                    // The caller connected and went away. Nothing to answer.
                    return;
                }

                var request = HelperJson.Parse<HelperRequest>(line);

                reply = request == null
                    ? HelperReply.Failed("That was not a message this helper understands.")
                    : this.Dispatch(request);
            }
            catch (InvalidDataException ex)
            {
                Log.Warn($"Refused a request: {ex.Message}");
                reply = HelperReply.Failed(ex.Message);
            }

            Write(connection, HelperJson.Line(reply) + "\n");
            connection.Shutdown(SocketShutdown.Both);
        }

        /// <summary>
        /// The whole reply, however many sends that takes. A blocking Send is allowed to place
        /// less than it was given, and a reply the caller only got half of is worse than none.
        /// </summary>
        private static void Write(Socket connection, string line)
        {
            var bytes = Encoding.UTF8.GetBytes(line);
            var sent = 0;

            while (sent < bytes.Length)
                sent += connection.Send(bytes, sent, bytes.Length - sent, SocketFlags.None);
        }
    }
}
