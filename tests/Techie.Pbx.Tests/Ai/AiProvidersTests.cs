using System.Text;
using System.Text.Json.Nodes;
using Techie.Pbx.Core.Ai;

namespace Techie.Pbx.Tests.Ai
{
    /// <summary>
    /// The chat request and answer around the transfer tool (D203), checked without a network:
    /// the tool and its enum only when there are extensions to offer, and a tool call read back.
    /// </summary>
    public class AiProvidersTests
    {
        private static readonly IReadOnlyList<(string Role, string Content)> Messages = new[] { ("system", "Be brief."), ("user", "Anishi please.") };

        private static AiChatReply Read(string json) => AiProviders.ReadChatReply(Encoding.UTF8.GetBytes(json));

        [Fact]
        public void With_no_extensions_no_tools_are_sent()
        {
            var request = AiProviders.ChatRequest("vendor/model", Messages, []);

            Assert.False(request.ContainsKey("tools"));
            Assert.Equal("vendor/model", (string?)request["model"]);
            Assert.Equal(2, request["messages"]!.AsArray().Count);
            Assert.Equal("Anishi please.", (string?)request["messages"]![1]!["content"]);
        }

        [Fact]
        public void The_transfer_tool_offers_exactly_the_extensions_and_names_them()
        {
            var request = AiProviders.ChatRequest("vendor/model", Messages, new[] { ("101", "Jaysam"), ("102", "Anishi") });

            var tool = Assert.Single(request["tools"]!.AsArray())!;
            var function = tool["function"]!;
            var extension = function["parameters"]!["properties"]!["extension"]!;

            Assert.Equal("function", (string?)tool["type"]);
            Assert.Equal(AiProviders.TransferToolName, (string?)function["name"]);
            Assert.Contains("101 Jaysam, 102 Anishi", (string?)function["description"], StringComparison.Ordinal);
            Assert.Equal("string", (string?)extension["type"]);
            Assert.Equal(new[] { "101", "102" }, extension["enum"]!.AsArray().Select(e => (string?)e));
            Assert.Equal(new[] { "extension" }, function["parameters"]!["required"]!.AsArray().Select(e => (string?)e));
        }

        [Fact]
        public void A_plain_reply_has_content_and_no_tool_call()
        {
            var reply = Read("""{"choices":[{"message":{"role":"assistant","content":"We open at nine."}}]}""");

            Assert.Equal("We open at nine.", reply.Content);
            Assert.Null(reply.ToolCall);
        }

        [Fact]
        public void A_tool_call_is_read_with_its_arguments()
        {
            var reply = Read(
                """{"choices":[{"message":{"role":"assistant","content":null,"tool_calls":[{"id":"call_1","type":"function","function":{"name":"transfer_call","arguments":"{\"extension\":\"102\"}"}}]}}]}""");

            Assert.Equal("", reply.Content);
            Assert.Equal(AiProviders.TransferToolName, reply.ToolCall!.Name);
            Assert.Equal("102", reply.ToolCall.Argument("extension"));
        }

        [Fact]
        public void A_reply_with_neither_content_nor_a_tool_call_is_empty()
        {
            var reply = Read("""{"choices":[{"message":{"role":"assistant"}}]}""");

            Assert.Equal("", reply.Content);
            Assert.Null(reply.ToolCall);
        }

        [Fact]
        public void An_answer_that_is_not_a_chat_completion_is_a_provider_failure()
        {
            Assert.Throws<AiProviderException>(() => Read("""{"error":"nope"}"""));
            Assert.Throws<AiProviderException>(() => Read("not json"));
        }

        [Theory]
        [InlineData("not json")]
        [InlineData("[\"102\"]")]
        [InlineData("{\"extension\":102}")]
        [InlineData("{}")]
        public void Arguments_that_do_not_carry_the_extension_as_a_string_have_none(string arguments)
        {
            Assert.Null(new AiToolCall(AiProviders.TransferToolName, arguments).Argument("extension"));
        }
    }
}
