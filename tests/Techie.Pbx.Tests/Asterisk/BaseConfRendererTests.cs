using Techie.Pbx.Asterisk.Ami;
using Techie.Pbx.Asterisk.Config;

namespace Techie.Pbx.Tests.Asterisk
{
    /// <summary>
    /// The base config: asterisk.conf, modules.conf, logger.conf, rtp.conf and manager.conf.
    /// Four of them are the same on every install; manager.conf comes from the AMI settings.
    /// </summary>
    public class BaseConfRendererTests
    {
        private static AmiSettings SampleAmi() => new()
        {
            Username = "tnpbx",
            Secret = "not-a-real-secret",
        };

        private static string Expected(string fileName) =>
            File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Expected", fileName)).ReplaceLineEndings("\n");

        [Fact]
        public void Asterisk_matches_expected_file()
        {
            Assert.Equal(Expected("asterisk.conf"), AsteriskConfRenderer.Render());
        }

        [Fact]
        public void Logger_matches_expected_file()
        {
            Assert.Equal(Expected("logger.conf"), LoggerConfRenderer.Render());
        }

        [Fact]
        public void Manager_matches_expected_file()
        {
            Assert.Equal(Expected("manager.conf"), ManagerConfRenderer.Render(SampleAmi()));
        }

        [Fact]
        public void Modules_matches_expected_file()
        {
            Assert.Equal(Expected("modules.conf"), ModulesConfRenderer.Render());
        }

        [Fact]
        public void Rtp_matches_expected_file()
        {
            Assert.Equal(Expected("rtp.conf"), RtpConfRenderer.Render());
        }

        /// <summary>
        /// With a STUN server set, rtp.conf gains stunaddr and ICE. Asterisk 22's
        /// res_rtp_asterisk takes stunaddr as host or host:port and re-resolves it periodically;
        /// checked on the lab VM before this was written (D72).
        /// </summary>
        [Fact]
        public void Rtp_with_stun_matches_expected_file()
        {
            var transport = new PjsipTransport { StunServer = "stun.l.google.com:19302" };

            Assert.Equal(Expected("rtp-stun.conf"), RtpConfRenderer.Render(transport));
        }

        /// <summary>
        /// Nothing about STUN or ICE is written unless something asked for it: the file a plain
        /// server generates is the file it generated before these settings existed (D72).
        /// </summary>
        [Fact]
        public void Rtp_says_nothing_about_stun_or_ice_by_default()
        {
            var actual = RtpConfRenderer.Render(new PjsipTransport());

            Assert.DoesNotContain("stunaddr", actual);
            Assert.DoesNotContain("icesupport", actual);
        }

        /// <summary>
        /// An external address is the other way of knowing what the outside looks like, so it
        /// turns ICE on by itself — but it is not a STUN server, so no stunaddr is written (D72).
        /// </summary>
        [Fact]
        public void An_external_address_turns_ice_on_without_a_stun_server()
        {
            var transport = new PjsipTransport
            {
                ExternalAddress = "203.0.113.10",
                LocalNets = { "10.8.20.0/24" },
            };

            var actual = RtpConfRenderer.Render(transport);

            Assert.Contains("icesupport = yes\n", actual);
            Assert.DoesNotContain("stunaddr", actual);
        }

        /// <summary>
        /// rtp.conf is read at startup only, so the applier has to report it as needing a restart
        /// rather than pretending a reload picked it up (D33).
        /// </summary>
        [Fact]
        public void Rtp_conf_is_a_restart_not_a_reload()
        {
            var file = new GeneratedFile("rtp.conf", null, RtpConfRenderer.Render());

            Assert.True(file.NeedsRestart);
            Assert.Empty(ConfigApplier.ReloadOrder(new List<GeneratedFile> { file }));
        }

        /// <summary>
        /// The point of the file is that Asterisk loads what we listed and nothing else (D31).
        /// </summary>
        [Fact]
        public void Modules_are_an_allowlist_not_an_autoload_with_exceptions()
        {
            var actual = ModulesConfRenderer.Render();

            Assert.Contains("autoload = no\n", actual);
            Assert.DoesNotContain("autoload = yes", actual);
            Assert.DoesNotContain("noload", actual);
        }

        [Fact]
        public void Every_module_is_named_once_and_looks_like_a_module()
        {
            var modules = ModulesConfRenderer.Modules;

            Assert.All(modules, module => Assert.EndsWith(".so", module));
            Assert.Equal(modules.Count, modules.Distinct(StringComparer.Ordinal).Count());
            Assert.Equal(modules.Count, ModulesConfRenderer.Render().Split("load = ").Length - 2);
        }

