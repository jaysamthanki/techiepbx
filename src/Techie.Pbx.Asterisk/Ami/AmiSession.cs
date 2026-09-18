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

        /// <summary>
        /// What PJSIPShowRegistrationsOutbound may answer when no trunk registers. Asterisk's
        /// wording here has not been seen on the wire yet, so every plausible one is tolerated
        /// and the list stays until the lab VM settles it (D40).
        /// </summary>
        private static readonly string[] NoRegistrationsFound =
        {
            "No objects found.",
            "No objects found",
            "No Registrations found",
            "No registrations found",
        };

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
        /// <param name="emptyListMessages">
        /// Error messages that mean "the list is empty" rather than "the action failed", for the
        /// actions that answer an empty list with Response: Error (D19). No events follow one.
        /// Several can be given: Asterisk's wording differs between actions, and being wrong about
        /// it would turn "nothing registered" into a failed page.
        /// </param>
        public AmiEventList SendEventList(AmiAction action, params string[] emptyListMessages)
        {
            var (actionID, response) = SendAndWait(action, emptyListMessages);
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
        /// Sends a PJSIP NOTIFY of a named type to an endpoint's registered contacts — the
        /// mechanism Yealink phones use in place of Polycom's HTTP push (D91), because a Yealink
        /// phone has no web endpoint to push to. The type names a category in notify.conf.
        /// </summary>
        public void SendNotify(string endpoint, string notificationName)
        {
            Send(new AmiAction("PJSIPSendNotify").Add("Endpoint", endpoint).Add("NotificationName", notificationName));
            Log.Info($"Sent NOTIFY '{notificationName}' to endpoint '{endpoint}'");
        }

        /// <summary>
        /// Which extensions are registered right now. A dedicated action, not "pjsip show
        /// contacts" through Action:Command, so the AMI user needs no "command" permission.
        /// The list items are "ContactList" events; with nothing registered Asterisk answers the
        /// action itself with an error instead of an empty list (D19).
        /// </summary>
        public List<PjsipContact> ShowContacts()
        {
            return SendEventList(new AmiAction("PJSIPShowContacts"), NoContactsFound).Events
                .Where(e => string.Equals(e.EventName, "ContactList", StringComparison.OrdinalIgnoreCase))
                .Select(PjsipContact.FromEvent)
                .ToList();
        }

        /// <summary>
        /// Which trunks are registered with their providers right now: our outbound registrations,
        /// not the phones registering with us (those are <see cref="ShowContacts"/>).
        ///
        /// Every event in the list is taken, rather than filtering by event name as the contacts
        /// list does: <see cref="SendEventList"/> has already narrowed the list to the events
        /// carrying our ActionID, so whatever Asterisk calls them, they are the answer to this
        /// question. That leaves one less name to be wrong about (D40).
        /// </summary>
        public List<PjsipRegistration> ShowRegistrations()
        {
            return SendEventList(new AmiAction("PJSIPShowRegistrationsOutbound"), NoRegistrationsFound).Events
                .Select(PjsipRegistration.FromEvent)
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

        private (string ActionID, AmiMessage Response) SendAndWait(AmiAction action, IReadOnlyCollection<string>? toleratedErrors = null)
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
                    var tolerated = toleratedErrors != null && message.Message != null &&
                        toleratedErrors.Contains(message.Message, StringComparer.OrdinalIgnoreCase);

                    if (!tolerated)
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
