using System.Net.Sockets;
using System.Text;
using log4net;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// A TCP connection to Asterisk's manager interface. Connect, use the session, dispose.
    /// The connection is short lived: the app talks to AMI when it has something to say.
    /// </summary>
    public class AmiClient : IDisposable
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(AmiClient));

        private readonly AmiSettings _settings;

        private TcpClient? _tcp;
        private StreamReader? _reader;
        private StreamWriter? _writer;
        private AmiSession? _session;

        public AmiClient(AmiSettings settings)
        {
            var errors = settings.Validate();
            if (errors.Count > 0)
                throw new AmiException("Invalid AMI settings: " + string.Join(" ", errors));

            _settings = settings;
        }

        public AmiSession Session => _session ?? throw new AmiException("Not connected to AMI.");

        /// <summary>
        /// Opens the connection, reads the greeting and logs in.
        /// </summary>
        public AmiSession Connect()
        {
            if (_session != null)
                throw new AmiException("This AMI client is already connected.");

            var timeout = TimeSpan.FromSeconds(_settings.TimeoutSeconds);
            var milliseconds = _settings.TimeoutSeconds == 0 ? 0 : (int)timeout.TotalMilliseconds;

            _tcp = new TcpClient { ReceiveTimeout = milliseconds, SendTimeout = milliseconds };

            try
            {
                if (_settings.TimeoutSeconds == 0)
                {
                    _tcp.Connect(_settings.Host, _settings.Port);
                }
                else
                {
                    using var cancellation = new CancellationTokenSource(timeout);
                    _tcp.ConnectAsync(_settings.Host, _settings.Port, cancellation.Token).AsTask().GetAwaiter().GetResult();
                }
            }
            catch (Exception ex) when (ex is SocketException or OperationCanceledException)
            {
                _tcp.Dispose();
                _tcp = null;
                throw new AmiException($"Could not connect to AMI at {_settings.Host}:{_settings.Port}.", ex);
            }

            var encoding = new UTF8Encoding(encoderShouldEmitUTF8Identifier: false);
            var stream = _tcp.GetStream();
            _reader = new StreamReader(stream, encoding);
            _writer = new StreamWriter(stream, encoding) { AutoFlush = false };

            var session = new AmiSession(_reader, _writer);
            session.ReadGreeting();
            session.Login(_settings.Username, _settings.Secret);

            Log.Info($"Connected to AMI at {_settings.Host}:{_settings.Port}");
            _session = session;
            return session;
        }

        public void Dispose()
        {
            if (_session != null)
            {
                try
                {
                    _session.Logoff();
                }
                catch (Exception ex)
                {
                    // The connection is going away anyway.
                    Log.Debug($"Ignoring an error while logging off from AMI: {ex.Message}");
                }
            }

            _session = null;
            _writer?.Dispose();
            _reader?.Dispose();
            _tcp?.Dispose();
            _writer = null;
            _reader = null;
            _tcp = null;
        }
    }
}
