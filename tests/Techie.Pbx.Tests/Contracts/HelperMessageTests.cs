using System.Text.Json;
using Techie.Pbx.Contracts;

namespace Techie.Pbx.Tests.Contracts
{
    /// <summary>
    /// The wire format between the web process and the Helper (D142): one request object, one
    /// reply object, each on one line of JSON. What matters here is that every message survives
    /// the round trip unchanged, and that anything malformed comes back as "not a message" rather
    /// than as a half-filled one — the Helper acts on what it deserializes.
    /// </summary>
    public class HelperMessageTests
    {
        private static List<FirewallRule> SampleRules() => new()
        {
            FirewallRule.Port(FirewallProtocol.Udp, 5060, "SIP UDP"),
            new FirewallRule(FirewallProtocol.Udp, 10000, 20000, "RTP UDP"),
            FirewallRule.Port(FirewallProtocol.Tcp, 443, "Web HTTPS"),
        };

        [Fact]
        public void A_firewall_apply_round_trips()
        {
            var line = HelperJson.Line(HelperRequest.FirewallApply(SampleRules()));
            var back = HelperJson.Parse<HelperRequest>(line);

            Assert.NotNull(back);
            Assert.Equal(HelperMessageTypes.FirewallApply, back.Type);
            Assert.Equal(3, back.Rules.Count);
            Assert.Equal(FirewallProtocol.Udp, back.Rules[1].Protocol);
            Assert.Equal(10000, back.Rules[1].StartPort);
            Assert.Equal(20000, back.Rules[1].EndPort);
            Assert.Equal("RTP UDP", back.Rules[1].Label);
            Assert.Empty(back.Validate());
        }

        [Fact]
        public void A_firewall_status_round_trips()
        {
            var back = HelperJson.Parse<HelperRequest>(HelperJson.Line(HelperRequest.FirewallStatus()));

            Assert.NotNull(back);
            Assert.Equal(HelperMessageTypes.FirewallStatus, back.Type);
            Assert.Empty(back.Rules);
            Assert.Empty(back.Validate());
        }

        [Fact]
        public void A_ping_round_trips()
        {
            var back = HelperJson.Parse<HelperRequest>(HelperJson.Line(HelperRequest.Ping()));

            Assert.NotNull(back);
            Assert.Equal(HelperMessageTypes.Ping, back.Type);
            Assert.Empty(back.Validate());
        }

        /// <summary>One object is one line, whatever it carries — that is what makes the framing work.</summary>
        [Fact]
        public void A_message_is_always_one_line()
        {
            var line = HelperJson.Line(HelperRequest.FirewallApply(SampleRules()));

            Assert.DoesNotContain("\n", line);
            Assert.DoesNotContain("\r", line);
        }

        [Fact]
        public void A_ping_reply_round_trips()
        {
            var reply = HelperReply.Succeeded(new PingResult { Version = HelperSocket.ProtocolVersion });
            var back = HelperJson.Parse<HelperReply>(HelperJson.Line(reply));

            Assert.NotNull(back);
            Assert.True(back.Ok);
            Assert.Null(back.Error);
            Assert.Equal(HelperSocket.ProtocolVersion, back.ResultAs<PingResult>().Version);
        }

        [Fact]
        public void A_status_reply_round_trips()
        {
            var applied = new DateTimeOffset(2026, 9, 23, 11, 22, 33, TimeSpan.Zero);
            var reply = HelperReply.Succeeded(new FirewallStatus { AppliedAtUtc = applied, AppliedRules = SampleRules() });

            var back = HelperJson.Parse<HelperReply>(HelperJson.Line(reply))!.ResultAs<FirewallStatus>();

            Assert.Equal(applied, back.AppliedAtUtc);
            Assert.Equal(3, back.AppliedRules.Count);
            Assert.Equal("Web HTTPS", back.AppliedRules[2].Label);
        }

        [Fact]
        public void An_error_reply_round_trips()
        {
            var back = HelperJson.Parse<HelperReply>(HelperJson.Line(HelperReply.Failed("nft refused the ruleset: syntax error.")));

            Assert.NotNull(back);
            Assert.False(back.Ok);
            Assert.Equal("nft refused the ruleset: syntax error.", back.Error);
            Assert.Null(back.Result);
        }

        /// <summary>A reply with no result is an error, not an empty status somebody would believe.</summary>
        [Fact]
        public void A_reply_with_no_result_refuses_to_be_read()
        {
            Assert.Throws<InvalidOperationException>(() => HelperReply.Failed("no").ResultAs<FirewallStatus>());
        }

        [Fact]
        public void Protocols_are_written_in_lower_case()
        {
            var line = HelperJson.Line(HelperRequest.FirewallApply(SampleRules()));

            Assert.Contains("\"protocol\":\"udp\"", line);
            Assert.Contains("\"protocol\":\"tcp\"", line);
        }

        [Theory]
        [InlineData("")]
        [InlineData("not json at all")]
        [InlineData("{\"type\":")]
        [InlineData("{\"type\":\"ping\",\"rules\":\"lots\"}")]
        public void Malformed_json_is_not_a_message(string line)
        {
            Assert.Null(HelperJson.Parse<HelperRequest>(line));
        }

