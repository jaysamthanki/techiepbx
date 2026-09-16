namespace Techie.Pbx.Asterisk.Config
{
    /// <summary>
    /// One generated Asterisk config file and the module that has to be reloaded when its
    /// content changes.
    /// </summary>
    public class GeneratedFile
    {
        public GeneratedFile(string fileName, string module, string content)
        {
            FileName = fileName;
            Module = module;
            Content = content;
        }

        public string FileName { get; }
        public string Module { get; }
        public string Content { get; }
    }
}
