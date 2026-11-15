using System;
using System.Collections;
using System.Linq;
using System.Reflection;
using com.vrcfury.api;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.SDKBase.Editor.BuildPipeline;

namespace Furroxide.ShockOsc.Editor
{
    public sealed class ShockOscMenuBuildHook : IVRCSDKPreprocessAvatarCallback
    {
        // Install before VRCFury (-10000), which rewrites badge-relative animation paths.
        public int callbackOrder => -11000;

        public bool OnPreprocessAvatar(GameObject avatarGameObject)
        {
            var installers = avatarGameObject.GetComponentsInChildren<ShockOscMenuInstaller>(true);
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

            var groups = eligible.Select(ConfigureBadge).ToArray();
            if (groups.Distinct(StringComparer.Ordinal).Count() != groups.Length)
                throw new InvalidOperationException("ShockOSC group names must be unique for each badge on an avatar.");
            var channels = groups.SelectMany(g => new[] { "", "_Active", "_Cooldown", "_CooldownPercentage", "_Intensity" }
                .Select(suffix => "ShockOsc/" + g + suffix)).ToArray();
            if (channels.Distinct(StringComparer.Ordinal).Count() != channels.Length)
                throw new InvalidOperationException("ShockOSC group names produce overlapping input or feedback parameters.");

            // Only the build copy is processed. Removing markers makes repeat calls harmless.
            foreach (var installer in installers) UnityEngine.Object.DestroyImmediate(installer);
            return true;
        }

        static string ConfigureBadge(ShockOscMenuInstaller installer)
        {
            var receiver = installer.GetComponentInChildren<VRCContactReceiver>(true);
            if (receiver == null) throw new InvalidOperationException("A ShockOSC badge has no contact receiver.");
            var group = (installer.groupName ?? "").Trim();
            if (group.Length == 0)
            {
                const string prefix = "ShockOsc/";
                if (!receiver.parameter.StartsWith(prefix, StringComparison.Ordinal))
                    throw new InvalidOperationException("Set the ShockOSC group on the badge installer.");
                group = receiver.parameter.Substring(prefix.Length);
            }
            if (group.Length == 0 || group.StartsWith("_", StringComparison.Ordinal) || group.Contains("/"))
                throw new InvalidOperationException("Invalid ShockOSC group name: " + group);
            receiver.parameter = "ShockOsc/" + group;

            // Older fitted prefab instances still carry the aggregate status controller.
            // Remove it from this build copy before adding the per-group controller.
            var legacy = installer.legacyBadgeControllerFeature != null
                ? installer.legacyBadgeControllerFeature : FindLegacyController(installer);
            if (legacy != null) UnityEngine.Object.DestroyImmediate(legacy);

            var feedback = FuryComponents.CreateFullController(installer.gameObject);
            feedback.AddController(CreateController(group));
            feedback.AddParams(CreateParameters(group, installer.syncIntensity));
            feedback.AddGlobalParam("Shocker/Toggle");
            feedback.AddGlobalParam("ShockOsc/" + group);
            feedback.AddGlobalParam("ShockOsc/" + group + "_Active");
            feedback.AddGlobalParam("ShockOsc/" + group + "_Cooldown");
            feedback.AddGlobalParam("ShockOsc/" + group + "_CooldownPercentage");
            feedback.AddGlobalParam("ShockOsc/" + group + "_Intensity");
            return group;
        }

        static MonoBehaviour FindLegacyController(ShockOscMenuInstaller installer)
        {
            const BindingFlags fields = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            foreach (var candidate in installer.GetComponents<MonoBehaviour>())
            {
                if (candidate == null || candidate.GetType().FullName != "VF.Model.VRCFury") continue;
                var content = candidate.GetType().GetField("content", fields)?.GetValue(candidate);
                if (content == null || content.GetType().FullName != "VF.Model.Feature.FullController") continue;
                var globals = content.GetType().GetField("globalParams", fields)?.GetValue(content) as IEnumerable;
                if (globals == null) continue;
                var names = globals.Cast<object>().OfType<string>().ToArray();
                if (names.Contains("ShockOsc/_Any_Active") && names.Contains("ShockOsc/_Any_Cooldown"))
                    return candidate;
            }
            return null;
        }

