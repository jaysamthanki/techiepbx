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

        private static AiChatReply Read(string json) => AiProviders.ReadOpenRouterChatReply(Encoding.UTF8.GetBytes(json));

        [Fact]
        public void With_no_extensions_no_tools_are_sent()
        {
            var request = AiProviders.OpenRouterChatRequest("vendor/model", Messages, []);

            Assert.False(request.ContainsKey("tools"));
            Assert.Equal("vendor/model", (string?)request["model"]);
            Assert.Equal(2, request["messages"]!.AsArray().Count);
            Assert.Equal("Anishi please.", (string?)request["messages"]![1]!["content"]);
        }

        [Fact]
        public void The_transfer_tool_offers_exactly_the_extensions_and_names_them()
        {
            var request = AiProviders.OpenRouterChatRequest("vendor/model", Messages, new[] { ("101", "Jaysam"), ("102", "Anishi") });

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
        /// <summary>
        /// The Ollama body (D204): the same messages and tools as OpenRouter's, but one JSON object
        /// answered in one piece (stream: false) and the limits under Ollama's own option names.
        /// </summary>
        [Fact]
        public void The_ollama_request_asks_for_one_piece_and_carries_the_tools()
        {
            var request = AiProviders.OllamaChatRequest(
                "llama3.2",
                new[] { ("system", "You are the receptionist."), ("user", "Can I speak to Anishi?") },
                new[] { ("102", "Anishi") });

            Assert.Equal("llama3.2", request["model"]!.GetValue<string>());
            Assert.False(request["stream"]!.GetValue<bool>());
            Assert.Equal(0.2, request["options"]!["temperature"]!.GetValue<double>());
            Assert.Equal(250, request["options"]!["num_predict"]!.GetValue<int>());
            Assert.Equal(2, request["messages"]!.AsArray().Count);

            var tool = request["tools"]!.AsArray()[0]!["function"]!;
            Assert.Equal(AiProviders.TransferToolName, tool["name"]!.GetValue<string>());
            Assert.Contains("102", tool["parameters"]!["properties"]!["extension"]!["enum"]!.AsArray().Select(v => v!.GetValue<string>()));
        }

        /// <summary>An Ollama reply carries its tool call's arguments as an object, not a string.</summary>
        [Fact]
        public void An_ollama_tool_call_is_read_with_object_arguments()
        {
            var reply = AiProviders.ReadOllamaChatReply(
                """{"message":{"role":"assistant","content":"","tool_calls":[{"function":{"name":"transfer_call","arguments":{"extension":"102"}}}]},"done":true}"""u8.ToArray());

            Assert.Equal(AiProviders.TransferToolName, reply.ToolCall!.Name);
            Assert.Equal("102", reply.ToolCall.Argument("extension"));
        }

        [Fact]
        public void An_ollama_reply_with_no_tool_call_is_just_content()
        {
            var reply = AiProviders.ReadOllamaChatReply(
                """{"message":{"role":"assistant","content":"We open at nine."},"done":true}"""u8.ToArray());

            Assert.Equal("We open at nine.", reply.Content);
            Assert.Null(reply.ToolCall);
        }

    }
}
