# Third-party notices

## OpenShock Shocker prefab

Source: [Kyobinoyo/OpenshockPrefabs, Shocker v1.2](https://github.com/Kyobinoyo/OpenshockPrefabs/releases/tag/Shockerv1.2).

The expression-parameter asset was copied unchanged. The controller retains the upstream state machine with renamed assets and replacement references. The animation clips and VRCFury prefab configuration were adapted for the replacement badge and shader. These are derived integration assets, not an independently reimplemented controller.

The upstream instructions credit **OpenShock** for the prefab and **lolatron** for the original model, linking to [PiShock 3D Model](https://lolatron.gumroad.com/l/PISHOCK). That model, its textures, the Substance Painter file, original menu icon and Poiyomi material are not included in the maintained package.

The repository is a fork and preserves upstream history. The original Unity packages remain in that inherited history and source tree; they are not dependencies of the maintained VPM ZIP or fallback archives.

Upstream redistribution terms remain unverified. See [LICENSE.md](LICENSE.md).

## External dependencies

- [VRChat Avatars SDK](https://creators.vrchat.com/sdk/) supplies the avatar descriptor, expression parameters and contact components.
- [VRCFury](https://github.com/VRCFury/VRCFury) merges controllers, adds menu items and attaches the badges to humanoid bones.
- [d4rkAvatarOptimizer](https://github.com/d4rkc0d3r/d4rkAvatarOptimizer) is optional. The supplied preset contains configuration only; the optimizer is installed separately.
- [OpenShock Desktop / ShockOSC](https://github.com/OpenShock/ShockOSC) is the separately configured OSC bridge.

None of those applications, SDKs or packages is bundled in the maintained release archives. Their respective terms continue to apply to separately installed copies.
