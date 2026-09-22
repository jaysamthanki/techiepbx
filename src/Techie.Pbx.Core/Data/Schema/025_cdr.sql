-- Call detail records (F5): one row per Cdr event cdr_manager sends over AMI, collected by the
-- app into this database rather than by one of Asterisk's own CDR backends, so the reports are
-- plain SQL over one database and how long they are kept is ours to decide. For now that is
-- forever: nothing deletes from this table.
--
-- A row is a CDR record, not a call. Asterisk writes several records with the same UniqueID for
-- one call: one per phone a ring-all Dial rang, and another for whatever the dialplan did after an
-- unanswered Dial, voicemail for instance. So UniqueID alone is not unique. What is unique is
-- UniqueID with Sequence, a counter the CDR engine gives every record it creates; both it and
-- LinkedID arrive only because cdr_manager.conf maps them onto the event. Should Sequence ever
-- arrive empty, it is NULL, and SQLite never treats two NULLs as a clash: the row is kept rather
-- than silently dropped.
--
-- Direction (inbound, outbound, internal) is not stored. It is derived when the report is read,
-- from which channels are trunks, so it follows the trunks as they are now.
--
-- Times are ISO-8601 UTC text ("2026-09-22T14:03:05Z"), which sorts and compares as text.
-- Disposition is Asterisk's own word: ANSWERED, NO ANSWER, BUSY, FAILED, CONGESTION or CANCEL.
CREATE TABLE Cdrs (
    CdrID              INTEGER PRIMARY KEY,
    UniqueID           TEXT    NOT NULL,
    Sequence           INTEGER NULL,
    LinkedID           TEXT    NULL,
    Src                TEXT    NOT NULL DEFAULT '',
    Dst                TEXT    NOT NULL DEFAULT '',
    Dcontext           TEXT    NOT NULL DEFAULT '',
    CallerID           TEXT    NULL,
    Channel            TEXT    NOT NULL DEFAULT '',
    DestinationChannel TEXT    NULL,
    LastApplication    TEXT    NULL,
    LastData           TEXT    NULL,
    Disposition        TEXT    NOT NULL,
    AmaFlags           TEXT    NULL,
    AccountCode        TEXT    NULL,
    StartUtc           TEXT    NOT NULL,
    AnswerUtc          TEXT    NULL,
    EndUtc             TEXT    NULL,
    DurationSeconds    INTEGER NULL,
    BillSecSeconds     INTEGER NULL,
    UNIQUE (UniqueID, Sequence)
);

CREATE INDEX IX_Cdrs_StartUtc ON Cdrs (StartUtc);
CREATE INDEX IX_Cdrs_Src ON Cdrs (Src);
CREATE INDEX IX_Cdrs_Dst ON Cdrs (Dst);
