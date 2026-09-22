-- The number an inbound caller dialled (F5). The trunk context reads it from the To header and
-- then sends the call into internal, so by the time anything is dialled the record's Dst is the
-- extension, not the DID. The dialplan copies the DID into the CDR userfield, and
-- cdr_manager.conf maps that onto the Cdr event as Did.
--
-- NULL for every other call, and for inbound calls recorded before this column existed: the
-- reports fall back to Dst for those.
ALTER TABLE Cdrs ADD COLUMN Did TEXT NULL;
