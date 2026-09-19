-- D110: how many devices may register to one extension at once (office phone + softphone).
-- Default 1 keeps every existing endpoint exactly as it is: one device, and a second
-- registration still replaces the first.

ALTER TABLE Extensions ADD COLUMN MaxContacts INTEGER NOT NULL DEFAULT 1;
