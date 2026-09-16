using log4net;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// Turns the AMI byte stream into messages. Separate from the socket so the protocol can be
    /// tested against canned responses.
    /// </summary>
    public class AmiReader
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(AmiReader));

        private readonly TextReader _reader;

        public AmiReader(TextReader reader)
        {
            _reader = reader;
        }

        /// <summary>
        /// The banner Asterisk sends as soon as the connection opens, e.g.
        /// "Asterisk Call Manager/9.0.0".
        /// </summary>
        public string ReadGreeting()
        {
            var line = _reader.ReadLine();

            if (line == null)
                throw new AmiException("The connection closed before Asterisk sent its AMI greeting.");

            if (!line.StartsWith("Asterisk Call Manager", StringComparison.Ordinal))
                throw new AmiException("The server did not answer with an AMI greeting.");

            return line;
        }

        /// <summary>
        /// Reads one packet: "Name: value" lines ending at a blank line. Returns null at the end
        /// of the stream.
        /// </summary>
        public AmiMessage? ReadMessage()
        {
            var headers = new List<KeyValuePair<string, string>>();

            while (true)
            {
                var line = _reader.ReadLine();

                if (line == null)
                    return headers.Count > 0 ? new AmiMessage(headers) : null;

                if (line.Length == 0)
                {
                    // A blank line ends a packet. Blank lines between packets are ignored.
                    if (headers.Count == 0)
                        continue;
                    return new AmiMessage(headers);
                }

                var colon = line.IndexOf(':');
                if (colon <= 0)
                {
                    // Asterisk 22 returns CLI output as repeated "Output" headers, so anything
                    // without a header name is something we don't model. Skip the line rather
                    // than abandoning the rest of the packet.
                    Log.Debug("Ignoring an AMI line that is not a header");
                    continue;
                }

                var name = line[..colon].TrimEnd();
                var value = line[(colon + 1)..];

                // Asterisk writes exactly one space after the colon; anything further along is
                // part of the value (CLI output is indented).
                if (value.StartsWith(' '))
                    value = value[1..];

                headers.Add(new KeyValuePair<string, string>(name, value));
            }
        }
    }
}