        /// <summary>
        /// A protocol this system does not have a rule shape for never becomes a rule: it fails
        /// deserialization, so nothing downstream has to decide what to do with it. (Case is not
        /// part of that: "TCP" and "tcp" are the same protocol, and the serializer treats them so.)
        /// </summary>
        [Theory]
        [InlineData("sctp")]
        [InlineData("any")]
        [InlineData("icmp")]
        public void An_unknown_protocol_name_is_not_a_message(string protocol)
        {
            var line = $"{{\"type\":\"firewall.apply\",\"rules\":[{{\"protocol\":\"{protocol}\",\"startPort\":1,\"endPort\":1,\"label\":\"X\"}}]}}";

            Assert.Null(HelperJson.Parse<HelperRequest>(line));
        }

        /// <summary>
        /// Numeric enum values are refused too. The wire format is names, and accepting numbers
        /// would tie it to whatever the C# members happen to be worth.
        /// </summary>
        [Fact]
        public void A_numeric_protocol_is_not_a_message()
        {
            var line = "{\"type\":\"firewall.apply\",\"rules\":[{\"protocol\":1,\"startPort\":1,\"endPort\":1,\"label\":\"X\"}]}";

            Assert.Null(HelperJson.Parse<HelperRequest>(line));
        }

        [Fact]
        public void An_unknown_message_type_is_refused()
        {
            var request = new HelperRequest("firewall.flush");

            Assert.Contains(request.Validate(), error => error.Contains("not a message"));
        }

        [Fact]
        public void A_shell_shaped_message_type_is_refused()
        {
            var request = new HelperRequest("exec");

            Assert.NotEmpty(request.Validate());
        }

        [Fact]
        public void Rules_on_a_message_that_is_not_an_apply_are_refused()
        {
            var request = new HelperRequest(HelperMessageTypes.Ping) { Rules = SampleRules() };

            Assert.Contains(request.Validate(), error => error.Contains("does not carry firewall rules"));
        }

        [Fact]
        public void More_than_the_maximum_number_of_rules_is_refused()
        {
            var rules = Enumerable.Range(1, HelperSocket.MaxRules + 1)
                .Select(port => FirewallRule.Port(FirewallProtocol.Tcp, port, "Bulk"))
                .ToList();

            Assert.Contains(HelperRequest.FirewallApply(rules).Validate(), error => error.Contains("at most"));
        }

        [Fact]
        public void The_maximum_number_of_rules_is_allowed()
        {
            var rules = Enumerable.Range(1, HelperSocket.MaxRules)
                .Select(port => FirewallRule.Port(FirewallProtocol.Tcp, port, "Bulk"))
                .ToList();

            Assert.Empty(HelperRequest.FirewallApply(rules).Validate());
        }

        /// <summary>A bad rule is named by its position, so an admin can tell which one it was.</summary>
        [Fact]
        public void A_bad_rule_is_reported_with_its_position()
        {
            var rules = new List<FirewallRule>
            {
                FirewallRule.Port(FirewallProtocol.Udp, 5060, "SIP UDP"),
                FirewallRule.Port(FirewallProtocol.Tcp, 70000, "Nonsense"),
            };

            Assert.Contains(HelperRequest.FirewallApply(rules).Validate(), error => error.StartsWith("Rule 2:"));
        }

        /// <summary>
        /// An apply carrying nothing is a real request and means what it says: the safety rules
        /// and nothing else. It is allowed rather than guessed at.
        /// </summary>
        [Fact]
        public void An_apply_with_no_rules_is_allowed()
        {
            Assert.Empty(HelperRequest.FirewallApply(new List<FirewallRule>()).Validate());
        }

        [Fact]
        public void Every_message_type_is_known_and_nothing_else_is()
        {
            Assert.True(HelperMessageTypes.IsKnown(HelperMessageTypes.FirewallApply));
            Assert.True(HelperMessageTypes.IsKnown(HelperMessageTypes.FirewallStatus));
            Assert.True(HelperMessageTypes.IsKnown(HelperMessageTypes.Ping));
            Assert.False(HelperMessageTypes.IsKnown("run"));
            Assert.False(HelperMessageTypes.IsKnown("Ping"));
            Assert.False(HelperMessageTypes.IsKnown(""));
        }

        /// <summary>Unknown fields do not become anything: the Helper reads only what it declared.</summary>
        [Fact]
        public void Extra_fields_are_ignored()
        {
            var back = HelperJson.Parse<HelperRequest>("{\"type\":\"ping\",\"command\":\"rm -rf /\",\"path\":\"/etc\"}");

            Assert.NotNull(back);
            Assert.Equal(HelperMessageTypes.Ping, back.Type);
            Assert.Empty(back.Validate());
            Assert.Equal("{\"rules\":[],\"type\":\"ping\"}", JsonSerializer.Serialize(back, HelperJson.Serializer));
        }
    }
}
