# Changelog

## 0.1.0 — initial development release, 2026-11-12

- Package the replacement 244-triangle badge as independently placeable single and four-instance prefabs.
- Preserve the existing ShockOSC channels, shared indicators, controller behaviour and asset GUIDs.
- Install one shared Shocker toggle per avatar instead of duplicating it for every badge.
- Add local, unsaved OpenShock configuration controls for pause, fixed/random intensity and duration, cooldown and hold time under `ShockOsc/Settings`.
- Retain the configuration parameters when using the optional optimizer preset.
- Add VCC dependency declarations and a backed-up migration helper that identifies legacy assets by their own GUID.
- Cover every asset in the current manual archives when switching to VCC, including the new menu scripts and assemblies.
- Move the optional optimizer preset into an importable sample and a separate manual-install archive.
- Build reproducible VPM and Unity archives from one asset list, with checksums and a published-release-only VCC listing.
- Refresh texture import settings and separate current validation from the earlier build results.

Published for installation testing with the project owner's authorization. Upstream redistribution terms remain unconfirmed and live VRChat/bridge OSC verification remains pending. This development release does not establish an upstream licence grant. Version `0.1.0` has no prerelease suffix and appears in VCC without enabling pre-release packages.

Disposable Unity installation, migration and runtime checks use SDK 3.10.6, VRCFury 1.1457.0 and optional optimizer 4.6.0. The package preserves the existing badge channels, attachment, contact and indicator behaviour while installing one shared menu per avatar.

VRCFury 1.1433.0 failed the clean SDK 3.10.6 compile because its strict compiler settings reject the obsolete parameter-cost API. The minimum dependency is raised to the official 1.1457.0 release for the new validation.
