using System;
using System.Linq;
using com.vrcfury.api;
using UnityEngine;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Furroxide.ShockOsc.Editor
{
    public sealed class ShockOscMenuBuildHook : IVRCSDKPreprocessAvatarCallback
    {
        // Install before VRCFury (-10000), so it handles menu merging, parameter names and pagination.
        public int callbackOrder => -11000;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var installers = avatarGameObject.GetComponentsInChildren<ShockOscMenuInstaller>(true);
            // VRCFury also builds inactive props, which may be enabled later by another avatar toggle.
            var eligible = installers.Where(i => i.enabled && !IsEditorOnly(i.transform, avatarGameObject.transform)).ToArray();
            if (eligible.Length == 0) return true;

            var source = eligible[0];
            if (source.menu == null || source.parameters == null || source.controller == null)
                throw new InvalidOperationException("The ShockOSC badge is missing its shared menu, parameters or controller.");
            if (eligible.Any(i => i.menu != source.menu || i.parameters != source.parameters || i.controller != source.controller))
                throw new InvalidOperationException("The avatar contains incompatible ShockOSC menu installers. Use one package version for all badges.");

            var shared = FuryComponents.CreateFullController(avatarGameObject);
            shared.AddMenu(source.menu, "ShockOsc");
            shared.AddParams(source.parameters);
            shared.AddController(source.controller);
            shared.AddGlobalParam("Shocker/Toggle");
            shared.AddGlobalParam("ShockOsc/_Config/_All/*");

            // Only the build copy is processed. Removing the markers also makes repeat calls harmless.
            foreach (var installer in installers) UnityEngine.Object.DestroyImmediate(installer);
            return true;
        }

        static bool IsEditorOnly(Transform current, Transform avatar)
        {
            while (current != null)
            {
                if (current.CompareTag("EditorOnly")) return true;
                if (current == avatar) break;
                current = current.parent;
            }
            return false;
        }
    }
}
