# Music on hold source material

Three royalty-free tracks from Audiodollar (audiodollar.com), placed here for the
MOH feature. Filenames carry the source IDs, e.g. 371876, for attribution.

- audiodollar-on-hold-music-371876.mp3
- audiodollar-piano-piano-inspirational-music-570589.mp3
- audiodollar-bollywood-bollywood-551817.mp3

Asterisk here is built without format_mp3, so these are not usable as-is: when a
track is picked in the Parking/MOH UI it must be transcoded to a native format
(G.722, matching D117) before it lands in /var/lib/asterisk/moh. This directory
is source material only, not served or shipped to phones.
