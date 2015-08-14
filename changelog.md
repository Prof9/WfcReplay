Changelog
=========


WfcReplay v0.6 - 14 August 2015
-------------------------------
Support for a more flexible patching system was added in this version, which increases compatibility with certain problem games that have an extreme number of URLs, such as the Japanese version of Pokémon Black 2. In fact, I'd be surprised if any other game required it...

Additionally, thanks to bjoern-r, WfcReplay should now be compatible with the Mono runtime on Linux (and maybe other operating systems?). I haven't personally tested this, though. You will probably need to compile BLZ yourself in order for it to work.

* Now properly generates codes for specific problem games.
* Mono compatibility.


WfcReplay v0.5 - 26 May 2014
----------------------------
This version uses a whole new ASM URL patcher that produces much smaller codes. As a result, games that previously did not have suitable code caves are now compatible, such as Pokémon Black 2 & White 2.

* URL patcher now uses compressed string addresses.
* Various other URL patcher optimizations.


WfcReplay v0.4.1 - 25 May 2014
------------------------------
Fixes an issue introduced in the previous version when using games with uncompressed ARM9 binaries or overlays.

* Fixed "BLZ decompression failed" error when ARM9 binary or overlay is not compressed.


WfcReplay v0.4 - 25 May 2014
----------------------------
This release contains various bugfixes for a number of games, such as Zelda: Phantom Hourglass and Planet Puzzle League. It also corrects some other issues. Antipiracy-protected games remain unsupported.

* Fixed code output issue for games with invalid characters in their title IDs, fixes various games.
* Fixed code cave address alignment issue, fixes various games.
* Fixed "Could not find ARM9 hook" error when the temporary folder path contained spaces.
* Fixed crash when no HTTPS URLs were found.


WfcReplay v0.3 - 23 May 2014
----------------------------
Initial release.