using System.Text;
using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Drives a session against canned Asterisk responses and checks both what we understood and
    /// what we put on the wire. The end-to-end check against a real Asterisk happens on the lab VM.
    /// </summary>
    public class AmiSessionTests
    {
        private const string Greeting = "Asterisk Call Manager/9.0.0\r\n";
        private const string LoginAccepted = "Response: Success\r\nActionID: 1\r\nMessage: Authentication accepted\r\n\r\n";

        private readonly StringWriter _sent = new();

        private AmiSession Session(string canned) => new(new StringReader(canned), _sent);

        private AmiSession LoggedIn(string canned)
        {
            var session = Session(Greeting + LoginAccepted + canned);
            session.ReadGreeting();
            session.Login("tnpbx", "not-a-real-secret");
            return session;
        }

        [Fact]
        public void Login_sends_the_credentials_and_accepts_success()
        {
            LoggedIn("");

            Assert.Equal(
                "Action: Login\r\nActionID: 1\r\nUsername: tnpbx\r\nSecret: not-a-real-secret\r\n\r\n",
                _sent.ToString());
        }

        [Fact]
        public void Login_failure_is_an_ami_exception()
        {
            var session = Session(Greeting + "Response: Error\r\nActionID: 1\r\nMessage: Authentication failed\r\n\r\n");
            session.ReadGreeting();

            var ex = Assert.Throws<AmiException>(() => session.Login("tnpbx", "wrong"));
            Assert.Contains("Authentication failed", ex.Message);
        }

        [Fact]
        public void Reload_sends_a_typed_action_rather_than_a_cli_command()
        {
            var session = LoggedIn("Response: Success\r\nActionID: 2\r\nMessage: Module Reloaded\r\n\r\n");

            session.Reload("res_pjsip");

            Assert.EndsWith("Action: Reload\r\nActionID: 2\r\nModule: res_pjsip\r\n\r\n", _sent.ToString());
        }

        [Fact]
        public void A_failed_reload_throws()
        {
            var session = LoggedIn("Response: Error\r\nActionID: 2\r\nMessage: No such module\r\n\r\n");

            var ex = Assert.Throws<AmiException>(() => session.Reload("res_nonexistent"));
            Assert.Contains("No such module", ex.Message);
        }

        /// <summary>
        /// The Yealink push mechanism (D91): a typed action, the same reasoning as Reload's own
        /// test above. Not yet verified against a real Asterisk 22 — see D91's caveat.
        /// </summary>
        [Fact]
        public void Send_notify_sends_a_typed_action_rather_than_a_cli_command()
        {
            var session = LoggedIn("Response: Success\r\nActionID: 2\r\nMessage: NOTIFY sent\r\n\r\n");

            session.SendNotify("1001", "tnpbx-check-cfg");

            Assert.EndsWith(
                "Action: PJSIPSendNotify\r\nActionID: 2\r\nEndpoint: 1001\r\nNotificationName: tnpbx-check-cfg\r\n\r\n",
                _sent.ToString());
        }

        [Fact]
        public void A_failed_send_notify_throws()
        {
            var session = LoggedIn("Response: Error\r\nActionID: 2\r\nMessage: Unable to find endpoint\r\n\r\n");

            var ex = Assert.Throws<AmiException>(() => session.SendNotify("9999", "tnpbx-check-cfg"));
            Assert.Contains("Unable to find endpoint", ex.Message);
        }

        [Fact]
        public void Events_arriving_before_a_response_do_not_confuse_it()
        {
            var session = LoggedIn(
                "Event: Newchannel\r\nChannel: PJSIP/1001-00000001\r\n\r\n" +
                "Response: Success\r\nActionID: 99\r\nMessage: Someone else's response\r\n\r\n" +
                "Response: Success\r\nActionID: 2\r\nMessage: Module Reloaded\r\n\r\n");

            session.Reload("res_pjsip");
        }

        [Fact]
        public void Show_contacts_collects_the_event_list_and_stops_at_complete()
        {
            // Canned from a real Asterisk 22.11: the items are "ContactList", not
            // "ContactStatusDetail", and there is no AOR header.
            var session = LoggedIn(
                "Response: Success\r\nActionID: 2\r\nEventList: start\r\nMessage: Contacts will follow\r\n\r\n" +
                "Event: ContactList\r\nActionID: 2\r\nObjectType: contact\r\nObjectName: 1001;@ab12cd34\r\n" +
                "ViaAddr: 10.8.20.8\r\nViaPort: 57688\r\nQualifyTimeout: 3.000\r\nCallID: \r\n" +
                "Endpoint: 1001\r\nUri: sip:1001@10.8.20.8:57688\r\nUserAgent: Fake Softphone 1.0\r\n" +
                "QualifyFrequency: 0\r\nStatus: NonQualified\r\nRoundtripUsec: 4321\r\n\r\n" +
                "Event: PeerStatus\r\nPeerStatus: Registered\r\n\r\n" +
                "Event: ContactList\r\nActionID: 2\r\nObjectType: contact\r\nObjectName: 1002;@ef56ab78\r\n" +
                "Endpoint: 1002\r\nUri: sip:1002@10.8.20.9:5060\r\n" +
                "QualifyFrequency: 60\r\nStatus: Unreachable\r\n\r\n" +
                "Event: ContactListComplete\r\nActionID: 2\r\nEventList: Complete\r\nListItems: 2\r\n\r\n" +
                "Event: AfterTheList\r\n\r\n");

            var contacts = session.ShowContacts();

            Assert.Equal(new[] { "1001", "1002" }, contacts.Select(c => c.Aor));
            Assert.Equal("sip:1001@10.8.20.8:57688", contacts[0].Uri);
            Assert.Equal("NonQualified", contacts[0].Status);
            Assert.Equal("Fake Softphone 1.0", contacts[0].UserAgent);
            Assert.Equal(4321, contacts[0].RoundTripMicroseconds);
            Assert.Equal("Unreachable", contacts[1].Status);
            Assert.Equal(0, contacts[1].RoundTripMicroseconds);
            Assert.EndsWith("Action: PJSIPShowContacts\r\nActionID: 2\r\n\r\n", _sent.ToString());
        }

        [Fact]
        public void Show_contacts_on_a_system_with_nothing_registered()
        {
            var session = LoggedIn(
                "Response: Success\r\nActionID: 2\r\nEventList: start\r\n\r\n" +
                "Event: ContactListComplete\r\nActionID: 2\r\nEventList: Complete\r\nListItems: 0\r\n\r\n");

            Assert.Empty(session.ShowContacts());
        }

        /// <summary>
        /// Asterisk 22.11 refuses the action outright when nothing is registered rather than
        /// answering an empty list. That is not a failure.
        /// </summary>
        [Fact]
        public void Show_contacts_treats_no_contacts_found_as_an_empty_list()
        {
            var session = LoggedIn("Response: Error\r\nActionID: 2\r\nMessage: No Contacts found\r\n\r\n");

            Assert.Empty(session.ShowContacts());
        }

        [Fact]
        public void Show_contacts_still_throws_on_a_real_error()
        {
            var session = LoggedIn("Response: Error\r\nActionID: 2\r\nMessage: Permission denied\r\n\r\n");

            var ex = Assert.Throws<AmiException>(() => session.ShowContacts());
            Assert.Contains("Permission denied", ex.Message);
        }

        [Fact]
        public void A_connection_that_closes_mid_action_is_an_ami_exception()
        {
            var session = LoggedIn("Event: Newchannel\r\nChannel: PJSIP/1001-00000001\r\n\r\n");

            Assert.Throws<AmiException>(() => session.Reload("res_pjsip"));
        }

        [Fact]
        public void Read_events_hands_over_events_until_the_handler_stops()
        {
            var session = LoggedIn(
                "Event: SuccessfulAuth\r\nAccountID: 1001\r\n\r\n" +
                "Response: Success\r\nActionID: 9\r\n\r\n" +
                "Event: InvalidAccountID\r\nAccountID: 9999\r\n\r\n" +
                "Event: NeverRead\r\n\r\n");

            var seen = new List<string>();
            session.ReadEvents(message =>
            {
                seen.Add(message.EventName!);
                return message.EventName != "InvalidAccountID";
            });

            Assert.Equal(new[] { "SuccessfulAuth", "InvalidAccountID" }, seen);
        }

        [Fact]
        public void Read_events_returns_when_the_connection_closes()
        {
            var session = LoggedIn("Event: SuccessfulAuth\r\nAccountID: 1001\r\n\r\n");

            var count = 0;
            session.ReadEvents(_ => { count++; return true; });

            Assert.Equal(1, count);
        }

        [Fact]
        public void Logoff_does_not_wait_for_an_answer()
        {
            var session = LoggedIn("");

            session.Logoff();

            Assert.EndsWith("Action: Logoff\r\nActionID: 2\r\n\r\n", _sent.ToString());
        }

        [Fact]
        public void Action_ids_increase_so_responses_can_be_matched()
        {
            var session = LoggedIn(
                "Response: Success\r\nActionID: 2\r\n\r\n" +
                "Response: Success\r\nActionID: 3\r\n\r\n");

            session.Reload("res_pjsip");
            session.Reload("pbx_config");

            var sent = _sent.ToString();
            Assert.Contains("ActionID: 2", sent);
            Assert.Contains("ActionID: 3", sent);
        }

        [Fact]
        public void The_greeting_must_come_first()
        {
            var session = new AmiSession(new StringReader("Response: Success\r\n\r\n"), new StringWriter(new StringBuilder()));

            Assert.Throws<AmiException>(() => session.ReadGreeting());
        }
    }
}
