-- The music on hold tracks an admin has uploaded. One music on hold class, one directory, one
-- row per file in it (D119): there is no per-class UI, because a PBX this size has one hold
-- tune, not a library.
--
-- File is what the track is actually stored as under /var/lib/asterisk/moh, derived from Name
-- and the row's own ID when the audio is saved and never from the uploaded file name. It is
-- UNIQUE because res_musiconhold scans that one directory: two rows naming the same file would
-- be one track pretending to be two.
--
-- Name is not unique. Unlike an announcement, a music on hold track is never referred to by
-- name from anywhere else — the class names the directory, not the files — so two tracks called
-- the same thing are untidy rather than ambiguous.
CREATE TABLE MohFiles (
    MohFileID   INTEGER PRIMARY KEY,
    Name        TEXT    NOT NULL,
    File        TEXT    NOT NULL UNIQUE,
    CreatedUnix INTEGER NOT NULL
);
