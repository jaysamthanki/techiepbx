-- D125. What an outbound call calls out as, and what its caller hears when the far side holds them.
--
-- Three columns, one decision. The caller ID an outbound call presents is chosen by the most
-- specific thing that named one: the extension that dialled, then the route it matched, then the
-- trunk's own callerid in pjsip.conf — which is untouched and stays the fallback it always was.
--
-- Extensions.OutboundCallerID is the user with a direct DID of their own. It is written into that
-- extension's endpoint as set_var = TNPBX_CID=<value>, so chan_pjsip puts the claim on every
-- channel the phone creates and no route has to know the extension exists. Empty means no claim,
-- which is every extension until somebody sets one.
--
-- OutboundRoutes.CallerID is FreePBX's "option CID": what this route calls out as when the
-- extension claimed nothing. Empty means none, which is what every route did before this column,
-- and then the trunk's callerid is what says who we are.
--
-- OutboundRoutes.MohClassID is the outbound twin of InboundRoutes.MohClassID (021, D122 amended):
-- the class an outbound caller hears while the person they rang has them on hold. Nullable, null by
-- default, and ON DELETE SET NULL for the same reasons as the inbound one — deleting a class must
-- not delete the route that named it, and a route pointing at a class that is gone would be a name
-- the renderer refuses to write.
--
-- SQLite can only add a column with a REFERENCES clause when its default is NULL, which is what
-- this is, so no table rebuild is needed for any of the three.
ALTER TABLE Extensions     ADD COLUMN OutboundCallerID TEXT NOT NULL DEFAULT '';
ALTER TABLE OutboundRoutes ADD COLUMN CallerID         TEXT NOT NULL DEFAULT '';

ALTER TABLE OutboundRoutes
    ADD COLUMN MohClassID INTEGER NULL REFERENCES MohClasses(MohClassID) ON DELETE SET NULL;
