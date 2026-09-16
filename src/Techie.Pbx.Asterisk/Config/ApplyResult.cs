namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// What an apply actually did. Both lists are empty when the database and the config files
    /// already agreed.
    /// </summary>
    public class ApplyResult
    {
        public ApplyResult(List<string> changedFiles, List<string> reloadedModules)
        {
            ChangedFiles = changedFiles;
            ReloadedModules = reloadedModules;
        }

        public IReadOnlyList<string> ChangedFiles { get; }
        public IReadOnlyList<string> ReloadedModules { get; }
    }
}
