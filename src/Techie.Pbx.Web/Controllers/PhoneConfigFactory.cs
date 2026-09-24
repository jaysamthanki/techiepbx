using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Asterisk.Provisioning;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Controllers
{
    /// <summary>
    /// The one place a phone's configuration object is assembled from its row, its keys, the
    /// extensions and the settings — for both brands, and for every caller: the provisioning
    /// controllers, which answer a phone, and the Phones page's "view config" preview, which
    /// answers an admin (D121). Before this existed the same assembly was written out in four
    /// places and they drifted: the preview's Yealink URL was missing its slash and showed a
    /// host that does not exist, while the phone's real config was right. One factory, one
    /// truth: a preview can no longer show anything the phone would not get.
    /// </summary>
    public static class PhoneConfigFactory
    {
        /// <summary>The bind address that means "every interface", where the request host is the only honest name.</summary>
        private const string AnyAddress = "0.0.0.0";

        /// <summary>
        /// The host a phone should know the PBX by: the <c>System.Hostname</c> setting when the
        /// site has one (D105), otherwise the host the request just reached us on.
        /// </summary>
        public static string HostName(Dictionary<string, string> stored, string requestHost)
        {
            stored.TryGetValue(SettingsKeys.SystemHostname, out var hostname);

            var trimmed = (hostname ?? "").Trim();
            return trimmed.Length > 0 ? trimmed : requestHost;
        }

        /// <summary>The address the phone should register SIP to: the bind address unless it is "every interface".</summary>
        public static string ServerAddress(PjsipTransport transport, string hostName) =>
            transport.BindAddress.Length == 0 || transport.BindAddress == AnyAddress ? hostName : transport.BindAddress;

        /// <summary>
        /// A Polycom's configuration, exactly as the provisioning controller would build it. The
        /// background image, when the site has one, is named by an absolute URL built the way the
        /// Yealink provisioning URL is — the request's scheme and the phone-facing host name — on
        /// the <c>/polycom</c> route, so the phone fetches it through the same gate it fetched
        /// this config through (D145, D151).
        /// </summary>
        public static PolycomConfig Polycom(
            Phone phone, List<PhoneButton> usable, List<Extension> allExtensions, List<CallFlowControl> callFlowControls,
            Dictionary<string, string> stored, PjsipTransport transport,
            string requestScheme, string requestHost, BackgroundImage? background)
        {
            stored.TryGetValue(SettingsKeys.ProvisioningAdminPassword, out var adminPassword);
            stored.TryGetValue(SettingsKeys.ProvisioningUserPassword, out var userPassword);

            var hostName = HostName(stored, requestHost);

            // The same object features.conf is generated from, so the Park soft key sends the
            // code Asterisk is actually listening for and appears only when there is a lot to
            // park in (D144).
            var parking = AsteriskSettings.Parking(stored);

            var backgroundUrl = background == null
                ? ""
                : requestScheme + "://" + hostName + PolycomController.RoutePrefix + "/" + PolycomFiles.BackgroundFileName(background.Format);

            return new PolycomConfig
            {
                AdminPassword = (adminPassword ?? "").Trim(),
                BackgroundUrl = backgroundUrl,
                Buttons = usable,
                CallFlowControls = callFlowControls,
                Extensions = allExtensions,
                GmtOffsetSeconds = PolycomConfig.GmtOffsetFor(AsteriskSettings.Timezone(stored)),
                ParkDtmfCode = parking.DtmfCode,
                ParkEnabled = parking.Enabled,
                Phone = phone,
                ServerAddress = ServerAddress(transport, hostName),
                SipPort = transport.Port,
                SntpAddress = AsteriskSettings.NtpServer(stored),
                UserPassword = (userPassword ?? "").Trim(),
            };
        }

        /// <summary>
        /// A Yealink's configuration, exactly as the provisioning controller would build it. The
        /// provisioning URL is written into the file itself so a phone provisioned once keeps
        /// coming back on its own schedule (D88).
        /// </summary>
        public static YealinkConfig Yealink(
            Phone phone, List<PhoneButton> usable, List<Extension> allExtensions, List<CallFlowControl> callFlowControls,
            Dictionary<string, string> stored, PjsipTransport transport,
            string requestScheme, string requestHost)
        {
            stored.TryGetValue(SettingsKeys.ProvisioningUsername, out var provisioningUsername);
            stored.TryGetValue(SettingsKeys.ProvisioningPassword, out var provisioningPassword);
            stored.TryGetValue(SettingsKeys.ProvisioningAdminPassword, out var adminPassword);
            stored.TryGetValue(SettingsKeys.ProvisioningUserPassword, out var userPassword);

            var hostName = HostName(stored, requestHost);

            return new YealinkConfig
            {
                AdminPassword = (adminPassword ?? "").Trim(),
                Buttons = usable,
                CallFlowControls = callFlowControls,
                Codecs = transport.Codecs,
                Extensions = allExtensions,
                NtpServer = AsteriskSettings.NtpServer(stored),
                Phone = phone,
                ProvisioningPassword = (provisioningPassword ?? "").Trim(),
                ProvisioningUrl = requestScheme + "://" + hostName + YealinkController.RoutePrefix,
                ProvisioningUsername = (provisioningUsername ?? "").Trim(),
                ServerAddress = ServerAddress(transport, hostName),
                SipPort = transport.Port,
                TimeZoneOffset = YealinkConfig.TimeZoneOffsetFor(AsteriskSettings.Timezone(stored)),
                UserPassword = (userPassword ?? "").Trim(),
            };
        }
    }
}
