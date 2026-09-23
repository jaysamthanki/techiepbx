namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>Which way a call flow control is set right now, as the UI shows it (F9).</summary>
    public enum CallFlowState
    {
        /// <summary>Asterisk could not be asked, so we genuinely do not know.</summary>
        Unknown,

        /// <summary>Switched off, or never flipped at all: calls go to the normal destination.</summary>
        Normal,

        /// <summary>Switched on: calls go to the override destination.</summary>
        Override,
    }
}
