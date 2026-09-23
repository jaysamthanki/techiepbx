using System.Net.Sockets;
using System.Text;
using Techie.Pbx.Contracts;

namespace Techie.Pbx.Web.Services
{
    /// <summary>
    /// This end of the Helper socket (D142). Connect, send one request, read one reply, hang up:
    /// there is no pooling and no long-lived connection, because there are only ever a handful of
    /// these a day and a socket nobody is holding is a socket nobody can leak.
    ///
    /// Everything that goes wrong — no helper, no permission, no answer, or an answer that says
    /// no — comes back as a <see cref="HelperException"/> carrying a sentence the firewall page
    /// shows. The page never swallows one: "the firewall may or may not have been applied" is not
    /// a state an admin should have to guess at.
    /// </summary>
    public class HelperClient
    {
        public static readonly TimeSpan Timeout = TimeSpan.FromSeconds(HelperSocket.TimeoutSeconds);

        private readonly string socketPath;

        public HelperClient() : this(HelperSocket.Path)
        {
        }

        public HelperClient(string socketPath)
        {
            this.socketPath = socketPath;
        }

        /// <summary>
        /// Replaces the firewall ruleset with these rules. The Helper adds its own safety rules
        /// underneath them and validates every one of these again, so what comes back is what it
        /// actually applied.
        /// </summary>
        public async Task<FirewallStatus> Apply(IReadOnlyList<FirewallRule> rules)
        {
            var reply = await this.Send(HelperRequest.FirewallApply(rules));

            return reply.ResultAs<FirewallStatus>();
        }

        /// <summary>The protocol version the Helper speaks, and so whether it is there at all.</summary>
        public async Task<int> Ping()
        {
            var reply = await this.Send(HelperRequest.Ping());

            return reply.ResultAs<PingResult>().Version;
        }

        /// <summary>What the Helper last applied, and when.</summary>
        public async Task<FirewallStatus> Status()
        {
            var reply = await this.Send(HelperRequest.FirewallStatus());

            return reply.ResultAs<FirewallStatus>();
        }

        /// <summary>
        /// One line of JSON back. The same newline rule and the same size limit as the Helper's
        /// own reader, because a reply that never ends must not become a page that never renders.
        /// </summary>
        private static async Task<string> ReadLine(Socket socket, CancellationToken cancellation)
        {
            var buffer = new byte[4096];
            var line = new MemoryStream();

            while (line.Length < HelperSocket.MaxRequestBytes)
            {
                var read = await socket.ReceiveAsync(buffer, SocketFlags.None, cancellation);
                if (read == 0)
                    break;

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

            throw new HelperException("The helper closed the connection without a complete reply.");
        }

        /// <summary>
        /// The whole exchange. Five seconds covers all of it — connect, send, and the Helper's two
        /// nft invocations — and running out of them is reported rather than retried: a firewall
        /// apply is not something to quietly try twice.
        /// </summary>
        private async Task<HelperReply> Send(HelperRequest request)
        {
            using var timeout = new CancellationTokenSource(Timeout);
            using var socket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);

            string line;

            try
            {
                await socket.ConnectAsync(new UnixDomainSocketEndPoint(this.socketPath), timeout.Token);
                await socket.SendAsync(Encoding.UTF8.GetBytes(HelperJson.Line(request) + "\n"), SocketFlags.None, timeout.Token);

                line = await ReadLine(socket, timeout.Token);
            }
            catch (OperationCanceledException)
            {
                throw new HelperException($"The helper did not answer within {Timeout.TotalSeconds:0} seconds.");
            }
            catch (SocketException ex)
            {
                throw new HelperException($"Could not reach the helper on {this.socketPath}: {ex.Message} Is tnpbx-helper running?");
            }
            catch (IOException ex)
            {
                throw new HelperException($"The connection to the helper failed: {ex.Message}");
            }

            var reply = HelperJson.Parse<HelperReply>(line)
                ?? throw new HelperException("The helper's reply could not be read.");

            if (!reply.Ok)
                throw new HelperException(reply.Error ?? "The helper refused the request without saying why.");

            return reply;
        }
    }
}
