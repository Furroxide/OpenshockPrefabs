# Development and release

The maintained source is `Unity/Packages/com.furroxide.shockosc-avatar`. Inherited upstream packages remain untouched. Only files explicitly listed in `Tools/package-assets.json` enter the maintained release archives.

## Build locally

Use Python 3.10 or newer; packaging uses only the standard library.

```sh
python Tools/package.py validate
python -m unittest discover -s Tests -v
python Tools/package.py build
```

The output directory is `dist`. A build produces:

- `com.furroxide.shockosc-avatar-0.1.0.zip`: VPM package, including the optional sample under `Samples~`.
- `PiShockBolt-Modular-OSC-0.1.0.unitypackage`: manual installation, excluding the optional optimizer sample.
- `PiShockBolt-Optimizer-0.1.0.unitypackage`: optional preset and its instructions.
- `package.json`, `SHA256SUMS` and `release-manifest.json`: release metadata, archive hashes and the asset inventory.
- `candidate-index.json`: a local listing candidate. It points to the future release URLs and must not be deployed before those downloads are public.

The builder preserves asset GUIDs and converts VRCFury's embedded asset paths for each format. Textures retain their current mipmap import settings. ZIP and tar entries use fixed timestamps, so identical source files produce identical archive hashes.

To test through Creator Companion before publication, add the source package folder through **Settings → User Packages** and install it into a disposable avatar project. Test the manual archive in a different disposable project. Do not add candidate assets to the production avatar project for packaging validation.

For repeatable engine validation, `Tools/prepare_unity_validation.py` copies the locally installed SDK and cached Unity dependencies into a new disposable project. Use a short output path on Windows. Pass `--vrcfury-archive` with an official compatible VRCFury VCC ZIP if the source project's installed version is older than 1.1457.0.

Run the Unity editor in batch mode with `ShockOscPackageValidation.Install`, wait for it to exit, then run `ShockOscPackageValidation.Run`. The runner waits for VRCFury initialization and processes unpacked build copies. VPM mode uses VPM Core's local-folder provider; manual mode uses Unity's native archive importer. `--optimizer` enables the optional sample/merge checks. For a manual optimizer test, run `ShockOscPackageValidation.ImportOptimizer` to completion before `Run`.

Migration mode needs an extra preparation step: run `ShockOscPackageValidation.PrepareMigration`, let Unity exit, apply the migration helper to that disposable project, then run `Install` and `Run`. The fixture includes a fitted scene, a local placement preset, a moved legacy file, an unrelated file occupying the old path, and an FBX whose metadata references a target material GUID.

## Release checks

`release-status.json` records the status and evidence for licensing, clean installation, migration, runtime behaviour, live OSC and independent review. Normally, publication requires every check to be verified with evidence for the current version. The owner authorized `0.1.0` for public installation testing with redistribution terms unconfirmed and live OSC verification pending. Its version-specific `testRelease` record accepts those two limitations without marking them verified. Installation, migration, runtime and independent review still require evidence. That authorization does not carry forward to another version.

Verify fresh VCC and fallback installation without the original OpenShock asset. Check the single and four-instance prefabs, removal of one badge, no missing scripts or animation bindings, contact enablement, bone links and green/red indicators. Repeat the PC optimizer merge with 976 triangles for four badges and 732 for three.

For migration, start from a copy of the existing custom installation containing fitted instances and the local placement preset. With Unity closed, run `Tools/migrate_legacy.py --project PATH --apply`, then install through VPM. Confirm GUID resolution, preserved overrides and recoverable backups. Also test a moved legacy file, an unrelated file with a different GUID, and a different asset whose metadata merely references a target GUID.

For the live check, capture the contact group parameters emitted by VRChat and the returned `_Any_Active`/`_Any_Cooldown` status from the configured bridge. Record the versions and actual result. A synthetic OSC packet test does not replace this check. Physical device output is not required for package validation.

Inspect all final archives, image metadata, dependency references and public documentation. Run the project owner's required generated-media scan over the project and staged release. Check any match against the actual metadata; a matching substring alone does not establish provenance. Use two independent reviewers for craft/communication and correctness/delivery, bound to the final source revision and artifact hashes. Keep private review records outside the distributed package.

## Automation

Pull requests and pushes to `main` run the package tests and upload candidate archives. **Build Release** is a manual workflow: it creates a draft by default. Its `publish` option checks release evidence and any explicit, version-specific test authorization before publishing. It does not replace the independent release review.

The Pages workflow builds its listing from public VPM releases with ordinary `X.Y.Z` versions, including the initial development version `0.1.0`. GitHub prereleases, inherited upstream releases and drafts are skipped. It verifies ZIP hashes and preserves older published versions. GitHub Pages must use the **GitHub Actions** source; the workflow must run from `main`.

Before releasing another version, update the package version, download URL and changelog, then reset all release evidence for that version. Do not overwrite an already-published release or remove its downloads.
