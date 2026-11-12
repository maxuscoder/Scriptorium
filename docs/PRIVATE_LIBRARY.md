# Private Library

Open **Private** in the sidebar to create a password-protected library. Choose a
mode before creating the password. Both modes hide private items from public
Home, Library, Favorites, Search, Categories, Continue Watching, and Saved Moments.
Everything works offline.

| Mode | Media storage | What the password protects |
| --- | --- | --- |
| Standard | Original files stay in place, without encryption or a second media copy. | Access through Scriptorium and its encrypted private catalog. File Explorer and other players can still read originals. |
| Encrypted | A complete encrypted copy goes into `PrivateVault` beside the SQLite database. | The private catalog and vault file contents. Retained originals and other copies remain readable. |

Imports retain originals. Encrypted imports require room for the full copy plus
72 bytes per file and 16 bytes per chunk. After checking playback, select an item
and choose **Remove verified original** if you want to keep only the encrypted
copy. This action authenticates the entire vault file, hashes both streams, and
deletes the exact matching source through a locked Windows file handle. It does
not use the Recycle Bin, erase backups, or securely overwrite disk remnants.
Changed originals, linked files, inaccessible files, and damaged vaults are kept.

Use **Import files**, **Import folder**, or **Choose existing media**. Existing
items retain their metadata and saved timestamps inside the encrypted catalog.
Private browsing reuses media cards and the embedded video player, with local
title/group search, favorites, editable titles/groups, and encrypted resume
history. Artwork uses a neutral placeholder and does not enter the shared image
or disk thumbnail caches. Public saved-moment creation/export is disabled for
private playback; previously saved moments are preserved for return to the public
library.

**Return to regular library** makes the item and its history public again.
Encrypted items require a new export filename; existing files are never
overwritten. Exporting intentionally writes a readable media file. The normal
organizers restore episode/course membership when its original watched folder
still exists.

## Locking and passwords

- Use **Lock now** to clear the private view, revoke all stream handles, cancel
  transfers, clear owned key/plaintext buffers, and stop private playback.
- Automatic locking defaults to 15 minutes of inactivity. Settings in Private
  allow 0, 1, 5, 15, 30, 60, or 120 minutes. Zero means exit-only automatic
  locking. Keyboard/mouse input counts as activity; playback does not.
- Application exit and Windows session lock/logoff always lock the library.
- Change the password inside Private using the current password. Only the
  master-key envelope changes; large media files do not need re-encryption.
- There is no password recovery or in-place protection-mode switching. A forgotten
  password can permanently prevent access to the catalog and encrypted media.
  Standard-mode original media remains readable outside Scriptorium.
- App-data reset is blocked while a private library is configured or vault files
  exist, to avoid deleting the only encryption key.

## Backup and interrupted transfers

Close Scriptorium, then back up the **entire Scriptorium data directory**, including
the SQLite database and `PrivateVault`. The database contains the password-wrapped
key and encrypted item records; vault files alone are not a complete backup.
If copying a running SQLite database, use a SQLite-aware backup including its WAL.

An encrypted import writes a uniquely named encrypted `.partial` file, flushes it,
authenticates every chunk, and renames it before atomically committing the encrypted
catalog record, source exclusion, and removal of public metadata in SQLite. No
import deletes its source. A cancellation normally removes incomplete ciphertext;
a process/power failure can leave unreferenced encrypted files. Retrying uses a new
vault file ID, so these files cannot overwrite an earlier transfer. Keep orphan
files until you have checked the original and backed up the complete database.
An export failure retains the private record and ciphertext; a completed readable
export may remain if the later database commit fails. Choose another filename on
retry. Partial export files are removed on ordinary cancellation/failure.

Protection applies to indexed source paths. Other copies, aliases/hard links,
previously public thumbnails, filesystem history, old logs, database remnants,
and backups are not retroactively erased. An unlocked process, malware, an
administrator, and OS memory dumps are outside this feature's protection boundary.

## Implementation