        static VRCExpressionParameters CreateParameters(string group, bool syncIntensity)
        {
            var root = "ShockOsc/" + group;
            var asset = ScriptableObject.CreateInstance<VRCExpressionParameters>();
            asset.name = "ShockOSC " + group + " feedback parameters";
            asset.parameters = new[] {
                Parameter(root, VRCExpressionParameters.ValueType.Bool, false),
                Parameter(root + "_Active", VRCExpressionParameters.ValueType.Bool, true),
                Parameter(root + "_Cooldown", VRCExpressionParameters.ValueType.Bool, true),
                Parameter(root + "_CooldownPercentage", VRCExpressionParameters.ValueType.Float, true),
                Parameter(root + "_Intensity", VRCExpressionParameters.ValueType.Float, syncIntensity)
            };
            return asset;
        }

        static VRCExpressionParameters.Parameter Parameter(string name, VRCExpressionParameters.ValueType type, bool synced)
        {
            return new VRCExpressionParameters.Parameter {
                name = name, valueType = type, saved = false, defaultValue = 0, networkSynced = synced
            };
        }

        static AnimatorController CreateController(string group)
        {
            var root = "ShockOsc/" + group;
            var controller = new AnimatorController { name = "ShockOSC " + group + " feedback" };
            controller.AddParameter("Shocker/Toggle", AnimatorControllerParameterType.Bool);
            controller.AddParameter(root, AnimatorControllerParameterType.Bool);
            controller.AddParameter(root + "_Active", AnimatorControllerParameterType.Bool);
            controller.AddParameter(root + "_Cooldown", AnimatorControllerParameterType.Bool);
            controller.AddParameter(root + "_CooldownPercentage", AnimatorControllerParameterType.Float);
            controller.AddParameter(root + "_Intensity", AnimatorControllerParameterType.Float);

            var status = AddLayer(controller, "ShockOSC " + group + " status");
            var disabled = status.AddState("Disabled");
            var idle = status.AddState("Idle");
            var active = status.AddState("Active");
            var cooldown = status.AddState("Cooldown");
            status.defaultState = disabled;
            disabled.motion = StatusClip("Disabled", false, 0, Color.black, 0, 0);
            idle.motion = StatusClip("Idle", true, 0, new Color(0, .28f, 0), 0, 0);
            active.motion = StatusClip("Active", true, 1, new Color(0, 2f, 0), 1, 0);
            cooldown.motion = StatusClip("Cooldown", true, 2, new Color(1.2f, .6f, 0), 0, 1);
            // The status changes smoothly but quickly enough to show a 300 ms trigger.
            Any(status, disabled, .12f, new Condition("Shocker/Toggle", AnimatorConditionMode.IfNot), new Condition(root + "_Active", AnimatorConditionMode.IfNot), new Condition(root + "_Cooldown", AnimatorConditionMode.IfNot));
            Any(status, active, .12f, new Condition(root + "_Active", AnimatorConditionMode.If));
            Any(status, cooldown, .12f, new Condition(root + "_Active", AnimatorConditionMode.IfNot), new Condition(root + "_Cooldown", AnimatorConditionMode.If));
            Any(status, idle, .12f, new Condition("Shocker/Toggle", AnimatorConditionMode.If), new Condition(root + "_Active", AnimatorConditionMode.IfNot), new Condition(root + "_Cooldown", AnimatorConditionMode.IfNot));

            var receiver = AddLayer(controller, "ShockOSC " + group + " receiver");
            var receiverOff = receiver.AddState("Receiver off");
            var receiverOn = receiver.AddState("Receiver on");
            receiver.defaultState = receiverOff;
            receiverOff.motion = ReceiverClip(false);
            receiverOn.motion = ReceiverClip(true);
            Any(receiver, receiverOff, new Condition("Shocker/Toggle", AnimatorConditionMode.IfNot));
            Any(receiver, receiverOn, new Condition("Shocker/Toggle", AnimatorConditionMode.If));

            var touch = AddLayer(controller, "ShockOSC " + group + " contact attempt");
            var off = touch.AddState("Not touched");
            var on = touch.AddState("Touched");
            touch.defaultState = off;
            off.motion = FloatClip("Contact off", "material._TouchStrength", 0);
            on.motion = FloatClip("Contact on", "material._TouchStrength", 1);
            Any(touch, on, .1f, new Condition("Shocker/Toggle", AnimatorConditionMode.If), new Condition(root, AnimatorConditionMode.If));
            Any(touch, off, .1f, new Condition("Shocker/Toggle", AnimatorConditionMode.IfNot));
            Any(touch, off, .1f, new Condition(root, AnimatorConditionMode.IfNot));

            var progress = AddLayer(controller, "ShockOSC " + group + " cooldown progress");
            var progressState = progress.AddState("Progress");
            progress.defaultState = progressState;
            var blend = new BlendTree { name = "Cooldown percentage", blendType = BlendTreeType.Simple1D,
                blendParameter = root + "_CooldownPercentage", useAutomaticThresholds = false };
            blend.AddChild(FloatClip("Cooldown empty", "material._CooldownProgress", 0), 0);
            blend.AddChild(FloatClip("Cooldown full", "material._CooldownProgress", 1), 1);
            progressState.motion = blend;

            var intensity = AddLayer(controller, "ShockOSC " + group + " intensity");
            var intensityState = intensity.AddState("Intensity");
            intensity.defaultState = intensityState;
            var intensityBlend = new BlendTree { name = "Reported intensity", blendType = BlendTreeType.Simple1D,
                blendParameter = root + "_Intensity", useAutomaticThresholds = false };
            intensityBlend.AddChild(FloatClip("Intensity empty", "material._IntensityLevel", 0), 0);
            intensityBlend.AddChild(FloatClip("Intensity full", "material._IntensityLevel", 1), 1);
            intensityState.motion = intensityBlend;
            return controller;
        }

