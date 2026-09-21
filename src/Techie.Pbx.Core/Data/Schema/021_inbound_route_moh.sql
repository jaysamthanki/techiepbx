-- The music an inbound caller hears while they are held becomes the route's own choice (D122
-- amended). Until now the only thing that named a class was call parking, so a caller a
-- receptionist put on hold heard whatever Asterisk fell back to, which is a class called
-- "default" that this system deliberately never writes (D119, D122) — silence. FreePBX lets each
-- inbound route pick its class, and the difference between the main line and the support line is
-- exactly the kind of thing a site wants to hear.
--
-- Nullable, and null is the default: no class named, which leaves the channel exactly as every
-- route left it before this column existed. A route that names one gets
-- Set(CHANNEL(musicclass)=<name>) in the trunk's context before the call is handed on.
--
-- ON DELETE SET NULL rather than a cascade or a refusal: deleting a class must not delete the
-- route that mentioned it, and a route pointing at a class that is gone would be a name the
-- renderer refuses to write. Reverting to "no class named" is the one answer that keeps the call
-- arriving.
--
-- SQLite can only add a column with a REFERENCES clause when its default is NULL, which is what
-- this is, so no table rebuild is needed.
ALTER TABLE InboundRoutes
    ADD COLUMN MohClassID INTEGER NULL REFERENCES MohClasses(MohClassID) ON DELETE SET NULL;
