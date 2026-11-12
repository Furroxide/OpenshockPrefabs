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
        public VRCExpressionsMenu menu;
        public VRCExpressionParameters parameters;
        public RuntimeAnimatorController controller;
    }
}