        static AnimatorStateMachine AddLayer(AnimatorController controller, string name)
        {
            var machine = new AnimatorStateMachine { name = name };
            controller.AddLayer(new AnimatorControllerLayer { name = name, defaultWeight = 1, stateMachine = machine });
            return machine;
        }

        struct Condition
        {
            public readonly string parameter;
            public readonly AnimatorConditionMode mode;
            public Condition(string parameter, AnimatorConditionMode mode) { this.parameter = parameter; this.mode = mode; }
        }

        static void Any(AnimatorStateMachine machine, AnimatorState state, params Condition[] conditions)
        {
            Any(machine, state, 0, conditions);
        }

        static void Any(AnimatorStateMachine machine, AnimatorState state, float seconds, params Condition[] conditions)
        {
            var transition = machine.AddAnyStateTransition(state);
            transition.hasExitTime = false;
            transition.hasFixedDuration = true;
            transition.duration = seconds;
            transition.canTransitionToSelf = false;
            foreach (var condition in conditions) transition.AddCondition(condition.mode, 0, condition.parameter);
        }

        static AnimationClip StatusClip(string name, bool visualEnabled, float mode, Color emission, float active, float cooling)
        {
            var clip = new AnimationClip { name = name, frameRate = 60 };
            Curve(clip, typeof(MeshRenderer), "material._FeedbackEnabled", new[] { 0f }, new[] { visualEnabled ? 1f : 0f });
            Curve(clip, typeof(MeshRenderer), "material._FeedbackMode", new[] { 0f }, new[] { mode });
            Curve(clip, typeof(MeshRenderer), "material._ActiveStrength", new[] { 0f }, new[] { active });
            Curve(clip, typeof(MeshRenderer), "material._CooldownStrength", new[] { 0f }, new[] { cooling });
            Curve(clip, typeof(MeshRenderer), "material._EmissionColor.r", new[] { 0f }, new[] { emission.r });
            Curve(clip, typeof(MeshRenderer), "material._EmissionColor.g", new[] { 0f }, new[] { emission.g });
            Curve(clip, typeof(MeshRenderer), "material._EmissionColor.b", new[] { 0f }, new[] { emission.b });
            return clip;
        }

        static AnimationClip ReceiverClip(bool enabled)
        {
            var clip = new AnimationClip { name = enabled ? "Receiver on" : "Receiver off", frameRate = 60 };
            Curve(clip, typeof(VRCContactReceiver), "m_Enabled", new[] { 0f }, new[] { enabled ? 1f : 0f });
            return clip;
        }

        static AnimationClip FloatClip(string name, string property, float value)
        {
            var clip = new AnimationClip { name = name, frameRate = 60 };
            Curve(clip, typeof(MeshRenderer), property, new[] { 0f }, new[] { value });
            return clip;
        }

        static void Curve(AnimationClip clip, Type type, string property, float[] times, float[] values)
        {
            var keys = times.Select((t, i) => new Keyframe(t, values[i])).ToArray();
            AnimationUtility.SetEditorCurve(clip, EditorCurveBinding.FloatCurve("Shocker", type, property), new AnimationCurve(keys));
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
