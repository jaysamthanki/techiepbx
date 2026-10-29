-- What an AI agent says just before it puts a caller through to an extension the model chose
-- (F11, D203). Canned, so the transfer costs no second model round trip; blank means the default
-- line, "One moment, putting you through."
ALTER TABLE AiAgents ADD COLUMN TransferLine TEXT NOT NULL DEFAULT '';
