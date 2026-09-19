-- D110 amendment: max contacts became a global setting (Sip.MaxContacts) rather than a
-- per-extension column, decided the same day. 015 already ran on the lab VM, so this drops
-- the column it added; a fresh install runs 015 then 016 and ends up with neither.

ALTER TABLE Extensions DROP COLUMN MaxContacts;