`IPrivateLibrary` is the authenticated boundary. Public repositories never switch
to a private scope when it is unlocked. Private item payloads are AES-256-GCM
encrypted in SQLite. Public metadata is removed atomically, while normalized
SHA-256 path exclusions prevent scanners and import services from rediscovering
the same sources. EF query filters also protect media-derived entities, and SQLite
triggers reject stale public writes. Cache invalidation reloads reads that were in
flight during a transfer. Public pages refresh after transfers, and public
playback, thumbnail generation, and external launching check the exclusion guard.

The password protects a random 256-bit master key using Argon2id with a random
128-bit salt, 64 MiB memory, 3 iterations, and 4 lanes. A 96-bit random GCM nonce
and 128-bit tag protect its envelope; authenticated data binds the format and
protection mode. Password changes use a fresh salt and envelope nonce. Password
input travels from `PasswordBox.SecurePassword` through transient byte buffers,
without string bindings or persistence. Owned password/key buffers are cleared.
Failed unlock attempts have a bounded exponential delay.

The media format uses .NET `AesGcm` and HKDF-SHA-256:

| Region | Layout |
| --- | --- |
| Header | 56 bytes: `SCRPV001` (8), little-endian chunk size (4), plaintext length (8), random file salt (16), unique vault file ID (16), reserved (4). |
| Header authentication | 16-byte GCM tag over the complete header, empty plaintext, nonce index `UInt64.MaxValue`. |
| Chunk records | Ciphertext followed by a 16-byte GCM tag; the final chunk can be shorter. |
| Nonce | 4 zero bytes followed by a little-endian 64-bit chunk index; never repeated with that file key. |
| Chunk authenticated data | Complete header followed by the 64-bit chunk index. |
| File key | HKDF-SHA-256(master key, random file salt, `Scriptorium/private/file/v1`), 32 bytes. |

Every import gets a fresh vault ID and random salt. The encrypted catalog binds
the expected vault ID, preventing substitution of another complete vault file.
Header authentication precedes allocating chunk buffers; exact ciphertext length
rejects truncation and appended bytes. Moving/swapping chunks fails authentication.
`VaultReadStream` uses checked 64-bit offsets, asynchronous random-access reads,
one cached plaintext chunk and one ciphertext buffer, serialized access, and
session cancellation. Plaintext is copied to callers only after its chunk tag
verifies. LibVLC receives a bounded `StreamMediaInput`; callback failures return
an error and stop playback. Streams are disposed after native teardown. No
decrypted playback files are created.

The default chunk size is **1 MiB**. A local 24 MiB cached-I/O comparison measured
1/2/4 MiB chunks at approximately 0.18/0.31/0.52 ms per random 64 KiB read, with
similar sequential decryption throughput (about 0.8 GiB/s including hashing).
These measurements are host-specific, not sustained disk-throughput guarantees.
One MiB limits seek amplification and uses about 2 MiB of chunk buffers per player.
.NET uses the platform AES-GCM implementation and hardware acceleration when
available. Argon2id runs during authentication/password changes, not per chunk.

References: [Microsoft AES-GCM nonce requirements](https://learn.microsoft.com/dotnet/api/system.security.cryptography.aesgcm.encrypt),
[Konscious Argon2 implementation](https://github.com/kmaragon/Konscious.Security.Cryptography),
[VideoLAN stream callbacks](https://github.com/videolan/libvlcsharp/blob/3.x/src/LibVLCSharp/Shared/StreamMediaInput.cs).

## Verification

```powershell
dotnet build Scriptorium.sln --no-restore
dotnet test Scriptorium.sln --no-restore
dotnet test tests/Scriptorium.Tests --no-restore --filter FullyQualifiedName~VaultReadStreamTests --logger "console;verbosity=detailed"
```

Tests cover both modes, authentication, lock/reopen, password rewrapping, configured
automatic locking, private UI bindings, file/folder import wiring, private search
and editing, public query/cache isolation, scanner write rejection, source
preservation on cancellation, verified original removal, exports, corruption,
chunk swapping, header tampering, wrong keys/identities, truncation/appending, and
1/2/4 MiB chunk evaluation. Native LibVLC tests play and seek standard and encrypted
fragmented MP4 streams in an embedded HWND. A sparse 101 GiB encrypted fixture
verifies authenticated seeking at 50/100 GiB and EOF without allocating 101 GiB of
test media. Full-duration playback of a physical 50–100 GB file is not covered by
these automated tests and should be exercised on target storage before release.
