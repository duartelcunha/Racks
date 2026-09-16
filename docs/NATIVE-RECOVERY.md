# Native Windows recovery

The product direction is the original Windows WPF Racks, refined with native
Coss controls. Shared safety code and the deferred Avalonia source/tests remain.
No corrected beta has been published.

## Withdrawal

On 16 September 2026, the beta feed was replaced with a signed empty appcast.
Its public HTTPS contents and Ed25519 signature were verified before
`v2.0.0-beta.1` was returned to draft. Feed commit:
`2ad2197c7bd2397ffb8c6b559a93e73ceed89f11`.
The tag, all four release assets, history and stable release were retained.
Installed beta users are not downgraded automatically.

## First review slice

- Real original WPF racks, shell icons and image previews, with Coss light/dark
  presets, native context menu styles and the existing appearance controls.
- Explicit global appearance defaults and existing rack settings are retained.
  New racks use Coss unless the user has saved appearance defaults.
- `--profile <directory> --design-preview` creates an isolated comparison.
  Personal registry import, desktop attachment, global hooks and Explorer mirror
  changes are skipped. Preview fixture creation refuses to overwrite files.
- Isolated registry compatibility uses versioned atomic JSON with a backup,
  preserving value types and ordered arrays. Production settings still use the
  existing registry contract; production JSON migration is not complete.
- Native physics elapsed time uses a monotonic clock.
- The old shell thumbnail path produced blank ordinary-file icons on the target
  machine. The rack now shares the native thumbnail service with Finder, calls
  Windows' image factory on a background thread, and frees its COM/GDI handles.
  The slice smoke requires all five real fixture icons/previews after restart.

Run `scripts/Test-All.ps1` for core and Windows regressions, the native slice
startup/restart check, preserved shared-app smoke and signing tests.
`scripts/Test-NativePreview.ps1` retains its two reports in
`.artifacts/native-preview-<id>/`. These smoke checks do not certify appearance,
cross-application drag, accessibility, or presentation performance.

## Review and release gates

The agreed next gate is user review of the running rack, menu and appearance
dialog in both themes. Apply the design to the remaining surfaces only after
that direction is accepted.

Still required before a corrected `2.0.0-beta.2` release:

- Native management, Finder and organization surfaces using accepted controls.
- Every native file flow connected to the shared operation coordinator, including
  collisions, cancellation, persistent undo and interrupted-operation recovery.
- Complete native JSON settings and settings-only selection when registry and
  beta profiles both exist, preserving IDs, references, appearance and ordering.
- UI-independent signed updater with WPF status, pause, safe restart and
  prevention of new operations during installation.
- Recycling virtualization, bounded asynchronous thumbnails, reduced motion,
  keyboard and accessibility acceptance across the actual application.
- Real Explorer drag, migration and disposable-VM installer/update acceptance.
- 100%, 150% and 200% visual inspection, including long names and backgrounds.
- Presented-frame measurements: average >=59 fps over 30 seconds; p95 <=18 ms;
  p99 <=33.4 ms. Callback measurements cannot pass this gate.
- Settled idle CPU <0.02 cores over 30 seconds without recurring animation or
  repeated position saves.

The new Windows build has not passed these release gates. Mac remains deferred.
