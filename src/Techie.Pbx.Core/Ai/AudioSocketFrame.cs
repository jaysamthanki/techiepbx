namespace Techie.Pbx.Core.Ai
{
    /// <summary>One AudioSocket message: its kind byte and its payload, which may be empty (D200).</summary>
    public sealed record AudioSocketFrame(byte Kind, byte[] Payload);
}
