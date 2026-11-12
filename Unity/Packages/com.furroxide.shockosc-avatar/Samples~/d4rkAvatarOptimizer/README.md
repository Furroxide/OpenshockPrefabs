# Optional d4rkAvatarOptimizer preset

Install d4rkAvatarOptimizer before importing this sample. Version 4.6.0 is the current validation target.

Add one optimizer component to the avatar root. Use its preset selector to apply `PiShockBolt_Optimizer.preset` and leave **Apply on Upload** enabled. Existing optimizer users should retain their settings and enable the badge merge options described in the package README.

The preset disables unrelated FX, PhysBone, blendshape and component-cleanup optimizations. Mesh merging still affects the avatar as a whole; review the merge preview and exclusions before uploading. Do not set this preset as the project-wide default.

For a VCC installation, the sample is copied to an editable `Assets/Samples` folder. For the optional manual-install archive, the preset is installed under `Assets/Furroxide/PiShockBolt/OSC`.