        /// <summary>
        /// Every module a generated dialplan needs is on the list, because a missing one shows up
        /// as a feature that quietly does not work (D31). The IVR menus are the newest claim on
        /// it: Playback for the prompts and TIMEOUT(digit) for the gap between keys (D61).
        /// Answer, Background, WaitExten, Set, Goto and GotoIf are Asterisk builtins and need no
        /// module, which is why none is listed for them.
        /// </summary>
        [Theory]
        [InlineData("pbx_config.so")]
        [InlineData("app_dial.so")]
        [InlineData("app_playback.so")]
        [InlineData("app_voicemail.so")]
        [InlineData("func_timeout.so")]
        public void The_allowlist_carries_what_the_dialplan_calls(string module)
        {
            Assert.Contains(module, ModulesConfRenderer.Modules);
        }

        /// <summary>
        /// The things a PBX gets attacked through that we do not use: other channel drivers,
        /// anonymous SIP identification, and the HTTP server.
        /// </summary>
        [Theory]
        [InlineData("chan_sip")]
        [InlineData("chan_iax2")]
        [InlineData("chan_skinny")]
        [InlineData("res_pjsip_endpoint_identifier_anonymous")]
        [InlineData("res_http_websocket")]
        [InlineData("res_agi")]
        public void The_allowlist_leaves_out_what_we_do_not_use(string module)
        {
            Assert.DoesNotContain(module, ModulesConfRenderer.Render());
        }

        [Fact]
        public void The_rtp_range_the_firewall_will_open_is_the_range_in_the_file()
        {
            var actual = RtpConfRenderer.Render();

            Assert.Contains($"rtpstart = {RtpConfRenderer.PortStart}\n", actual);
            Assert.Contains($"rtpend = {RtpConfRenderer.PortEnd}\n", actual);
            Assert.True(RtpConfRenderer.PortStart < RtpConfRenderer.PortEnd);
        }

        [Fact]
        public void Manager_binds_to_loopback_and_permits_nothing_else()
        {
            var actual = ManagerConfRenderer.Render(SampleAmi());

            Assert.Contains("bindaddr = 127.0.0.1\n", actual);
            Assert.Contains("deny = 0.0.0.0/0.0.0.0\n", actual);
            Assert.Contains("permit = 127.0.0.1/255.255.255.255\n", actual);
            Assert.Contains("webenabled = no\n", actual);
        }

        /// <summary>
        /// A setting that says AMI is somewhere else is a mistake we refuse to write out, rather
        /// than one we discover as a broken apply on a live system (D32).
        /// </summary>
        [Theory]
        [InlineData("10.8.20.8")]
        [InlineData("0.0.0.0")]
        [InlineData("::1")]
        public void Manager_refuses_to_render_for_an_ami_host_that_is_not_loopback(string host)
        {
            var ami = SampleAmi();
            ami.Host = host;

            var ex = Assert.Throws<InvalidOperationException>(() => ManagerConfRenderer.Render(ami));

            Assert.Contains("127.0.0.1", ex.Message);
        }

        [Fact]
        public void Manager_accepts_localhost_as_well_as_the_address()
        {
            var ami = SampleAmi();
            ami.Host = "localhost";

            Assert.Contains("bindaddr = 127.0.0.1\n", ManagerConfRenderer.Render(ami));
        }

        /// <summary>An AMI account with no password on a box we generate config for: never.</summary>
        [Fact]
        public void Manager_refuses_to_render_without_a_username_and_secret()
        {
            Assert.Throws<InvalidOperationException>(() => ManagerConfRenderer.Render(new AmiSettings()));
            Assert.Throws<InvalidOperationException>(() =>
                ManagerConfRenderer.Render(new AmiSettings { Username = "tnpbx", Secret = "" }));
        }

        /// <summary>The AMI user does not need Action: Command, so it does not get it (D17).</summary>
        [Fact]
        public void The_ami_account_gets_no_command_permission()
        {
            var actual = ManagerConfRenderer.Render(SampleAmi());

            Assert.Contains("write = system,config\n", actual);
            Assert.DoesNotContain("command", actual);
            Assert.DoesNotContain("originate", actual);
        }

        [Theory]
        [InlineData("tnpbx]\n[evil", "not-a-real-secret")]
        [InlineData("tnpbx", "secret; deny = 0.0.0.0")]
        public void Manager_refuses_a_username_or_secret_that_would_break_out_of_its_section(string username, string secret)
        {
            var ami = new AmiSettings { Username = username, Secret = secret };

            Assert.Throws<InvalidOperationException>(() => ManagerConfRenderer.Render(ami));
        }

        [Fact]
        public void Manager_uses_the_port_the_app_connects_to()
        {
            var ami = SampleAmi();
            ami.Port = 5039;

            Assert.Contains("port = 5039\n", ManagerConfRenderer.Render(ami));
        }
    }
}
