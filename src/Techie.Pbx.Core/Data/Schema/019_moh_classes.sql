-- Music on hold becomes several classes rather than one (D122). D119 shipped one class called
-- "parking" on one directory, because the only thing that played hold music was a parked call;
-- a system that wants a different tune for parked callers than for anything else needs more than
-- one, and res_musiconhold has always been built that way — a class is a directory.
--
-- Name is the class as Asterisk sees it: it becomes the section heading in the generated
-- musiconhold.conf and the value of parkedmusicclass. Asterisk matches class names with
-- strcasecmp (res/res_musiconhold.c, moh_class_cmp), so Name is UNIQUE with NOCASE: two classes
-- that differ only in case would be one class to Asterisk and two to an admin. For the same
-- reason the model refuses the name "default" outright — that is the class Asterisk falls back to
-- when music is asked for and none was named, and having one would play music to a parked caller
-- whose setting says silence (D119).
--
-- Directory is the one subdirectory of /var/lib/asterisk/moh that this class plays. Lower-case
-- letters, digits and dashes only, because it is built into a path and written into a conf file.
-- It is separate from Name so that renaming a class does not have to move files on disk.
--
-- IsDefault marks the class that ships with the product. It cannot be deleted: it is what the
-- installer's tracks are installed into, and it is what a system that has never been configured
-- plays.
CREATE TABLE MohClasses (
    MohClassID INTEGER PRIMARY KEY,
    Name       TEXT    NOT NULL COLLATE NOCASE UNIQUE,
    Directory  TEXT    NOT NULL UNIQUE,
    IsDefault  INTEGER NOT NULL DEFAULT 0
);

-- The class the installer fills. Named "Standard" rather than "Default": the badge in the UI says
-- which class is the default one, and a class actually called "default" is the one name Asterisk
-- reserves (see above). Its directory is still "default", which is only a path.
INSERT INTO MohClasses (Name, Directory, IsDefault) VALUES ('Standard', 'default', 1);

-- MohFiles gains the class it belongs to. SQLite cannot add a NOT NULL column with a REFERENCES
-- clause to an existing table, so the table is rebuilt; File is unique per class now rather than
-- globally, because each class plays its own directory.
CREATE TABLE MohFilesNew (
    MohFileID   INTEGER PRIMARY KEY,
    MohClassID  INTEGER NOT NULL REFERENCES MohClasses(MohClassID) ON DELETE CASCADE,
    Name        TEXT    NOT NULL,
    File        TEXT    NOT NULL,
    CreatedUnix INTEGER NOT NULL,
    UNIQUE (MohClassID, File)
);

-- The three tracks the installer transcodes into /var/lib/asterisk/moh/default, so that they are
-- listed and manageable in the UI rather than being files nobody can account for. Only on a
-- system that had no music on hold of its own: an existing install's tracks are its own business,
-- and this runs before they are copied across, so the check reads the old table.
INSERT INTO MohFilesNew (MohClassID, Name, File, CreatedUnix)
SELECT (SELECT MohClassID FROM MohClasses WHERE IsDefault = 1), t.Name, t.File, CAST(strftime('%s', 'now') AS INTEGER)
FROM (
    SELECT 'On hold' AS Name, 'default-1.g722' AS File
    UNION ALL SELECT 'Piano', 'default-2.g722'
    UNION ALL SELECT 'Bollywood', 'default-3.g722'
) AS t
WHERE NOT EXISTS (SELECT 1 FROM MohFiles);

-- Everything that was uploaded under D119 belongs to the class that ships, because that is the
-- directory it is already sitting in.
INSERT INTO MohFilesNew (MohFileID, MohClassID, Name, File, CreatedUnix)
SELECT MohFileID, (SELECT MohClassID FROM MohClasses WHERE IsDefault = 1), Name, File, CreatedUnix
FROM MohFiles;

DROP TABLE MohFiles;
ALTER TABLE MohFilesNew RENAME TO MohFiles;
