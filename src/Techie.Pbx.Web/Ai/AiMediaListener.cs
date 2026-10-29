using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using log4net;
using Techie.Pbx.Core.Ai;
using Techie.Pbx.Core.Data;

namespace Techie.Pbx.Web.Ai
{
    /// <summary>
    /// The far end of every AI agent's <c>AudioSocket()</c> (F11, D199, D200): a TCP listener on
    /// 127.0.0.1 at <c>Ai.MediaPort</c>, one <see cref="AiCallSession"/> per connection. A hosted
    /// service for the reason <c>CdrCollectorService</c> is one; everything it uses is built with
    /// <c>new</c>, and it never talks to ARI or AMI — a call is handed back to the dialplan by
    /// closing its socket.
    ///
    /// The port is read once, at start: a changed port needs an app restart, as well as the apply
    /// that writes it into the dialplan. The keys and voices are read per call, so a change on the
    /// Settings page reaches the next call without one.
    /// </summary>
    public class AiMediaListener : BackgroundService
    {
        /// <summary>Calls answered at once. One beyond it is refused, and the dialplan carries on.</summary>
        public const int MaxConcurrentCalls = 8;

        /// <summary>How long Asterisk has to send the UUID after connecting.</summary>
        public static readonly TimeSpan FirstFrameTimeout = TimeSpan.FromSeconds(5);

        /// <summary>How long to wait before trying again when the port cannot be bound.</summary>
        public static readonly TimeSpan RetryDelay = TimeSpan.FromSeconds(30);

        /// <summary>
        /// One client for the life of the app, so connections to the providers are pooled across
        /// calls. No client-wide timeout: each request carries its own (<see cref="AiProviders"/>).
        /// </summary>
        private static readonly HttpClient Http = new() { Timeout = Timeout.InfiniteTimeSpan };

        private static readonly ILog Log = LogManager.GetLogger(typeof(AiMediaListener));

        private int activeCalls;

        protected override async Task ExecuteAsync(CancellationToken stoppingToken)
        {
            var port = AiSettingsSnapshot.From(new SettingsRepository(PbxDatabase.Current).GetAll()).MediaPort;

            while (!stoppingToken.IsCancellationRequested)
            {
                var listener = new TcpListener(IPAddress.Loopback, port);

                try
                {
                    listener.Start();
                    Log.Info($"AI media listener on 127.0.0.1:{port}");

                    while (true)
                    {
                        var client = await listener.AcceptTcpClientAsync(stoppingToken);
                        this.Accept(client, stoppingToken);
                    }
                }
                catch (OperationCanceledException)
                {
                    // The app is stopping.
                }
                catch (SocketException ex)
                {
                    Log.Warn($"AI media listener cannot use 127.0.0.1:{port} ({ex.SocketErrorCode}); trying again in {RetryDelay.TotalSeconds:0} seconds.");

                    try
                    {
                        await Task.Delay(RetryDelay, stoppingToken);
                    }
                    catch (OperationCanceledException)
                    {
                        // The app is stopping.
                    }
                }
                finally
                {
                    listener.Stop();
                }
            }
        }

        private void Accept(TcpClient client, CancellationToken stoppingToken)
        {
            if (Interlocked.Increment(ref this.activeCalls) > MaxConcurrentCalls)
            {
                Interlocked.Decrement(ref this.activeCalls);
                Log.Warn($"AI media listener: {MaxConcurrentCalls} calls already in progress, refusing another.");
                client.Dispose();
                return;
            }

            _ = Task.Run(async () =>
            {
                try
                {
                    await this.HandleAsync(client, stoppingToken);
                }
                finally
                {
                    Interlocked.Decrement(ref this.activeCalls);
                }
            });
        }

        /// <summary>
        /// One call: the UUID first, then the agent it names, then the session. One INFO line per
        /// call — the agent's number, how long it lasted and how it ended, and nothing anyone said.
        /// </summary>
        private async Task HandleAsync(TcpClient client, CancellationToken stoppingToken)
        {
            using (client)
            {
                try
                {
                    client.NoDelay = true;
                    var stream = client.GetStream();

                    AudioSocketFrame? first;
                    using (var timeout = CancellationTokenSource.CreateLinkedTokenSource(stoppingToken))
                    {
                        timeout.CancelAfter(FirstFrameTimeout);
                        first = await AudioSocketProtocol.ReadFrameAsync(stream, timeout.Token);
                    }

                    if (first == null || first.Kind != AudioSocketProtocol.Kinds.Uuid || first.Payload.Length != 16)
                    {
                        Log.Warn("AI media listener: a connection did not start with a UUID; closed.");
                        return;
                    }

                    // AudioSocket sends the UUID in RFC 4122 (big-endian) byte order.
                    var uuid = new Guid(first.Payload, bigEndian: true).ToString("D");
                    var agent = new AiAgentRepository(PbxDatabase.Current).GetByServiceUuid(uuid);

                    if (agent == null)
                    {
                        Log.Warn($"AI media listener: no enabled agent has UUID {uuid}; closed.");
                        return;
                    }

                    var settings = AiSettingsSnapshot.From(new SettingsRepository(PbxDatabase.Current).GetAll());

                    if (!settings.IsConfigured)
                    {
                        Log.Warn($"AI agent {agent.Number}: the OpenRouter or ElevenLabs key is not set; handing the call to the dialplan.");
                        return;
                    }

                    var session = new AiCallSession(
                        agent,
                        settings,
                        Log,
                        (wav, ct) => AiProviders.TranscribeAsync(Http, settings, wav, ct),
                        (messages, model, ct) => AiProviders.ChatAsync(Http, settings, model, messages, ct),
                        (text, model, voice, ct) => AiProviders.SpeakAsync(Http, settings, model, voice, text, ct));

                    var clock = Stopwatch.StartNew();
                    await session.RunAsync(stream, stoppingToken);

                    Log.Info($"AI agent {agent.Number}: call ended after {clock.Elapsed.TotalSeconds:0} seconds, {session.Outcome}.");
                }
                catch (OperationCanceledException) when (!stoppingToken.IsCancellationRequested)
                {
                    Log.Warn("AI media listener: a connection sent no UUID in time; closed.");
                }
                catch (OperationCanceledException)
                {
                    // The app is stopping.
                }
                catch (Exception ex)
                {
                    Log.Error($"AI media listener: a connection failed ({ex.GetType().Name}: {ex.Message}).");
                }
            }
        }
    }
}
