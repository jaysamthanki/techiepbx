namespace Techie.Pbx.Core.Migration
{
    /// <summary>One <c>sounds[]</c> element of manifest v1: a file under <c>files/sounds/</c>.</summary>
    public class ManifestSound
    {
        public string? AnnouncementName { get; set; }
        public string? Filename { get; set; }
    }
}
