using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Core.Migration
{
    /// <summary>One announcement the import will create, and the export file its audio comes from.</summary>
    public class PlannedSound
    {
        public Announcement Announcement { get; set; } = new();

        /// <summary>
        /// Whether the converter can read the source at all: WAV and MP3 yes, FreePBX's raw
        /// telephony formats (<c>.gsm</c>, <c>.g722</c>, <c>.sln</c>…) no. An announcement whose
        /// only copy is raw is still created, with no audio, and the preview says so.
        /// </summary>
        public bool Convertible { get; set; }

        /// <summary>Other files with the same name in another format, which are not imported.</summary>
        public List<string> IgnoredFiles { get; set; } = new();

        /// <summary>The manifest's file name, e.g. <c>en/custom/Main.wav</c>.</summary>
        public string SourceFile { get; set; } = "";

        /// <summary>The file's full path in the staging directory, already checked to be inside it.</summary>
        public string SourcePath { get; set; } = "";
    }
}
