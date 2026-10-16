# Security Policy

## Reporting a vulnerability

If you find a security issue in Racks, please report it privately so it can be fixed
before it is disclosed publicly.

- Preferred: open a private report via GitHub's **"Report a vulnerability"** button under
  the repository's **Security** tab (Security Advisories).
- Please include what you found, where (file and, if possible, line), and how to reproduce it.

Please do **not** open a public issue for security reports.

## Scope

Racks is a local, single-user Windows desktop app. It has no server or cloud backend, so the
relevant trust boundaries are other local processes, the file system, the network path to
GitHub's release assets, and the contents of an imported layout file. Reports about those areas
are in scope.

## What we already guard against

The codebase is reviewed against a set of security invariants documented in
[`docs/SECURITY-INVARIANTS.md`](docs/SECURITY-INVARIANTS.md), including:

- The Delete verb is blocked for files inside a rack, and dragging an item out sends the original to the Recycle Bin, never a permanent delete.
- Path guards are canonicalized (junctions, 8.3 names, `\\?\` aliases) before comparison.
- User- and import-supplied regular expressions run with a bounded match timeout (no ReDoS).
- Layout import is validated and atomic (a bad file can never wipe your racks); imported folder paths must be local drive paths, never network shares, and font names and colors (from imports or saved settings) can never name a network location.
- The updater installs a release only if its manifest is signed by the update key built into Racks, names exactly that release and a newer version, and matches the downloaded installer's size and SHA-256. The private key never touches GitHub or CI, and the owner signs only after verifying a release tag signed with a separate key and reviewing the changes, so a compromised GitHub account cannot push an installer to installs that check signatures (the first signed release and later; 1.4.0 and earlier cannot). Anything missing or mismatched fails closed. See [`docs/RELEASING.md`](docs/RELEASING.md).

## A note on code signing

Racks is not yet Authenticode code-signed, so Windows SmartScreen may warn on first run. Updates
are protected by the separate update signature above. The source is public and can be built from
scratch.
