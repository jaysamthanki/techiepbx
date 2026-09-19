-- D109: digits a route adds in front of the dialled number (PrependDigits) and how many
-- leading dialled digits it drops before sending (StripDigits). Both empty/zero by default,
-- so every existing route keeps sending exactly what the caller dialled.

ALTER TABLE OutboundRoutes ADD COLUMN PrependDigits TEXT    NOT NULL DEFAULT '';
ALTER TABLE OutboundRoutes ADD COLUMN StripDigits   INTEGER NOT NULL DEFAULT 0;
