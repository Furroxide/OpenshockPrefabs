# Furroxide ShockOSC Avatars

Modular lightning badges for VRChat avatars: independent touch channels, shared active and cooldown indicators, and an optional PC mesh merge. Each badge uses 244 triangles and the same two 512 × 512 texture maps.

This is the Furroxide-maintained fork of [Kyobinoyo's OpenShock prefabs](https://github.com/Kyobinoyo/OpenshockPrefabs). The Shocker controller, parameter asset, animation clips and VRCFury configuration were adapted for the replacement badge. The original model, textures and Poiyomi material are excluded from the maintained package. The [original prefab documentation](docs/upstream-prefabs.md) and upstream history are preserved.

**Release status:** version 1.0.0 is being prepared. Upstream redistribution terms and the live VRChat/bridge test remain unverified. Development follows the owner's open-source assumption; this is not a confirmed licence grant. See [licensing status](Unity/Packages/com.furroxide.shockosc-avatar/LICENSE.md) and [release checks](docs/development.md#release-checks).

## Install with Creator Companion

Once a verified release is published:

1. Add the [VRCFury repository](https://vcc.vrcfury.com/) and the [ShockOSC avatar repository](https://furroxide.github.io/OpenshockPrefabs/index.json) in Creator Companion → Settings → Packages.
2. Open **Manage Project** for your humanoid avatar project and install **Furroxide ShockOSC Avatars**.
3. In Unity, open **Packages → Furroxide ShockOSC Avatars → OSC**. Drag the single-badge or four-badge prefab under your avatar root.
4. Fit each badge and set its receiver parameter to match a ShockOSC group. Follow the [fitting and channel guide](Unity/Packages/com.furroxide.shockosc-avatar/README.md).

The listing includes published releases only. It stays empty while release checks are pending. For a local candidate, build the ZIP and add the package source folder as a VCC user package; see [development](docs/development.md).

## Compatibility

| Area | Requirement or limit |
| --- | --- |
| Unity | 2022.3.22f1 |
| VRChat Avatars SDK | `>=3.10.6 <3.11.0` |
| VRCFury | `>=1.1457.0 <2.0.0` |
| Optional optimizer | d4rkAvatarOptimizer 4.6.0 is the current validation target. Install it separately before importing the preset sample. |
| Platform | PC avatar assets. Android/Quest support is not claimed. |
| Desktop application | ShockOSC in OpenShock Desktop must be configured and running separately. No desktop app, credentials or device configuration are included. |

VCC resolves the required SDK and VRCFury packages from repositories you have added. It does not install d4rkAvatarOptimizer automatically.

## Other installation and upgrades

- The versioned `PiShockBolt-Modular-OSC-1.0.0.unitypackage` is a fallback for manual installation. Install the SDK and VRCFury first. The optimizer preset is a separate optional download.
- Choose one installation format per project. When upgrading the custom Unity package, run the backed-up GUID migration helper before installing through VCC; it preserves avatar-specific placement presets. Read [upgrading](docs/upgrading.md) before switching formats.
- [Avatar fitting, channels and optimizer setup](Unity/Packages/com.furroxide.shockosc-avatar/README.md)
- [Official ShockOSC avatar setup](https://wiki.openshock.org/guides/shockosc/avatar-setup-vrc)
- [Builds, tests and release process](docs/development.md)

## Source and terms

The package's [third-party notices](Unity/Packages/com.furroxide.shockosc-avatar/THIRD_PARTY_NOTICES.md) identify the reused integration and the separate dependencies. The desktop ShockOSC application's licence is not used as a substitute for the prefab repository's missing terms.
