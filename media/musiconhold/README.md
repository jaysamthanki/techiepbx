# Music on hold source material

Three royalty-free tracks from Audiodollar (audiodollar.com), placed here for the
MOH feature. Filenames carry the source IDs, e.g. 371876, for attribution.

- audiodollar-on-hold-music-371876.mp3
- audiodollar-piano-piano-inspirational-music-570589.mp3
- audiodollar-bollywood-bollywood-551817.mp3

Asterisk here is built without format_mp3, so these are not usable as-is. The
installer (`install.sh`, and `build-asterisk-vm.sh` on the lab VM) transcodes them
with `ffmpeg -ar 8000 -ac 1 -sample_fmt s16` into
`/var/lib/asterisk/moh/default/default-{1,2,3}.wav` — the directory of the Default
music on hold class, which the application's schema lists a row per track for
(D122). That is the same 16-bit 8 kHz mono PCM WAV the upload form produces, and
what `format_wav` plays with no transcoding at call time (D55).

This directory is source material only: the MP3s are never served, shipped to a
phone, or copied onto the server as they are.
