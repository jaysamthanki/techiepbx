using Techie.Pbx.Asterisk.Ami;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// Parsing and formatting only: canned bytes in, messages out, and back again.
    /// </summary>
    public class AmiProtocolTests
    {
        private static AmiReader Reader(string text) => new(new StringReader(text));

        [Fact]
        public void Reads_the_greeting()
        {
            Assert.Equal("Asterisk Call Manager/9.0.0", Reader("Asterisk Call Manager/9.0.0\r\n").ReadGreeting());
        }

        [Fact]
        public void Rejects_a_port_that_is_not_ami()
        {
            Assert.Throws<AmiException>(() => Reader("SSH-2.0-OpenSSH_9.2\r\n").ReadGreeting());
            Assert.Throws<AmiException>(() => Reader("").ReadGreeting());
        }

        [Fact]
        public void Reads_consecutive_packets_and_stops_at_the_end_of_the_stream()
        {
            var reader = Reader(
                "Response: Success\r\nActionID: 1\r\nMessage: Authentication accepted\r\n\r\n" +
                "Event: FullyBooted\r\nStatus: Fully Booted\r\n\r\n");

            var response = reader.ReadMessage()!;
            Assert.Equal("Success", response.Response);
            Assert.True(response.IsSuccess);
            Assert.Equal("1", response.ActionID);
            Assert.Equal("Authentication accepted", response.Message);
            Assert.Null(response.EventName);

            var booted = reader.ReadMessage()!;
            Assert.Equal("FullyBooted", booted.EventName);
            Assert.Null(booted.Response);

            Assert.Null(reader.ReadMessage());
        }

        [Fact]
        public void Header_names_are_case_insensitive_and_repeats_are_kept_in_order()
        {
            var message = Reader("Response: Success\r\noutput: one\r\nOutput: two\r\n\r\n").ReadMessage()!;

            Assert.Equal("one", message.Get("OUTPUT"));
            Assert.Equal(new[] { "one", "two" }, message.GetAll("Output"));
            Assert.Null(message.Get("Nothing"));
            Assert.Empty(message.GetAll("Nothing"));
        }

        [Fact]
        public void Only_one_space_after_the_colon_is_removed_so_a_value_keeps_its_indentation()
        {
            var message = Reader("Response: Success\r\nOutput:   indented\r\nOutput:\r\n\r\n").ReadMessage()!;

            Assert.Equal(new[] { "  indented", "" }, message.GetAll("Output"));
        }

        [Fact]
        public void Ignores_blank_lines_between_packets_and_lines_that_are_not_headers()
        {
            var reader = Reader("\r\n\r\nResponse: Success\r\n--END COMMAND--\r\nActionID: 3\r\n\r\n");

            var message = reader.ReadMessage()!;
            Assert.Equal("Success", message.Response);
            Assert.Equal("3", message.ActionID);
            Assert.Equal(2, message.Headers.Count);
        }

        [Fact]
        public void An_unterminated_packet_at_the_end_of_the_stream_is_still_returned()
        {
            var message = Reader("Response: Error\r\nMessage: Authentication failed\r\n").ReadMessage()!;

            Assert.False(message.IsSuccess);
            Assert.Equal("Authentication failed", message.Message);
        }

        [Fact]
        public void Only_a_success_response_counts_as_success()
        {
            Assert.True(Reader("Response: Success\r\n\r\n").ReadMessage()!.IsSuccess);
            Assert.False(Reader("Response: Follows\r\n\r\n").ReadMessage()!.IsSuccess);
        }

        [Fact]
        public void Action_writes_headers_in_order_with_crlf_and_a_trailing_blank_line()
        {
            var action = new AmiAction("Reload").Add("Module", "res_pjsip");

            Assert.Equal("Action: Reload\r\nActionID: 7\r\nModule: res_pjsip\r\n\r\n", action.ToProtocol("7"));
        }

        [Fact]
        public void Action_refuses_a_value_that_could_inject_a_second_action()
        {
            var login = new AmiAction("Login");

            Assert.Throws<InvalidOperationException>(() => login.Add("Secret", "value\r\nAction: Command"));
            Assert.Throws<InvalidOperationException>(() => login.Add("Secret", "value\n"));
            Assert.Throws<InvalidOperationException>(() => login.Add("Secret", "value\0"));
            Assert.Throws<InvalidOperationException>(() => login.Add("Secret", "value" + (char)7));
        }

        [Fact]
        public void Action_accepts_an_ordinary_value()
        {
            Assert.Contains("Command: pjsip show contacts",
                new AmiAction("Command").Add("Command", "pjsip show contacts").ToProtocol("1"));
        }

        [Theory]
        [InlineData("Bad Name")]
        [InlineData("Bad:Name")]
        [InlineData("")]
        public void Action_refuses_an_unusable_name(string name)
        {
            Assert.Throws<InvalidOperationException>(() => new AmiAction(name));
            Assert.Throws<InvalidOperationException>(() => new AmiAction("Login").Add(name, "value"));
        }

        [Fact]
        public void Contact_is_read_from_a_contact_list_event()
        {
            var message = Reader(
                "Event: ContactList\r\nObjectType: contact\r\nObjectName: 1001;@ab12cd34\r\n" +
                "ViaAddr: 10.8.20.8\r\nViaPort: 57688\r\nEndpoint: 1001\r\n" +
                "Uri: sip:1001@10.8.20.8:57688\r\nStatus: NonQualified\r\nRoundtripUsec: 1234\r\n" +
                "UserAgent: Fake Softphone 1.0\r\n\r\n").ReadMessage()!;

            var contact = PjsipContact.FromEvent(message);

            // Endpoint, not ObjectName: ObjectName carries the ";@<hash>" suffix. There is no AOR
            // header on ContactList at all (D19).
            Assert.Equal("1001", contact.Aor);
            Assert.Equal("sip:1001@10.8.20.8:57688", contact.Uri);
            Assert.Equal("NonQualified", contact.Status);
            Assert.Equal("Fake Softphone 1.0", contact.UserAgent);
            Assert.Equal(1234, contact.RoundTripMicroseconds);
        }

        [Fact]
        public void Settings_are_validated()
        {
            Assert.Empty(new AmiSettings { Username = "tnpbx", Secret = "not-a-real-secret" }.Validate());

            var errors = new AmiSettings { Host = "", Port = 0, TimeoutSeconds = -1 }.Validate();
            Assert.Equal(5, errors.Count);

            Assert.Throws<AmiException>(() => new AmiClient(new AmiSettings()));
        }
    }
}
