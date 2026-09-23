namespace Techie.Pbx.Contracts
{
    /// <summary>
    /// The transport a firewall rule opens. Serialized as "tcp" or "udp" (D142): a string, so a
    /// message is readable in a log, and an enum, so anything that is not one of these two is a
    /// deserialization failure rather than a value that reaches the generated ruleset.
    ///
    /// There is deliberately no zero member. A message that leaves the field out deserializes to
    /// 0, which is not a member, and <see cref="FirewallRule.Validate"/> refuses it rather than
    /// guessing at TCP.
    /// </summary>
    public enum FirewallProtocol
    {
        Tcp = 1,
        Udp = 2,
    }
}
