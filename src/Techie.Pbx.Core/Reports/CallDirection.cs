namespace Techie.Pbx.Core.Reports
{
    /// <summary>Which way a call record went, relative to the trunks (F5). Never stored: see <see cref="CdrChannels"/>.</summary>
    public enum CallDirection
    {
        Internal,
        Inbound,
        Outbound,
    }
}
