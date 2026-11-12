# Upgrade an existing installation

Back up your avatar project before changing installation formats. Choose one format per project; importing the fallback over an installed VPM package creates duplicate asset GUIDs.

## From the custom Bolt Badge Unity package to VCC

The migration helper identifies the 17 known legacy files by the GUID in each file's own `.meta` header. It moves matching files into a dated `.ShockOscMigrationBackup` folder outside `Assets`. It does not delete the old badge folder or change the fitted scene.

The local `OSC/Presets/PiShockBolt_OSC_Four_KhnFuCat.prefab` placement preset is excluded from migration and from distribution. It continues to reference the generic prefabs by their preserved GUIDs. Scene placement, overrides and channel names should therefore resolve to the package assets after migration.

1. Close Unity. Download or clone this repository and use Python 3.10 or newer to run `python Tools/migrate_legacy.py --project "PATH_TO_YOUR_AVATAR_PROJECT"`. Review the proposed file moves; this first command changes nothing.
2. Run the same command with `--apply` to move those files and their metadata into the backup. The helper stops on duplicate matching GUIDs and keeps unmatched files, including the local placement preset.
3. Add the VRCFury and ShockOSC avatar repositories to VCC, then install the maintained package before reopening Unity.
4. Check the existing fitted instances. Confirm there is one loaded asset for each badge GUID, no missing prefab or script, and no unexpected transform or receiver changes.
5. If you used the optimizer preset, its already-applied component settings remain on your avatar. Import the optional sample only if you need an editable copy of the preset again.

Renamed or moved legacy files can be found by their GUID. Unrelated files with a different GUID are not migration targets. Keep your own variants under `Assets` and verify them after every package update.

Native VPM `legacyFiles` deletion is intentionally disabled. It matches paths first and its GUID fallback searches references inside metadata as well as asset identity, so it can select an unrelated asset. The helper parses only the actual GUID header and makes a recoverable backup instead.

To roll back before changing anything else, remove the VCC package, then restore the backup's `Assets` files to their original project-relative paths. Keep the backup until the upgraded avatar has been checked. Do not copy backup assets into `Assets` while the VCC package is installed.

## From the original OpenShock Shocker prefab

The original prefab assets are preserved separately. Installing this package does not replace or remove them automatically. Remove the old fitted Shocker instances from the avatar when replacing them with the new badges, then fit the new instances and retain your existing ShockOSC group names.

Both integrations use the inherited global toggle and aggregate status names. Leaving both sets of receivers on an avatar is not a migration strategy.

## Return to manual installation

Remove the VCC package before importing the fallback `.unitypackage`. Keep a project backup and your own placement presets. The fallback uses the same asset GUIDs and restores the original custom asset paths. Install the SDK and VRCFury separately, then verify the fitted instances and parameters before uploading.
