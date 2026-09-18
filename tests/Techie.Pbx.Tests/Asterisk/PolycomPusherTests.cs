using System.Net;
using Techie.Pbx.Asterisk.Provisioning;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The push this app sends a phone's own web UI to reload its config or reboot it (D84). The
    /// network call is replaced with a fake handler so these run without a real phone: production
    /// leaves <c>handler</c> null and gets a real one instead.
    /// </summary>
    public class PolycomPusherTests
    {
        [Fact]
        public async Task A_config_reload_push_posts_the_update_action_and_reports_success()
        {
            var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

            var sent = await PolycomPusher.PushUpdateConfig("10.8.20.31", "AdminPass123", handler);

            Assert.True(sent);
            Assert.NotNull(handler.LastRequest);
            Assert.Equal(HttpMethod.Post, handler.LastRequest!.Method);
            Assert.Equal("https://10.8.20.31/push", handler.LastRequest.RequestUri!.ToString());
            Assert.Contains("Action:UpdateConfig", handler.LastRequestBody);
            Assert.Contains("application/push-simpleui", handler.LastRequest.Headers.Accept.ToString());
        }

        [Fact]
        public async Task A_reboot_push_posts_the_reboot_action()
        {
            var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.OK));

            var sent = await PolycomPusher.PushReboot("10.8.20.31", "AdminPass123", handler);

            Assert.True(sent);
            Assert.Contains("Action:Reboot", handler.LastRequestBody);
        }

        /// <summary>A phone that refuses the credentials or the request is a warning, not an exception.</summary>
        [Fact]
        public async Task A_non_success_response_reports_failure_without_throwing()
        {
            var handler = new FakeHandler(_ => new HttpResponseMessage(HttpStatusCode.Unauthorized));

            var sent = await PolycomPusher.PushUpdateConfig("10.8.20.31", "wrong-password", handler);

            Assert.False(sent);
        }

        /// <summary>
        /// Simulates what <see cref="HttpClient.Timeout"/> does when a phone never answers: the
        /// send throws <see cref="TaskCanceledException"/> rather than the request completing.
        /// Thrown directly here rather than waiting out a real two second timeout, which would
        /// only slow the suite down for the same assertion.
        /// </summary>
        [Fact]
        public async Task A_timeout_is_reported_as_failure_not_an_exception()
        {
            var handler = new FakeHandler(_ => throw new TaskCanceledException("simulated timeout"));

            var sent = await PolycomPusher.PushUpdateConfig("10.8.20.31", "AdminPass123", handler);

            Assert.False(sent);
        }

        /// <summary>A phone that has never provisioned has no address to push to, so there is nothing to try.</summary>
        [Fact]
        public async Task An_empty_ip_is_skipped_without_a_network_call()
        {
            var handler = new FakeHandler(_ => throw new InvalidOperationException("should not be called"));

            var sent = await PolycomPusher.PushUpdateConfig("", "AdminPass123", handler);

            Assert.False(sent);
            Assert.Null(handler.LastRequest);
        }

        /// <summary>Answers with a canned response and records the request, so a test can inspect both.</summary>
        private sealed class FakeHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> respond;

            public FakeHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                this.respond = respond;
            }

            public HttpRequestMessage? LastRequest { get; private set; }
            public string LastRequestBody { get; private set; } = "";

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                this.LastRequest = request;
                this.LastRequestBody = request.Content == null ? "" : await request.Content.ReadAsStringAsync(cancellationToken);

                return this.respond(request);
            }
        }
    }
}
