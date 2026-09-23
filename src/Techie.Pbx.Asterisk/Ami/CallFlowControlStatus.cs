using System.Net.Sockets;
using log4net;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// Which way each call flow control is set, read from astdb over AMI (F9). The state is written
    /// by the dialplan when a phone dials the code, never by this app, so this only ever reads.
    ///
    /// Like <see cref="RegistrationStatus"/>, <see cref="Read"/> never throws: a state badge is not
    /// worth failing a page over, and an unreachable Asterisk makes every switch Unknown.
    /// </summary>
    public static class CallFlowControlStatus
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(CallFlowControlStatus));

        /// <summary>
        /// What one astdb value means, the way the dialplan reads it: exactly
        /// <see cref="CallFlowControl.StateOn"/> is on, and anything else — 0, empty, no key at all
        /// — is off. Pure, so the rule is testable without a connection.
        /// </summary>
        public static CallFlowState Map(string? value) =>
            string.Equals(value, CallFlowControl.StateOn, StringComparison.Ordinal) ? CallFlowState.Override : CallFlowState.Normal;

        /// <summary>
        /// Asks Asterisk for every switch's state over one AMI connection, one DBGet each. Keyed by
        /// <see cref="CallFlowControl.CallFlowControlID"/>. If AMI is unreachable or refuses, every
        /// switch comes back <see cref="CallFlowState.Unknown"/> and the reason is logged.
        /// </summary>
        public static Dictionary<long, CallFlowState> Read(AmiSettings ami, IEnumerable<CallFlowControl> controls)
        {
            var wanted = controls.ToList();
            var states = new Dictionary<long, CallFlowState>();

            if (wanted.Count == 0)
                return states;

            try
            {
                using var client = new AmiClient(ami);
                var session = client.Connect();

                foreach (var control in wanted)
                    states[control.CallFlowControlID] = Map(session.DbGet(CallFlowControl.StateFamily, control.StateKey));

                return states;
            }
            catch (Exception ex) when (ex is AmiException or IOException or SocketException)
            {
                Log.Warn($"Could not read call flow control state over AMI: {ex.Message}");
                return wanted.ToDictionary(c => c.CallFlowControlID, _ => CallFlowState.Unknown);
            }
        }
    }
}
