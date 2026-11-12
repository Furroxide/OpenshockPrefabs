# ShockOSC Bolt Badges

A modular lightning badge for VRChat avatars, with 244 triangles, one material and two shared 512 × 512 maps. The badge includes rear strap loops; the prefabs add touch receivers and active/cooldown indicators.

## Requirements and installation

Use Unity 2022.3.22f1, VRChat Avatars SDK `>=3.10.6 <3.11.0` and VRCFury `>=1.1457.0 <2.0.0`. Creator Companion installs the required packages when the VRChat and [VRCFury](https://vcc.vrcfury.com/) repositories are available. Manual `.unitypackage` users must install them first.

The package targets PC. A legacy mobile material is retained, but no Android build or standalone Quest connection is claimed.

## Fit the badges

1. Drag `OSC/PiShockBolt_OSC.prefab` or `OSC/PiShockBolt_OSC_Four.prefab` under your avatar root. The four-badge prefab contains independent nested instances. To remove a mount, unpack the outer group first, retaining the nested badge instances.
2. Fit each instance using its root transform. Its **VRCFury Armature Link** selects the humanoid attachment bone. Leave alignment disabled to retain your placement.
3. On the `Shocker` child, change the contact receiver's parameter to `ShockOsc/YourGroupName`, matching your ShockOSC group exactly. Adjust its radius and height to fit. Keep the child name `Shocker`; the animation bindings use it.
4. Upload through the VRChat SDK. VRCFury installs the menus, controller and bone attachments in the build copy.

Fit prefab instances in your scene or create your own prefab variant under `Assets`. Installed VPM package files are replaced on update, so do not use them to store your custom placement.

The contact component is disabled in the authored prefab and enabled by the animations. Leave that checkbox disabled. The badge stays visible for fitting in Edit Mode. The menu is `ShockOsc/Shocker`. Its `Shocker/Toggle` defaults off on first use, but its state is saved and can restore enabled on later avatar loads.

## Channels and indicators

| Instance | Attachment | Contact parameter |
| --- | --- | --- |
| LowerLeft | LeftUpperLeg | `ShockOsc/Lower-Left` |
| UpperLeft | LeftUpperArm | `ShockOsc/Upper-Left` |
| UpperRight | RightUpperArm | `ShockOsc/Upper-Right` |
| LowerRight | RightUpperLeg | `ShockOsc/Lower-Right` |

All badges display the aggregate `ShockOsc/_Any_Active` and `ShockOsc/_Any_Cooldown` status: a periodic green idle blink, green active pulse and red cooldown. Touch channels remain independent. Receivers use finger tags, other-user contact, local-only filtering, self-contact off and minimum velocity 0.05.

The inherited controller's states and timing are retained. Emission strength is baked into the colour curves for compatibility with the badge shader. The indicator area of the atlas is neutral so the green and red colours remain readable.

Enable OSC in VRChat and configure matching groups in ShockOSC/OpenShock Desktop. This package supplies the avatar assets; the bridge, device setup and credentials remain separate. Follow the [official avatar setup guide](https://wiki.openshock.org/guides/shockosc/avatar-setup-vrc).

## Optional PC mesh merge

Install d4rkAvatarOptimizer separately. Version 4.6.0 is the current validation target.

For VCC installation, select this package in **Window → Package Manager**, expand **Samples** and import **d4rkAvatarOptimizer preset**. For manual installation, import `PiShockBolt-Optimizer-1.0.0.unitypackage` after installing the optimizer; its preset appears at `Assets/Furroxide/PiShockBolt/OSC/PiShockBolt_Optimizer.preset`.

Add one optimizer component to the avatar root and apply the supplied preset through the component's preset selector. Keep **Apply on Upload** enabled. If an optimizer is already configured, retain its settings and enable **Merge Skinned Meshes**, **Merge Static Meshes as Skinned**, **Use Shader Toggles** and **Merge Different Property Materials**.

The optimizer belongs on the avatar once. The preset disables unrelated FX, PhysBone, blendshape and component-cleanup optimizations. Mesh and material merging still affects the avatar, so use exclusions for parts that should remain separate. Do not make this preset a project-wide default.

| Configuration | Triangles | Renderers | Material slots |
| --- | ---: | ---: | ---: |
| Single badge | 244 | 1 | 1 |
| Four editable instances | 976 | 4 | 4 |
| Four after verified PC merge | 976 | 1 | 1 |
| Three after removal and verified merge | 732 | 1 | 1 |

The PC material uses the included lightweight shader. The optional advanced merge was verified with that shader. The retained mobile material uses VRChat/Mobile/Standard Lite and is intended for separate renderers.

## Verification and licensing status

The release candidate was checked in disposable humanoid projects with Unity 2022.3.22f1, SDK 3.10.6, VRCFury 1.1457.0 and optimizer 4.6.0. Fresh VPM installation, native Unity archive import and backed-up migration passed. Single, four-badge and three-badge build copies retained their channels, limb attachments, contact enablement and green/red indicators through Disabled → Idle → Active → Cooldown → Disabled → Idle. Shader and animation-binding checks passed, and the optional PC merge produced the counts above.

The release-copy FBX matches the replacement badge source in vertices, normals, UVs and topology; only texture-resource paths were rebased. Live VRChat/bridge traffic and physical device output were not exercised. Current release status is tracked in the repository's `release-status.json`.

The replacement atlas and mask have mipmaps and streaming enabled, with texture Read/Write disabled. The FBX retains Read/Write and Legacy Blend Shape Normals for SDK import validation.

The integration derives from [Kyobinoyo's OpenShock Shocker prefab](https://github.com/Kyobinoyo/OpenshockPrefabs/releases/tag/Shockerv1.2). Its original model and textures are excluded. See [third-party notices](THIRD_PARTY_NOTICES.md) and [licensing status](LICENSE.md). No confirmed upstream redistribution licence has been recorded yet.
