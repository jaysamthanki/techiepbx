namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// One generated Asterisk config file and the module that has to be reloaded when its content
    /// changes. A null module means no reload can apply this file: Asterisk reads it at startup
    /// only, so a change waits for a restart (D33).
    /// </summary>
    public class GeneratedFile
    {
        public GeneratedFile(string fileName, string? module, string content)
        {
            FileName = fileName;
            Module = module;
            Content = content;
        }

        public string Content { get; }
        public string FileName { get; }
        public string? Module { get; }

        /// <summary>Whether this file can only be picked up by restarting Asterisk.</summary>
        public bool NeedsRestart => Module == null;
    }
}
