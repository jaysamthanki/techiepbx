namespace Techie.Pbx.Asterisk.Ami
{
    /// <summary>
    /// The result of an action that answers with a list, such as PJSIPShowContacts: one response
    /// followed by an event per item.
    /// </summary>
    public class AmiEventList
    {
        public AmiEventList(AmiMessage response, List<AmiMessage> events)
        {
            Response = response;
            Events = events;
        }

        public AmiMessage Response { get; }
        public IReadOnlyList<AmiMessage> Events { get; }
    }
}
