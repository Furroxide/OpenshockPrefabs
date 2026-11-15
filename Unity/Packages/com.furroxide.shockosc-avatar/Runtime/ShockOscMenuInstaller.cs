using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDKBase;

namespace Furroxide.ShockOsc
{
    // Build-time data only. Each badge carries a reference; the build installs one shared menu.
    [AddComponentMenu("")]
    [DisallowMultipleComponent]
    public sealed class ShockOscMenuInstaller : MonoBehaviour, IEditorOnly
    {
        // Leave empty to use the existing contact receiver parameter. This keeps fitted
        // instances from older package versions bound to their original ShockOSC group.
        public string groupName;
        [Tooltip("Also show the secondary intensity bar to other players. Costs one synced float (8 bits) per badge when enabled.")]
        public bool syncIntensity;
        [HideInInspector]
        public MonoBehaviour legacyBadgeControllerFeature;
        public VRCExpressionsMenu menu;
        public VRCExpressionParameters parameters;
        public RuntimeAnimatorController controller;
    }
}
