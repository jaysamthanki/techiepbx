using Techie.Pbx.Core.Models;
using Techie.Pbx.Web.Pages.Shared;

namespace Techie.Pbx.Web.Pages.Ivrs
{
    /// <summary>
    /// What the create/edit form in the modal shows, and what it posts back. The text fields are
    /// nullable because model binding turns a field the user left blank into null whatever the
    /// initialiser says.
    /// </summary>
    public class IvrForm
    {
        /// <summary>The announcement whose audio is the greeting (D58).</summary>
        public long AnnouncementID { get; set; }

        /// <summary>The final destination as one string, which is what the picker posts (D35).</summary>
        public string? Destination { get; set; } = "";

        /// <summary>The picker itself, filled in by the page. Not posted back.</summary>
        public DestinationSelect DestinationChoices { get; set; } = new();

        public string? Description { get; set; } = "";
        public bool EnableDirectDial { get; set; }
        public bool Enabled { get; set; } = true;
        public List<string> Errors { get; set; } = new();

        /// <summary>
        /// Set when the announcement this menu greets with is not one it could play any more, so
        /// the greeting box comes back empty. Worth saying out loud rather than letting a save
        /// quietly repoint the greeting.
        /// </summary>
        public bool GreetingMissing { get; set; }

        /// <summary>The announcements that have audio, to choose a greeting from. Not posted back.</summary>
        public List<Announcement> Greetings { get; set; } = new();

        public bool IsNew => this.IvrID == 0;
        public long IvrID { get; set; }

        /// <summary>
        /// The digit map editor: one row per key a caller could press, in keypad order. Posted
        /// back whole, because a save replaces the whole map (D59).
        /// </summary>
        public List<IvrKeyForm> Keys { get; set; } = new();

        public string? Name { get; set; } = "";
        public string? PlayExtension { get; set; } = "";
        public int Retries { get; set; } = 3;

        /// <summary>Whether an announcement key comes back to this menu (piece 37).</summary>
        public bool ReturnAfterAnnouncement { get; set; }

        public int TimeoutSeconds { get; set; } = 10;
    }
}
