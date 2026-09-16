using log4net;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// The AMI conversation over an already connected pair of streams. Knows nothing about
    /// sockets, so the protocol can be driven from canned text in tests.
    /// Not thread safe: one session belongs to one caller.
    /// </summary>
    public class AmiSession
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(AmiSession));

        /// <summary>What PJSIPShowContacts answers when no phone is registered.</summary>
        private const string NoContactsFound = "No Contacts found";

        private readonly AmiReader _reader;
        private readonly TextWriter _writer;

        private int _actionCounter;

        public AmiSession(TextReader reader, TextWriter writer)
        {
            _reader = new AmiReader(reader);
            _writer = writer;
        }

        public string ReadGreeting() => _reader.ReadGreeting();

        /// <summary>
        /// Logs in. Throws AmiException if Asterisk rejects the credentials. The secret is never
        /// logged.
        /// </summary>
        public void Login(string username, string secret)
        {
            Send(new AmiAction("Login").Add("Username", username).Add("Secret", secret));
            Log.Info($"AMI login as '{username}' accepted");
        }

        /// <summary>
        /// Asterisk answers "Response: Goodbye" and closes, so there is nothing worth waiting for.
        /// </summary>
        public void Logoff()
        {
            Write(new AmiAction("Logoff"));
        }

        /// <summary>
        /// Sends an action and returns its response, skipping any events that arrive first.
        /// Throws AmiException if the response is an error.
        /// </summary>
        public AmiMessage Send(AmiAction action) => SendAndWait(action).Response;

        /// <summary>
        /// Sends an action whose answer is a list: the response, then one event per item, then a
        /// final event with "EventList: Complete".
        /// </summary>
        /// <param name="emptyListMessage">
        /// An error message that means "the list is empty" rather than "the action failed", for
        /// the actions that answer an empty list with Response: Error. No events follow it.
        /// </param>
        public AmiEventList SendEventList(AmiAction action, string? emptyListMessage = null)
        {
            var (actionID, response) = SendAndWait(action, emptyListMessage);
            var events = new List<AmiMessage>();

            if (!response.IsSuccess)
                return new AmiEventList(response, events);

            while (true)
            {
                var message = ReadOrThrow();

                // List items always carry our ActionID; anything else is an unsolicited event.
                if (message.EventName == null || !string.Equals(message.ActionID, actionID, StringComparison.Ordinal))
                {
                    Log.Debug($"Skipping {message} while reading the {action.Name} list");
                    continue;
                }

                if (string.Equals(message.Get("EventList"), "Complete", StringComparison.OrdinalIgnoreCase))
                    return new AmiEventList(response, events);

                events.Add(message);
            }
        }

        /// <summary>
        /// Reloads one Asterisk module: res_pjsip for pjsip.conf, pbx_config for extensions.conf.
        /// A typed action rather than a CLI command, so the AMI user doesn't need "command"
        /// permission.
        /// </summary>
        public void Reload(string module)
        {
            Send(new AmiAction("Reload").Add("Module", module));
            Log.Info($"Reloaded Asterisk module '{module}'");
        }

        /// <summary>
        /// Which extensions are registered right now. A dedicated action, not "pjsip show
        /// contacts" through Action:Command, so the AMI user needs no "command" permission.
        /// The list items are "ContactList" events; with nothing registered Asterisk answers the
        /// action itself with an error instead of an empty list (D19).
        /// </summary>
        public List<PjsipContact> ShowContacts()
        {
            return SendEventList(new AmiAction("PJSIPShowContacts"), emptyListMessage: NoContactsFound).Events
                .Where(e => string.Equals(e.EventName, "ContactList", StringComparison.OrdinalIgnoreCase))
                .Select(PjsipContact.FromEvent)
                .ToList();
        }

        /// <summary>
        /// Reads events until the handler returns false or the connection closes. Responses are
        /// ignored, so a session used this way should not also be sending actions.
        /// </summary>
        public void ReadEvents(Func<AmiMessage, bool> handler)
        {
            while (true)
            {
                var message = _reader.ReadMessage();
                if (message == null)
                {
                    Log.Info("AMI connection closed, stopped reading events");
                    return;
                }

                if (message.EventName == null)
                    continue;

                if (!handler(message))
                    return;
            }
        }

        private (string ActionID, AmiMessage Response) SendAndWait(AmiAction action, string? toleratedError = null)
        {
            var actionID = Write(action);

            while (true)
            {
                var message = ReadOrThrow();

                if (message.Response == null)
                {
                    Log.Debug($"Skipping {message} while waiting for the {action.Name} response");
                    continue;
                }

                // Asterisk echoes our ActionID. A response without one is old or unusual; taking
                // it is better than hanging.
                if (message.ActionID != null && !string.Equals(message.ActionID, actionID, StringComparison.Ordinal))
                    continue;

                if (!message.IsSuccess)
                {
                    if (toleratedError == null || !string.Equals(message.Message, toleratedError, StringComparison.OrdinalIgnoreCase))
                        throw new AmiException($"AMI action '{action.Name}' failed: {message.Response} {message.Message}".TrimEnd());

                    Log.Debug($"AMI action '{action.Name}' answered '{message.Message}', treating it as an empty list");
                }

                return (actionID, message);
            }
        }

        private string Write(AmiAction action)
        {
            var actionID = (++_actionCounter).ToString();
            _writer.Write(action.ToProtocol(actionID));
            _writer.Flush();
            return actionID;
        }

        private AmiMessage ReadOrThrow() =>
            _reader.ReadMessage() ?? throw new AmiException("The AMI connection closed while waiting for a response.");
    }
}
