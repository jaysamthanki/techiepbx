namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// What an apply actually did. Every list is empty when the database and the config files
    /// already agreed.
    /// </summary>
    public class ApplyResult
    {
        public ApplyResult(List<string> changedFiles, List<string> reloadedModules, List<string> restartRequiredFiles)
        {
            ChangedFiles = changedFiles;
            ReloadedModules = reloadedModules;
            RestartRequiredFiles = restartRequiredFiles;
        }

        public IReadOnlyList<string> ChangedFiles { get; }
        public IReadOnlyList<string> ReloadedModules { get; }

        /// <summary>Whether Asterisk is still running config we have already replaced on disk.</summary>
        public bool RestartRequired => RestartRequiredFiles.Count > 0;

        /// <summary>
        /// Files that were written but that no reload can pick up, so what Asterisk is running is
        /// not what is on disk until someone restarts it (D33).
        /// </summary>
        public IReadOnlyList<string> RestartRequiredFiles { get; }
    }
}
