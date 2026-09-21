using System.Globalization;
using System.Text.Json;
using log4net;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.RazorPages;
using Techie.Pbx.Asterisk.Audio;
using Techie.Pbx.Asterisk.Config;
using Techie.Pbx.Core;
using Techie.Pbx.Core.Data;
using Techie.Pbx.Core.Models;

namespace Techie.Pbx.Web.Pages.Moh
{
    /// <summary>
    /// The Music on hold page: the classes, and the tracks in them (D122). A class is a name
    /// Asterisk knows and a directory it plays, so this page is two tables — one of classes, one of
    /// every track with the class it is in — and both use the same modal-and-htmx pattern as the
    /// rest of the list pages (D42, D48).
    ///
    /// The tracks half is what used to sit on the Parking page under D119, upload and conversion
    /// included; what is new is that a track belongs to a class, and so lands in that class's
    /// directory. A track cannot be moved between classes after it is created: that would be
    /// moving a file on disk to make a dropdown true, and deleting it and uploading it again says
    /// the same thing with no new failure to explain.
    /// </summary>
    [RequestSizeLimit(MohStore.MaxUploadBytes + (1024 * 1024))]
    public class IndexModel : PageModel
    {
        private static readonly ILog Log = LogManager.GetLogger(typeof(IndexModel));

        private readonly MohClassRepository mohClasses;
        private readonly MohFileRepository mohFiles;
        private readonly SettingsRepository settings;
        private readonly MohStore store;

        private string? parkingClass;

        /// <summary>
        /// The class the parking settings point at, read once per request rather than once per row
        /// of the table. Blank is impossible — the setting falls back to the class that ships — so
        /// this is never "no class".
        /// </summary>
        private string ParkingClass =>
            this.parkingClass ??= AsteriskSettings.Parking(this.settings.GetAll()).MusicClass.Trim();

        public IndexModel()
        {
            this.mohClasses = new MohClassRepository(PbxDatabase.Current);
            this.mohFiles = new MohFileRepository(PbxDatabase.Current);
            this.settings = new SettingsRepository(PbxDatabase.Current);
            this.store = new MohStore();
        }

        public void OnGet()
        {
        }

        /// <summary>The create or edit form for a class, which the page shows in the modal.</summary>
        public IActionResult OnGetClassForm(long? mohClassID)
        {
            if (mohClassID is null or 0)
                return this.Partial("_ClassForm", new MohClassForm());

            var mohClass = this.mohClasses.GetByID(mohClassID.Value);
            if (mohClass == null)
                return this.NotFound();

            return this.Partial("_ClassForm", new MohClassForm
            {
                Directory = mohClass.Directory,
                IsDefault = mohClass.IsDefault,
                IsParkingClass = this.IsParkingClass(mohClass),
                MohClassID = mohClass.MohClassID,
                Name = mohClass.Name,
                Tracks = this.mohFiles.CountsByClass().GetValueOrDefault(mohClass.MohClassID),
            });
        }

        /// <summary>
        /// The class table: what each class is called, what it plays, how many tracks are in it,
        /// and what points at it.
        /// </summary>
        public PartialViewResult OnGetClasses()
        {
            var counts = this.mohFiles.CountsByClass();
            var rows = this.mohClasses.GetAll().Select(mohClass => this.Row(mohClass, counts)).ToList();

            return this.Partial("_Classes", rows);
        }

        /// <summary>The create or edit form for a track, which the page shows in the modal.</summary>
        public IActionResult OnGetTrackForm(long? mohFileID)
        {
            var counts = this.mohFiles.CountsByClass();
            var classes = this.mohClasses.GetAll();

            if (mohFileID is null or 0)
            {
                return this.Partial("_TrackForm", new MohTrackForm
                {
                    Classes = classes.Select(mohClass => this.Row(mohClass, counts)).ToList(),
                    MohClassID = classes.FirstOrDefault()?.MohClassID ?? 0,
                });
            }

            var file = this.mohFiles.GetByID(mohFileID.Value);
            if (file == null)
                return this.NotFound();

            var owner = classes.FirstOrDefault(c => c.MohClassID == file.MohClassID);
            if (owner == null)
                return this.NotFound();

            return this.Partial("_TrackForm", this.Fill(new MohTrackForm
            {
                ClassName = owner.Name,
                MohClassID = file.MohClassID,
                MohFileID = file.MohFileID,
                Name = file.Name,
            }, owner, file));
        }

