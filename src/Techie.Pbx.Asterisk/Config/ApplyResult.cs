namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// What an apply actually did. Every list is empty when the database and the config files
    /// already agreed.
    /// </summary>
    public class ApplyResult
    {
        public ApplyResult(List<string> changedFiles, List<string> reloadedModules, List<string> restartRequiredFiles,
            bool asteriskDown = false)
        {
            ChangedFiles = changedFiles;
            ReloadedModules = reloadedModules;
            RestartRequiredFiles = restartRequiredFiles;
            AsteriskDown = asteriskDown;
        }

        public IReadOnlyList<string> ChangedFiles { get; }
        public IReadOnlyList<string> ReloadedModules { get; }

        /// <summary>
        /// True when the apply wrote the files but could not talk to Asterisk at all — normally
        /// because Asterisk has not been started yet, which is the first-run state: install.sh
        /// leaves it stopped because there is no config to read until this apply wrote one (D93).
        /// Not an error: the files on disk are current, and what is missing is a start, not a fix.
        /// </summary>
        public bool AsteriskDown { get; }

        /// <summary>Whether Asterisk is still running config we have already replaced on disk.</summary>
        public bool RestartRequired => RestartRequiredFiles.Count > 0;

        /// <summary>
        /// Files that were written but that no reload can pick up, so what Asterisk is running is
        /// not what is on disk until someone restarts it (D33).
        /// </summary>
        public IReadOnlyList<string> RestartRequiredFiles { get; }
    }
}
