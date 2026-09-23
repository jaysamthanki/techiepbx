namespace Techie.Pbx.Helper
{
    /// <summary>A system account as the password database gives it: the name asked for, and its IDs.</summary>
    public record SystemUser(string Name, uint Uid, uint Gid);
}