        /// <summary>
        /// Every track of every class, in the order Asterisk will play them: grouped by class, and
        /// within a class by file name, because that is what <c>sort = alpha</c> does (D119).
        /// </summary>
        public PartialViewResult OnGetTracks()
        {
            var classes = this.mohClasses.GetAll().ToDictionary(c => c.MohClassID);

            var rows = this.mohFiles.GetAll()
                .Where(file => classes.ContainsKey(file.MohClassID))
                .Select(file =>
                {
                    var owner = classes[file.MohClassID];
                    var audio = this.store.Describe(owner, file);

                    return new MohTrackRow
                    {
                        Audio = Summarise(file, audio),
                        AudioUsable = audio != null,
                        ClassName = owner.Name,
                        File = file.HasAudio ? file.File : "—",
                        MohFileID = file.MohFileID,
                        Name = file.Name,
                    };
                })
                .ToList();

            return this.Partial("_Tracks", rows);
        }

        /// <summary>
        /// Deletes a class, its tracks' rows and its directory. The rows go with the class through
        /// the foreign key; the files have to be taken out here, or they are music in a directory
        /// nothing names any more.
        /// </summary>
        public IActionResult OnPostDeleteClass(long mohClassID)
        {
            var mohClass = this.mohClasses.GetByID(mohClassID);
            if (mohClass == null)
                return this.NotFound();

            if (this.IsParkingClass(mohClass))
                return this.Refused($"'{mohClass.Name}' is the class parked callers hear. Point the parking " +
                                    "settings at another class first.");

            try
            {
                this.mohClasses.Delete(mohClassID);
            }
            catch (ValidationFailedException ex)
            {
                return this.Refused(string.Join(" ", ex.Errors));
            }

            try
            {
                this.store.DeleteClass(mohClass);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The rows are gone, but the files are not. Nothing plays them now — no class
                // names that directory — but say so rather than claiming a clean delete.
                Log.Warn($"Music on hold class '{mohClass.Name}' was deleted but its directory could not be removed: {ex.Message}");
            }

            Log.Info($"Music on hold class '{mohClass.Name}' deleted by {this.User.Identity?.Name}");
            return this.Changed($"Music on hold class '{mohClass.Name}' deleted.");
        }

        /// <summary>
        /// Deletes the track and its file. The row goes first: a file left behind is music
        /// Asterisk would still play, whereas a row left behind is one line in a table.
        /// </summary>
        public IActionResult OnPostDeleteTrack(long mohFileID)
        {
            var file = this.mohFiles.GetByID(mohFileID);
            if (file == null)
                return this.NotFound();

            var owner = this.mohClasses.GetByID(file.MohClassID);

            this.mohFiles.Delete(mohFileID);

            try
            {
                if (owner != null)
                    this.store.Delete(owner, file);
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                // The row is gone, but the file is not — and in a directory a class plays whole
                // that means Asterisk carries on playing it. Say so rather than claiming a clean
                // delete.
                Log.Warn($"Music on hold track '{file.Name}' was deleted but its file could not be removed: {ex.Message}");
            }

            Log.Info($"Music on hold track '{file.Name}' deleted by {this.User.Identity?.Name}");
            return this.Changed($"Music on hold track '{file.Name}' deleted.");
        }

