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

- Files inside a rack cannot be deleted through the app; removal goes to the Recycle Bin.
- Path guards are canonicalized (junctions, 8.3 names, `\\?\` aliases) before comparison.
- User- and import-supplied regular expressions run with a bounded match timeout (no ReDoS).
- Layout import is validated and atomic (a bad file can never wipe your racks).
- The updater only downloads the expected signed release asset from the project's GitHub repo.

## A note on code signing

Racks is not yet code-signed, so Windows SmartScreen may warn on first run and the updater
cannot verify an Authenticode signature. This is a known limitation for an independent
open-source project; the source is public and can be built from scratch.
