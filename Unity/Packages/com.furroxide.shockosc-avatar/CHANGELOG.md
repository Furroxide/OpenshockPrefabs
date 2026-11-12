# Changelog

## 1.0.0 — unreleased

- Package the replacement 244-triangle badge as independently placeable single and four-instance prefabs.
- Preserve the existing ShockOSC channels, shared indicators, controller behaviour and asset GUIDs.
- Add VCC dependency declarations and a backed-up migration helper that identifies legacy assets by their own GUID.
- Move the optional optimizer preset into an importable sample and a separate manual-install archive.
- Build reproducible VPM and Unity archives from one asset list, with checksums and a published-release-only VCC listing.
- Refresh texture import settings and separate current validation from the earlier build results.

The initial public release is pending upstream redistribution terms and live OSC verification. Disposable Unity installation, migration and runtime checks passed with SDK 3.10.6, VRCFury 1.1457.0 and optional optimizer 4.6.0.

VRCFury 1.1433.0 failed the clean SDK 3.10.6 compile because its strict compiler settings reject the obsolete parameter-cost API. The minimum dependency is raised to the official 1.1457.0 release for the new validation.