        /// <summary>
        /// Creates or edits one class. Changing the directory moves the music that is already in
        /// it, so that the tracks follow the class rather than being left where the generated conf
        /// file no longer looks.
        /// </summary>
        public IActionResult OnPostSaveClass(MohClassForm form)
        {
            var isNew = form.MohClassID == 0;
            var existing = isNew ? null : this.mohClasses.GetByID(form.MohClassID);

            if (!isNew && existing == null)
                return this.NotFound();

            var mohClass = new MohClass
            {
                Directory = Text(form.Directory),
                IsDefault = existing?.IsDefault ?? false,
                MohClassID = form.MohClassID,
                Name = Text(form.Name),
            };

            // A new class with no directory typed in gets one derived from its name, the way a
            // track's file name is derived from the track's: a path is not something to make an
            // admin invent.
            if (mohClass.Directory.Length == 0)
                mohClass.Directory = MohClass.DirectoryFor(mohClass.Name);

            try
            {
                if (isNew)
                    this.mohClasses.Insert(mohClass);
                else
                    this.mohClasses.Update(mohClass);
            }
            catch (ValidationFailedException ex)
            {
                form.Errors = ex.Errors.ToList();
                return this.Partial("_ClassForm", this.Fill(form, existing));
            }

            // Parking names its class by name, so renaming the class parked callers hear would
            // otherwise point the setting at nothing — which is silence, quietly. The setting
            // follows the rename rather than being left behind.
            var followed = existing != null
                && this.IsParkingClass(existing)
                && !string.Equals(existing.Name, mohClass.Name, StringComparison.Ordinal);

            if (followed)
            {
                this.settings.Set(SettingsKeys.ParkingMusicClass, mohClass.Name);
                Log.Info($"Parking.MusicClass followed the rename of '{existing!.Name}' to '{mohClass.Name}'");
            }

            if (existing == null || !string.Equals(existing.Directory, mohClass.Directory, StringComparison.Ordinal))
            {
                try
                {
                    // A new class gets its directory now rather than at the first upload: it is
                    // written into musiconhold.conf either way, and res_musiconhold warns about a
                    // directory it cannot enter.
                    if (existing == null)
                        this.store.CreateClass(mohClass);
                    else
                        this.store.RenameClass(existing, mohClass);
                }
                catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
                {
                    Log.Error($"Music on hold class '{mohClass.Name}' was saved, but {this.store.MohPath} could not be changed: {ex.Message}", ex);
                    form.Errors = new List<string>
                    {
                        existing == null
                            ? $"The class was saved, but its directory could not be created under {this.store.MohPath}. " +
                              "Check that the directory exists and that the web user may write to it."
                            : $"The class was saved, but its tracks could not be moved to {mohClass.Directory}. " +
                              "Upload them again, or put the directory back to what it was.",
                    };
                    return this.Partial("_ClassForm", this.Fill(form, mohClass));
                }
            }

            Log.Info($"Music on hold class '{mohClass.Name}' {(isNew ? "created" : "updated")} by {this.User.Identity?.Name}");
            return this.Changed($"Music on hold class '{mohClass.Name}' saved." +
                                (followed ? " Parked callers still hear it: the parking setting followed the new name." : ""));
        }

