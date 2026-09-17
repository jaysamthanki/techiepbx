using System.Globalization;
using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Announcements
{
    /// <summary>
    /// The announcements page: a recorded message a caller hears (D55, D56). Built like the other
    /// list pages — a shell htmx fills, forms in the shared Bootstrap modal (D42), rows that open
    /// their own edit form (D48).
    ///
    /// What is different here is that one save touches two things: a row in the database and a
    /// file on disk. The order is deliberate and is described on <see cref="OnPostSave"/>.
    /// </summary>
    [RequestSizeLimit(AnnouncementStore.MaxUploadBytes + (1024 * 1024))]
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly AnnouncementRepository announcements;
        private readonly AnnouncementStore store;

        public IndexModel()
        {
            this.announcements = new AnnouncementRepository(PbxDatabase.Current);
            this.store = PbxSounds.Current;
        }

        public void OnGet()
        {
        }

        /// <summary>The create or edit form, which the page shows in the Bootstrap modal.</summary>
        public IActionResult OnGetForm(long? announcementID)
        {
            if (announcementID is null or 0)
                return this.Partial("_Form", new AnnouncementForm());

            var announcement = this.announcements.GetByID(announcementID.Value);
            if (announcement == null)
                return this.NotFound();

            return this.Partial("_Form", this.Fill(new AnnouncementForm
            {
                AnnouncementID = announcement.AnnouncementID,
                Description = announcement.Description,
                Enabled = announcement.Enabled,
                Name = announcement.Name,
                PlayExtension = announcement.PlayExtension,
            }, announcement));
        }

        /// <summary>The whole table, by name.</summary>
        public PartialViewResult OnGetTable()
        {
            var rows = this.announcements.GetAll()
                .Select(announcement =>
                {
                    var audio = this.store.Describe(announcement);

                    return new AnnouncementRow
                    {
                        AnnouncementID = announcement.AnnouncementID,
                        Audio = Summarise(announcement, audio),
                        AudioUsable = audio != null,
                        Description = announcement.Description,
                        Enabled = announcement.Enabled,
                        Name = announcement.Name,
                        PlayExtension = announcement.PlayExtension.Length > 0 ? announcement.PlayExtension : "—",
                    };
                })
                .ToList();

            return this.Partial("_Table", rows);
        }

        /// <summary>
        /// Deletes the announcement and its audio. The row goes first: a file left behind is
        /// rubbish in a directory, whereas a row left behind is an announcement that still appears
        /// in every destination picker and plays nothing.
        /// </summary>
        public IActionResult OnPostDelete(long announcementID)
        {
            var announcement = this.announcements.GetByID(announcementID);
            if (announcement == null)
                return this.NotFound();

            this.announcements.Delete(announcementID);

            try
            {
                this.store.Delete(announcement);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The announcement is gone either way, and nothing plays the file any more.
                Log.Warn($"Announcement '{announcement.Name}' was deleted but its audio could not be removed: {ex.Message}");
            }

            Log.Info($"Announcement '{announcement.Name}' deleted by {this.User.Identity?.Name}");
            return this.Changed($"Announcement '{announcement.Name}' deleted.");
        }

        /// <summary>
        /// Creates or updates one announcement, and replaces its audio when a file came with it.
        ///
        /// The order matters. Conversion happens first, in a temporary directory, so that an
        /// upload ffmpeg refuses — or an ffmpeg that is not installed at all — comes back as a
        /// message on the form with nothing written anywhere. Only then is the row saved, and only
        /// then does the converted file take its place on disk. An announcement being created has
        /// no ID until the row exists, so a new one is inserted first and its audio placed after;
        /// if that placement fails, the announcement exists with the audio missing, which the table
        /// and the form both say plainly rather than pretending.
        /// </summary>
        public IActionResult OnPostSave(AnnouncementForm form)
        {
            var isNew = form.AnnouncementID == 0;
            var announcement = isNew ? new Announcement() : this.announcements.GetByID(form.AnnouncementID);
            if (announcement == null)
                return this.NotFound();

            var previousFile = announcement.AudioFile;

            announcement.Description = Text(form.Description);
            announcement.Enabled = form.Enabled;
            announcement.Name = Text(form.Name);
            announcement.PlayExtension = Text(form.PlayExtension);

            // The stored file is named after the announcement, so renaming it renames the file.
            var uploaded = form.Audio is { Length: > 0 };
            if (uploaded || previousFile.Length > 0)
                announcement.AudioFile = Announcement.FileNameFor(announcement.Name);

            try
            {
                if (isNew)
                    this.announcements.Insert(announcement);
                else
                    this.announcements.Update(announcement);
            }
            catch (ValidationFailedException ex)
            {
                announcement.AudioFile = previousFile;
                form.Errors = ex.Errors.ToList();
                return this.Partial("_Form", this.Fill(form, announcement));
            }

            try
            {
                if (uploaded)
                {
                    using var upload = form.Audio!.OpenReadStream();
                    this.store.Save(announcement, upload);
                }
                else if (previousFile.Length > 0)
                {
                    this.store.Rename(announcement.AnnouncementID, previousFile, announcement.AudioFile);
                }
            }
            catch (AudioUploadException ex)
            {
                // The row is saved; only the audio is not. Say so rather than claiming a clean save.
                Log.Warn($"Announcement '{announcement.Name}' saved, but its audio was refused: {ex.Message}");
                form.AnnouncementID = announcement.AnnouncementID;
                form.Errors = new List<string> { ex.Message };
                return this.Partial("_Form", this.Fill(form, announcement));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Announcement '{announcement.Name}' saved, but its audio could not be written: {ex.Message}", ex);
                form.AnnouncementID = announcement.AnnouncementID;
                form.Errors = new List<string>
                {
                    $"The announcement was saved, but its audio could not be written to {this.store.SoundsPath}. " +
                    "Check that the directory exists and that the web user may write to it.",
                };
                return this.Partial("_Form", this.Fill(form, announcement));
            }

            Log.Info($"Announcement '{announcement.Name}' {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}" +
                     (uploaded ? " with new audio" : ""));
            return this.Changed($"Announcement '{announcement.Name}' saved.");
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary>
        /// The audio as a line of text: how long it runs and how big it is, or why there is none.
        /// Read off the file rather than out of the database, so it cannot claim audio that is not
        /// there (D55).
        /// </summary>
        private static string Summarise(Announcement announcement, AnnouncementAudio? audio)
        {
            if (audio != null)
                return $"{audio.Seconds.ToString("0.#", CultureInfo.InvariantCulture)} s, {audio.Bytes / 1024} KB";

            return announcement.HasAudio ? "File missing" : "No audio";
        }

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to.
        /// "announcementsChanged" refreshes the table, "configChanged" wakes the navbar's apply
        /// button (D43), "pbxToast" says what happened.
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["announcementsChanged"] = null,
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>
        /// What the form cannot know for itself: whether there is audio on disk for this
        /// announcement and how it reads.
        /// </summary>
        private AnnouncementForm Fill(AnnouncementForm form, Announcement announcement)
        {
            var audio = announcement.AnnouncementID > 0 ? this.store.Describe(announcement) : null;

            form.AudioMissing = audio == null && announcement.HasAudio;
            form.AudioSummary = Summarise(announcement, audio);
            form.HasAudio = audio != null;

            return form;
        }
    }
}
