-- A recorded message a caller hears: "we are closed for the holiday", "calls may be recorded".
-- The audio is a file on disk, converted on upload to the one format this system stores (D55);
-- this table holds what the announcement is called, what its file is called, and the number a
-- user can dial to hear it.
--
-- PlayExtension is optional. When it is set it is dialled like an extension, so it lives in the
-- same number space as extensions and ring groups and is collision-checked against both (D57).
-- When it is not set, the announcement has no dialplan entry and cannot be a destination.
CREATE TABLE Announcements (
    AnnouncementID INTEGER PRIMARY KEY,
    Name           TEXT    NOT NULL UNIQUE,
    Description    TEXT    NOT NULL DEFAULT '',
    PlayExtension  TEXT    NOT NULL DEFAULT '',
    AudioFile      TEXT    NOT NULL DEFAULT '',
    Enabled        INTEGER NOT NULL DEFAULT 1
);
