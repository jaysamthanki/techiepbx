-- Voicemail is one optional mailbox per extension, so it is columns on Extensions rather than a
-- table of its own (D27). Disabled by default: an extension only gets a mailbox when asked for.
-- Email delivery itself is not built yet (F4), but the settings are stored now.
ALTER TABLE Extensions ADD COLUMN VoicemailEnabled          INTEGER NOT NULL DEFAULT 0;
ALTER TABLE Extensions ADD COLUMN VoicemailPin              TEXT    NOT NULL DEFAULT '';
ALTER TABLE Extensions ADD COLUMN VoicemailEmail            TEXT    NOT NULL DEFAULT '';
ALTER TABLE Extensions ADD COLUMN VoicemailAttachRecording  INTEGER NOT NULL DEFAULT 1;
ALTER TABLE Extensions ADD COLUMN VoicemailDeleteAfterEmail INTEGER NOT NULL DEFAULT 0;