        /// <summary>
        /// Creates or renames one track, and replaces its audio when a file came with it.
        ///
        /// The order is the announcements page's (D55), for the same reasons. A new track has no
        /// ID until its row exists and the stored file name is derived from that ID, so the row is
        /// inserted first, the audio placed second, and the row updated with the file name it
        /// ended up with. A conversion ffmpeg refuses comes back as a message on the form with
        /// nothing on disk touched; a placement that fails leaves a track with no audio, which the
        /// table and the form both say plainly rather than pretending.
        /// </summary>
        public IActionResult OnPostSaveTrack(MohTrackForm form)
        {
            var isNew = form.MohFileID == 0;
            var file = isNew
                ? new MohFile { CreatedUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds(), MohClassID = form.MohClassID }
                : this.mohFiles.GetByID(form.MohFileID);

            if (file == null)
                return this.NotFound();

            // The class is the row's, never the form's, once the track exists: a track is not
            // moved between classes, because that is a file on disk rather than a column.
            var owner = this.mohClasses.GetByID(file.MohClassID);
            if (owner == null)
            {
                form.Errors = new List<string> { "Choose a music on hold class to put the track in." };
                return this.Partial("_TrackForm", this.Fill(form, null, file));
            }

            var previousFile = file.File;
            file.Name = Text(form.Name);
            form.ClassName = owner.Name;

            var uploaded = form.Audio is { Length: > 0 };

            // A track with no audio at all is not worth creating: nothing would play it, and there
            // is no other thing a music on hold row can be.
            if (isNew && !uploaded)
            {
                form.Errors = new List<string> { "Choose a file to upload. A music on hold track is the file." };
                return this.Partial("_TrackForm", this.Fill(form, owner, file));
            }

            try
            {
                // Either way the row comes back naming the file it should be stored as: the name
                // is derived from the ID and the track's name, so the repository owns it.
                if (isNew)
                    this.mohFiles.Insert(file);
                else
                    this.mohFiles.Update(file);
            }
            catch (ValidationFailedException ex)
            {
                file.File = previousFile;
                form.Errors = ex.Errors.ToList();
                return this.Partial("_TrackForm", this.Fill(form, owner, file));
            }

            try
            {
                if (uploaded)
                {
                    using var upload = form.Audio!.OpenReadStream();
                    this.store.Save(owner, file, upload, previousFile);
                }
                else if (previousFile.Length > 0)
                {
                    this.store.Rename(owner, file.MohFileID, previousFile, file.File);
                }
            }
            catch (AudioUploadException ex)
            {
                Log.Warn($"Music on hold track '{file.Name}' saved, but its audio was refused: {ex.Message}");
                form.MohFileID = file.MohFileID;
                form.Errors = new List<string> { ex.Message };
                return this.Partial("_TrackForm", this.Fill(form, owner, file));
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
            {
                Log.Error($"Music on hold track '{file.Name}' saved, but its audio could not be written: {ex.Message}", ex);
                form.MohFileID = file.MohFileID;
                form.Errors = new List<string>
                {
                    $"The track was saved, but its audio could not be written to {this.store.ClassPath(owner)}. " +
                    "Check that the directory exists and that the web user may write to it.",
                };
                return this.Partial("_TrackForm", this.Fill(form, owner, file));
            }

            Log.Info($"Music on hold track '{file.Name}' {(isNew ? "created" : "updated")} in class '{owner.Name}' " +
                     $"by {this.User.Identity?.Name}" + (uploaded ? " with new audio" : ""));
            return this.Changed($"Music on hold track '{file.Name}' saved.");
        }

        /// <summary>
        /// The audio as a line of text: how long it runs and how big it is, or why there is none.
        /// Read off the file rather than out of the database, so it cannot claim audio that is not
        /// there. Minutes rather than seconds, because hold music is measured in them.
        /// </summary>
        private static string Summarise(MohFile file, AnnouncementAudio? audio)
        {
            if (audio != null)
            {
                var minutes = audio.Seconds / 60;
                return $"{minutes.ToString("0.#", CultureInfo.InvariantCulture)} min, {audio.Bytes / 1024} KB";
            }

            return file.HasAudio ? "File missing" : "No audio";
        }

        /// <summary>A posted field, trimmed. A field the user left blank arrives as null.</summary>
        private static string Text(string? value) => (value ?? "").Trim();

        /// <summary>
        /// The answer to a change: no content to swap, and events for the page to react to. Both
        /// tables listen for both events, because a class change moves the track counts and a
        /// track change moves them back; "configChanged" wakes the navbar's apply button (D43) —
        /// musiconhold.conf is generated from these rows — and "pbxToast" says what happened.
        /// </summary>
        private IActionResult Changed(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["mohClassesChanged"] = null,
                ["mohFilesChanged"] = null,
                ["configChanged"] = null,
                ["pbxToast"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>What the class form cannot know for itself once a save has come back.</summary>
        private MohClassForm Fill(MohClassForm form, MohClass? mohClass)
        {
            form.IsDefault = mohClass?.IsDefault ?? false;
            form.IsParkingClass = mohClass != null && this.IsParkingClass(mohClass);
            form.Tracks = mohClass == null ? 0 : this.mohFiles.CountsByClass().GetValueOrDefault(mohClass.MohClassID);

            return form;
        }

        /// <summary>
        /// What the track form cannot know for itself: which classes there are to choose from,
        /// whether there is audio on disk for this track, and how it reads.
        /// </summary>
        private MohTrackForm Fill(MohTrackForm form, MohClass? owner, MohFile file)
        {
            var counts = this.mohFiles.CountsByClass();
            var audio = owner != null && file.MohFileID > 0 ? this.store.Describe(owner, file) : null;

            form.AudioMissing = audio == null && file.HasAudio;
            form.AudioSummary = Summarise(file, audio);
            form.Classes = this.mohClasses.GetAll().Select(mohClass => this.Row(mohClass, counts)).ToList();
            form.HasAudio = audio != null;

            return form;
        }

        /// <summary>
        /// Whether the parking settings name this class, which is what stops it being deleted out
        /// from under a parked caller. Matched without regard to case, because that is how Asterisk
        /// matches a class name.
        /// </summary>
        private bool IsParkingClass(MohClass mohClass) =>
            string.Equals(this.ParkingClass, mohClass.Name.Trim(), StringComparison.OrdinalIgnoreCase);

        /// <summary>
        /// The answer to something this page will not do: no content, an alert rather than a toast
        /// because it needs reading, and nothing refreshed because nothing changed.
        /// </summary>
        private IActionResult Refused(string message)
        {
            var events = new Dictionary<string, object?>
            {
                ["pbxAlert"] = new { message },
            };

            this.Response.Headers["HX-Trigger"] = JsonSerializer.Serialize(events);
            return new StatusCodeResult(StatusCodes.Status204NoContent);
        }

        /// <summary>One line of the class table, which the track form reuses as its class list.</summary>
        private MohClassRow Row(MohClass mohClass, IReadOnlyDictionary<long, int> counts) => new()
        {
            Directory = MohStore.ConfDirectory(mohClass),
            IsDefault = mohClass.IsDefault,
            IsParkingClass = this.IsParkingClass(mohClass),
            MohClassID = mohClass.MohClassID,
            Name = mohClass.Name,
            Tracks = counts.GetValueOrDefault(mohClass.MohClassID),
        };
    }
}
