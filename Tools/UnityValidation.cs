// Copied only into disposable validation projects, never into the distributed package.
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.Presets;
using UnityEditor.SceneManagement;
using UnityEngine;
using VRC.SDK3.Avatars.Components;
using VRC.SDK3.Avatars.ScriptableObjects;
using VRC.SDK3.Dynamics.Contact.Components;
using VRC.PackageManagement.Core.Types;
using VRC.PackageManagement.Core.Types.Providers;

public static class ShockOscPackageValidation
{
    const string PackageName = "com.furroxide.shockosc-avatar";
    const string LegacyRoot = "Assets/Furroxide/PiShockBolt";
    static string Root => Directory.GetParent(Application.dataPath).FullName;
    static JObject Config => JObject.Parse(File.ReadAllText(Path.Combine(Root, "validation-config.json")));
    static string Version => (string)JObject.Parse(File.ReadAllText(Path.Combine(Root, "PackageSource/package.json")))["version"];
    static void Require(bool condition, string message) { if (!condition) throw new Exception(message); }
    static string Hash(string path) { using (var sha = SHA256.Create()) return BitConverter.ToString(sha.ComputeHash(File.ReadAllBytes(path))).Replace("-", "").ToLowerInvariant(); }
    static void Write(string name, object value) => File.WriteAllText(Path.Combine(Root, name), JsonConvert.SerializeObject(value, Formatting.Indented));
    static Type FindType(string name) => AppDomain.CurrentDomain.GetAssemblies().Select(a => a.GetType(name)).FirstOrDefault(t => t != null);
    static string PendingImport => Path.Combine(Root, "validation-pending-import.json");

    [InitializeOnLoadMethod]
    static void ResumeNativeImport()
    {
        if (!File.Exists(PendingImport)) return;
        EditorApplication.update -= CheckNativeImport;
        EditorApplication.update += CheckNativeImport;
    }

    static void CheckNativeImport()
    {
        if (!File.Exists(PendingImport)) { EditorApplication.update -= CheckNativeImport; return; }
        var pending = JObject.Parse(File.ReadAllText(PendingImport));
        var phase = (string)pending["phase"];
        if (DateTime.UtcNow - pending["startedUtc"].ToObject<DateTime>() > TimeSpan.FromMinutes(3))
        {
            Write("installation-error.json", new { error = "Native import did not resolve within three minutes", phase });
            File.Delete(PendingImport); EditorApplication.Exit(1); return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating) return;
        bool ready = phase == "optimizer"
            ? AssetDatabase.LoadAssetAtPath<Preset>(LegacyRoot + "/OSC/PiShockBolt_Optimizer.preset") != null
            : AssetDatabase.LoadAssetAtPath<GameObject>(LegacyRoot + "/OSC/PiShockBolt_OSC.prefab") != null
              && AssetDatabase.LoadAssetAtPath<GameObject>(LegacyRoot + "/OSC/PiShockBolt_OSC_Four.prefab") != null
              && AssetDatabase.LoadAssetAtPath<Shader>(LegacyRoot + "/OSC/PiShockBolt.shader") != null;
        if (!ready) return;
        File.Delete(PendingImport);
        EditorApplication.update -= CheckNativeImport;
        Write(phase == "optimizer" ? "optimizer-installation.json" : "installation.json", new {
            mode = "unitypackage", installer = "Unity AssetDatabase.ImportPackage; imported inventory resolved after refresh/domain reload", phase, passed = true
        });
        EditorApplication.Exit(0);
    }

    static void BeginNativeImport(string file, string phase)
    {
        Write("validation-pending-import.json", new { phase, startedUtc = DateTime.UtcNow });
        ResumeNativeImport();
        AssetDatabase.importPackageFailed += (package, error) => {
            Write("installation-error.json", new { error }); File.Delete(PendingImport); EditorApplication.Exit(1);
        };
        AssetDatabase.ImportPackage(Path.Combine(Root, file), false);
    }

    public static void ImportOptimizer() => BeginNativeImport("optimizer.unitypackage", "optimizer");

    public static void Install()
    {
        try
        {
            foreach (string file in new[] { "installation.json", "installation-error.json" })
                if (File.Exists(Path.Combine(Root, file))) File.Delete(Path.Combine(Root, file));
            string mode = (string)Config["mode"];
            if (mode == "unitypackage")
            {
                BeginNativeImport("candidate.unitypackage", "base");
                return;
            }
            string placement = Path.Combine(Root, LegacyRoot + "/OSC/Presets/PiShockBolt_OSC_Four_KhnFuCat.prefab");
            string presetHash = null, sceneHash = null;
            if (mode == "migration")
            {
                var fixture = JObject.Parse(File.ReadAllText(Path.Combine(Root, "migration-fixture.json")));
                presetHash = (string)fixture["presetHash"]; sceneHash = (string)fixture["sceneHash"];
            }
            // Use the same local-folder provider and VPM installer as Creator Companion.
            var available = new[] { Path.Combine(Root, "PackageSource") }.Concat(Directory.GetDirectories(Path.Combine(Root, "Packages")));
            var provider = new LocalFolderPackageProvider(available);
            var candidate = provider.GetPackage(PackageName);
            Require(candidate != null, "VPM did not recognize the candidate manifest");
            var project = new UnityProject(Root);
            Require(project.AddVPMPackage(candidate, new[] { provider }, Version), "VPM installation rejected the package or dependencies");
            Require(File.Exists(Path.Combine(Root, "Packages/" + PackageName + "/package.json")), "VPM package source was not installed");
            if (mode == "migration")
            {
                Require(Hash(placement) == presetHash, "Migration modified the local placement preset");
                Require(Hash(Path.Combine(Root, "Assets/FittedBadgeFixture.unity")) == sceneHash, "Migration modified the fitted scene");
                Require(!File.Exists(Path.Combine(Root, "Assets/MovedBadgeMask.png")), "GUID lookup did not remove the moved legacy mask");
                Require(File.Exists(Path.Combine(Root, LegacyRoot + "/PiShockBolt_EmissionMask.png")), "Migration removed the unrelated file occupying the original path");
            }
            Write("installation.json", new { mode, installer = "VPM Core LocalFolderPackageProvider / UnityProject.AddVPMPackage", coreVersion = typeof(UnityProject).Assembly.GetName().Version.ToString(), passed = true, presetHash, sceneHash });
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            Write("installation-error.json", new { error = error.ToString() });
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    public static void PrepareMigration()
    {
        try
        {
            string placement = LegacyRoot + "/OSC/Presets/PiShockBolt_OSC_Four_KhnFuCat.prefab";
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);
            var fitted = (GameObject)PrefabUtility.InstantiatePrefab(AssetDatabase.LoadAssetAtPath<GameObject>(placement), scene);
            fitted.name = "FittedBadgeFixture";
            fitted.transform.localPosition = new Vector3(.123f, .456f, .789f);
            fitted.GetComponentsInChildren<VRCContactReceiver>(true)[0].parameter = "ShockOsc/Custom-Test";
            PrefabUtility.RecordPrefabInstancePropertyModifications(fitted.transform);
            PrefabUtility.RecordPrefabInstancePropertyModifications(fitted.GetComponentsInChildren<VRCContactReceiver>(true)[0]);
            Require(EditorSceneManager.SaveScene(scene, "Assets/FittedBadgeFixture.unity"), "Could not save migration fixture");
            Write("migration-fixture.json", new { presetHash = Hash(Path.Combine(Root, placement)), sceneHash = Hash(Path.Combine(Root, "Assets/FittedBadgeFixture.unity")) });
            EditorApplication.Exit(0);
        }
        catch (Exception error) { Write("migration-fixture-error.json", new { error = error.ToString() }); Debug.LogException(error); EditorApplication.Exit(1); }
    }

    public static void Run()
    {
        Write("validation-pending-runtime.json", new { startedUtc = DateTime.UtcNow });
        ResumeRuntime();
    }

    [InitializeOnLoadMethod]
    static void ResumeRuntime()
    {
        if (!File.Exists(Path.Combine(Root, "validation-pending-runtime.json"))) return;
        EditorApplication.update -= RunWhenReady;
        EditorApplication.update += RunWhenReady;
    }

    static void RunWhenReady()
    {
        string pendingPath = Path.Combine(Root, "validation-pending-runtime.json");
        var pending = JObject.Parse(File.ReadAllText(pendingPath));
        if (DateTime.UtcNow - pending["startedUtc"].ToObject<DateTime>() > TimeSpan.FromMinutes(3))
        {
            Write("validation-results.json", new { passed = false, error = "VRCFury temporary package was not ready within three minutes" });
            File.Delete(pendingPath); EditorApplication.Exit(1); return;
        }
        if (EditorApplication.isCompiling || EditorApplication.isUpdating || !AssetDatabase.IsValidFolder("Packages/com.vrcfury.temp")) return;
        EditorApplication.update -= RunWhenReady;
        File.Delete(pendingPath);
        RunActual();
    }

    static void RunActual()
    {
        var report = new Dictionary<string, object>();
        try
        {
            string mode = (string)Config["mode"];
            bool optimize = (bool)Config["optimizer"];
            string folder = mode == "unitypackage" ? LegacyRoot : "Packages/" + PackageName;
            report["unityVersion"] = Application.unityVersion;
            report["dependencies"] = Config["dependencyVersions"];
            report["mode"] = mode;
            var single = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/OSC/PiShockBolt_OSC.prefab");
            var four = AssetDatabase.LoadAssetAtPath<GameObject>(folder + "/OSC/PiShockBolt_OSC_Four.prefab");
            Require(single != null && four != null, "Prefab references did not resolve after installation");
            Require(!Directory.Exists(Path.Combine(Root, "Assets/Openshock")), "Validation must not include the original OpenShock asset");
            var contact = single.GetComponentInChildren<VRCContactReceiver>(true);
            Require(contact != null && !contact.enabled && !contact.allowSelf && contact.allowOthers && contact.localOnly, "Authored receiver defaults changed");
            var mesh = single.GetComponentInChildren<MeshFilter>(true).sharedMesh;
            Require(mesh.triangles.Length / 3 == 244, "Single badge triangle count changed");
            var originalMesh = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/ValidationOriginal/PiShockBolt.fbx").GetComponentInChildren<MeshFilter>(true).sharedMesh;
            Require(mesh.vertices.SequenceEqual(originalMesh.vertices) && mesh.normals.SequenceEqual(originalMesh.normals)
                && mesh.uv.SequenceEqual(originalMesh.uv) && mesh.triangles.SequenceEqual(originalMesh.triangles), "FBX texture-path rebasing changed mesh data");
            Require(four.GetComponentsInChildren<VRCContactReceiver>(true).Length == 4, "Four-badge prefab is incomplete");
            foreach (var c in four.GetComponentsInChildren<Component>(true)) Require(c != null, "Missing script in four-badge prefab");
            foreach (var textureName in new[] { "PiShockBolt_OSC_BaseColor.png", "PiShockBolt_EmissionMask.png" })
            {
                var importer = (TextureImporter)AssetImporter.GetAtPath(folder + "/" + textureName);
                Require(importer.mipmapEnabled && importer.mipmapFilter == TextureImporterMipFilter.KaiserFilter && !importer.isReadable, "Texture import settings changed: " + textureName);
            }
            report["authoredPrefabs"] = new { singleTriangles = 244, fourContacts = 4, missingScripts = 0, textureImportSettings = "verified", verticesNormalsUvTopologyUnchanged = true };
            if (mode == "migration")
            {
                var previous = JObject.Parse(File.ReadAllText(Path.Combine(Root, "installation.json")));
                string placement = Path.Combine(Root, LegacyRoot + "/OSC/Presets/PiShockBolt_OSC_Four_KhnFuCat.prefab");
                Require(Hash(placement) == (string)previous["presetHash"], "Placement preset changed after import");
                Require(Hash(Path.Combine(Root, "Assets/FittedBadgeFixture.unity")) == (string)previous["sceneHash"], "Fitted scene changed after import");
                var fittedScene = EditorSceneManager.OpenScene("Assets/FittedBadgeFixture.unity", OpenSceneMode.Single);
                var fitted = fittedScene.GetRootGameObjects().Single(g => g.name == "FittedBadgeFixture");
                Require(fitted.transform.localPosition == new Vector3(.123f, .456f, .789f), "Fitted root transform changed");
                Require(fitted.GetComponentsInChildren<VRCContactReceiver>(true).Any(c => c.parameter == "ShockOsc/Custom-Test"), "Channel override was lost");
                foreach (var c in fitted.GetComponentsInChildren<Component>(true)) Require(c != null, "Missing component in migrated fitted instance");
                Require(fitted.GetComponentsInChildren<MeshFilter>(true).All(m => m.sharedMesh != null), "Migrated mesh reference is missing");
                report["migration"] = new { presetPreserved = true, scenePreserved = true, transformPreserved = true, channelOverridePreserved = true, movedFileRemovedByGuid = true, unrelatedFilePreserved = true };
            }
            string optimizerPreset = null;
            if (optimize)
            {
                if (mode == "unitypackage")
                {
                    optimizerPreset = LegacyRoot + "/OSC/PiShockBolt_Optimizer.preset";
                    Require(File.Exists(Path.Combine(Root, "optimizer-installation.json")), "Run ImportOptimizer to completion before the manual optimizer runtime test");
                }
                else
                {
                    var sampleType = FindType("UnityEditor.PackageManager.UI.Sample");
                    Require(sampleType != null, "Unity Package Manager sample API is unavailable");
                    var samples = (IEnumerable)sampleType.GetMethod("FindByPackage", BindingFlags.Public | BindingFlags.Static).Invoke(null, new object[] { PackageName, Version });
                    object sample = samples.Cast<object>().Single();
                    var import = sampleType.GetMethod("Import", BindingFlags.Public | BindingFlags.Instance);
                    var options = Enum.Parse(import.GetParameters()[0].ParameterType, "OverridePreviousImports");
                    Require((bool)import.Invoke(sample, new[] { options }), "Package Manager sample import failed");
                    AssetDatabase.Refresh(ImportAssetOptions.ForceSynchronousImport);
                    optimizerPreset = AssetDatabase.GUIDToAssetPath("416e103d053e99c47b4b2d57d979388f");
                }
                Require(AssetDatabase.LoadAssetAtPath<Preset>(optimizerPreset) != null, "Optional optimizer preset did not import");
                report["optimizerSample"] = new { imported = true, path = optimizerPreset };
            }
            var results = new List<object>();
            report["builds"] = results;
            foreach (int count in new[] { 4, 1, 3 }) results.Add(CheckBuild(single, four, count, optimize, optimizerPreset));
            results.Add(CheckBuild(single, four, 4, optimize, optimizerPreset, true));
            results.Add(CheckBuild(single, four, 4, false, null, false, true));
            report["passed"] = true;
            Write("validation-results.json", report);
            EditorApplication.Exit(0);
        }
        catch (Exception error)
        {
            report["passed"] = false;
            report["error"] = error.ToString();
            Write("validation-results.json", report);
            Debug.LogException(error);
            EditorApplication.Exit(1);
        }
    }

    static GameObject MakeHumanoid()
    {
        var root = new GameObject("ShockOSC Validation Rig");
        var map = new Dictionary<HumanBodyBones, Transform>();
        Action<HumanBodyBones, HumanBodyBones?, Vector3> bone = (name, parent, position) => {
            var t = new GameObject(name.ToString()).transform;
            t.SetParent(parent.HasValue ? map[parent.Value] : root.transform, false);
            t.localPosition = position;
            map[name] = t;
        };
        bone(HumanBodyBones.Hips, null, new Vector3(0, 1, 0));
        bone(HumanBodyBones.Spine, HumanBodyBones.Hips, new Vector3(0, .2f, 0));
        bone(HumanBodyBones.Chest, HumanBodyBones.Spine, new Vector3(0, .2f, 0));
        bone(HumanBodyBones.Neck, HumanBodyBones.Chest, new Vector3(0, .15f, 0));
        bone(HumanBodyBones.Head, HumanBodyBones.Neck, new Vector3(0, .12f, 0));
        bone(HumanBodyBones.LeftUpperLeg, HumanBodyBones.Hips, new Vector3(-.12f, -.08f, 0));
        bone(HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftUpperLeg, new Vector3(0, -.4f, 0));
        bone(HumanBodyBones.LeftFoot, HumanBodyBones.LeftLowerLeg, new Vector3(0, -.4f, .05f));
        bone(HumanBodyBones.RightUpperLeg, HumanBodyBones.Hips, new Vector3(.12f, -.08f, 0));
        bone(HumanBodyBones.RightLowerLeg, HumanBodyBones.RightUpperLeg, new Vector3(0, -.4f, 0));
        bone(HumanBodyBones.RightFoot, HumanBodyBones.RightLowerLeg, new Vector3(0, -.4f, .05f));
        bone(HumanBodyBones.LeftShoulder, HumanBodyBones.Chest, new Vector3(-.12f, .1f, 0));
        bone(HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftShoulder, new Vector3(-.15f, 0, 0));
        bone(HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftUpperArm, new Vector3(-.3f, 0, 0));
        bone(HumanBodyBones.LeftHand, HumanBodyBones.LeftLowerArm, new Vector3(-.25f, 0, 0));
        bone(HumanBodyBones.RightShoulder, HumanBodyBones.Chest, new Vector3(.12f, .1f, 0));
        bone(HumanBodyBones.RightUpperArm, HumanBodyBones.RightShoulder, new Vector3(.15f, 0, 0));
        bone(HumanBodyBones.RightLowerArm, HumanBodyBones.RightUpperArm, new Vector3(.3f, 0, 0));
        bone(HumanBodyBones.RightHand, HumanBodyBones.RightLowerArm, new Vector3(.25f, 0, 0));
        var description = new HumanDescription {
            human = map.Select(p => new HumanBone { boneName = p.Value.name, humanName = HumanTrait.BoneName[(int)p.Key], limit = new HumanLimit { useDefaultValues = true } }).ToArray(),
            skeleton = root.GetComponentsInChildren<Transform>().Select(t => new SkeletonBone { name = t.name, position = t.localPosition, rotation = t.localRotation, scale = t.localScale }).ToArray(),
            upperArmTwist = .5f, lowerArmTwist = .5f, upperLegTwist = .5f, lowerLegTwist = .5f, armStretch = .05f, legStretch = .05f
        };
        var avatar = AvatarBuilder.BuildHumanAvatar(root, description);
        Require(avatar.isValid && avatar.isHuman, "Disposable humanoid rig is invalid");
        root.AddComponent<Animator>().avatar = avatar;
        var descriptor = root.AddComponent<VRCAvatarDescriptor>();
        descriptor.baseAnimationLayers = new[] { VRCAvatarDescriptor.AnimLayerType.Base, VRCAvatarDescriptor.AnimLayerType.Additive,
            VRCAvatarDescriptor.AnimLayerType.Gesture, VRCAvatarDescriptor.AnimLayerType.Action, VRCAvatarDescriptor.AnimLayerType.FX }
            .Select(type => new VRCAvatarDescriptor.CustomAnimLayer { type = type, isDefault = true }).ToArray();
        descriptor.specialAnimationLayers = new[] { VRCAvatarDescriptor.AnimLayerType.Sitting, VRCAvatarDescriptor.AnimLayerType.TPose, VRCAvatarDescriptor.AnimLayerType.IKPose }
            .Select(type => new VRCAvatarDescriptor.CustomAnimLayer { type = type, isDefault = true }).ToArray();
        descriptor.customExpressions = true;
        return root;
    }

    static object CheckBuild(GameObject single, GameObject four, int count, bool optimize, string presetPath, bool initiallyInactive = false, bool editorOnly = false)
    {
        var scene = EditorSceneManager.NewPreviewScene();
        try
        {
            var avatar = MakeHumanoid();
            UnityEngine.SceneManagement.SceneManager.MoveGameObjectToScene(avatar, scene);
            var badges = (GameObject)PrefabUtility.InstantiatePrefab(count == 1 ? single : four, scene);
            badges.transform.SetParent(avatar.transform, false);
            // SDK preprocessing operates on a build copy, without editor prefab connections.
            PrefabUtility.UnpackPrefabInstance(badges, PrefabUnpackMode.Completely, InteractionMode.AutomatedAction);
            if (count == 3)
            {
                UnityEngine.Object.DestroyImmediate(badges.transform.GetChild(3).gameObject);
            }
            if (initiallyInactive) badges.SetActive(false);
            if (editorOnly) badges.tag = "EditorOnly";
            var menuHookType = FindType("Furroxide.ShockOsc.Editor.ShockOscMenuBuildHook");
            if (menuHookType != null)
            {
                var menuHook = Activator.CreateInstance(menuHookType);
                Require((int)menuHookType.GetProperty("callbackOrder").GetValue(menuHook) < -10000, "Shared menu must be installed before VRCFury");
                Require((bool)menuHookType.GetMethod("OnPreprocessAvatar").Invoke(menuHook, new object[] { avatar }), "Shared menu installation failed");
                // Running preparation twice must not add the shared menu twice.
                Require((bool)menuHookType.GetMethod("OnPreprocessAvatar").Invoke(menuHook, new object[] { avatar }), "Repeated menu preparation failed");
            }
            var hookType = FindType("VF.Hooks.VrcPreuploadHook");
            Require(hookType != null, "VRCFury pre-upload hook is unavailable");
            Require((bool)hookType.GetMethod("OnPreprocessAvatar").Invoke(Activator.CreateInstance(hookType, true), new object[] { avatar }), "VRCFury rejected the disposable avatar");
            if (optimize)
            {
                var type = FindType("d4rkAvatarOptimizer");
                Require(type != null, "Optimizer type is unavailable");
                var optimizer = avatar.AddComponent(type);
                Require(AssetDatabase.LoadAssetAtPath<Preset>(presetPath).ApplyTo(optimizer), "Optimizer preset did not apply");
                var automatic = type.GetProperty("DoAutoSettings");
                if (automatic != null) automatic.SetValue(optimizer, false);
                else type.GetField("DoAutoSettings")?.SetValue(optimizer, false);
                type.GetMethod("Optimize").Invoke(optimizer, null);
            }
            var descriptor = avatar.GetComponent<VRCAvatarDescriptor>();
            if (editorOnly)
            {
                var excludedControls = ReadMenu(descriptor.expressionsMenu);
                Require(!excludedControls.Any(c => c.parameter != null && c.parameter.name == "Shocker/Toggle"), "EditorOnly badges must not install a Shocker toggle");
                Require(descriptor.expressionParameters == null || !descriptor.expressionParameters.parameters.Any(p => p.name.StartsWith("ShockOsc/_Config/", StringComparison.Ordinal)), "EditorOnly badges must not install configuration parameters");
                return new { count, editorOnly, sharedMenuInstalled = false };
            }
            var menuControls = CheckMenus(descriptor);
            if (initiallyInactive) return new { count, initiallyInactive, optimized = optimize, menuControls };
            var expressionParameters = descriptor.expressionParameters.parameters.Select(p => new { p.name, p.saved, p.defaultValue, p.networkSynced }).ToArray();
            var fx = descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            Require(fx != null, "Built FX controller is missing");
            var receivers = avatar.GetComponentsInChildren<VRCContactReceiver>(true);
            Require(receivers.Length == count, "Built receiver count changed");
            var animator = avatar.GetComponent<Animator>();
            animator.runtimeAnimatorController = fx;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Rebind();
            var configInteractions = CheckConfigInteractions(animator);
            var expectedBones = new Dictionary<string, HumanBodyBones> {
                { "ShockOsc/Lower-Left", HumanBodyBones.LeftUpperLeg }, { "ShockOsc/Lower-Right", HumanBodyBones.RightUpperLeg },
                { "ShockOsc/Upper-Left", HumanBodyBones.LeftUpperArm }, { "ShockOsc/Upper-Right", HumanBodyBones.RightUpperArm }
            };
            Write("attachment-debug-" + count + ".json", new {
                optimized = optimize,
                root = avatar.GetInstanceID(), rootTransform = avatar.transform.GetInstanceID(),
                bones = expectedBones.Select(p => new { p.Key, bone = animator.GetBoneTransform(p.Value) == null ? "NULL" : AnimationUtility.CalculateTransformPath(animator.GetBoneTransform(p.Value), avatar.transform),
                    id = animator.GetBoneTransform(p.Value)?.GetInstanceID(), rootId = animator.GetBoneTransform(p.Value)?.root.GetInstanceID() }).ToArray(),
                receivers = receivers.Select(c => new { c.parameter, path = AnimationUtility.CalculateTransformPath(c.transform, avatar.transform), ancestors = c.GetComponentsInParent<Transform>(true).Select(t => new { t.name, id = t.GetInstanceID() }).ToArray() }).ToArray(),
                rigBones = avatar.GetComponentsInChildren<Transform>(true).Where(t => t.name == "LeftUpperLeg").Select(t => new { t.name, id = t.GetInstanceID(), rootId = t.root.GetInstanceID() }).ToArray()
            });
            foreach (var receiver in receivers)
            {
                Require(expectedBones.ContainsKey(receiver.parameter), "Unexpected receiver channel: " + receiver.parameter);
                var intendedBone = animator.GetBoneTransform(expectedBones[receiver.parameter]);
                Require(intendedBone != null, "Humanoid bone is missing for " + receiver.parameter);
                var parentIds = receiver.GetComponentsInParent<Transform>(true).Select(parent => parent.GetInstanceID()).ToArray();
                Require(parentIds.Contains(intendedBone.GetInstanceID()), "Receiver attached to the wrong limb: " + receiver.parameter
                    + "; expected bone " + intendedBone.GetInstanceID() + "; ancestors " + string.Join(",", parentIds));
            }
            var invalid = new List<string>();
            foreach (var clip in fx.animationClips.Distinct()) foreach (var binding in AnimationUtility.GetCurveBindings(clip))
            {
                if (binding.type == typeof(Animator)) continue;
                var t = string.IsNullOrEmpty(binding.path) ? avatar.transform : avatar.transform.Find(binding.path);
                if (t == null || (binding.type != typeof(GameObject) && t.GetComponent(binding.type) == null)) invalid.Add(clip.name + ": " + binding.path + " / " + binding.propertyName);
            }
            Require(invalid.Count == 0, "Invalid animation bindings: " + string.Join(", ", invalid));
            var snapshots = new List<object>();
            var renderers = avatar.GetComponentsInChildren<Renderer>(true);
            var emissionNames = fx.animationClips.SelectMany(c => AnimationUtility.GetCurveBindings(c)).Where(b => b.propertyName.Contains("EmissionColor")).Select(b => b.propertyName.Substring(9).Split('.')[0]).Distinct().ToArray();
            foreach (string state in new[] { "Disabled", "Idle", "Active", "Cooldown", "Disabled", "Idle" })
            {
                animator.SetBool("Shocker/Toggle", state != "Disabled");
                animator.SetBool("ShockOsc/_Any_Active", state == "Active");
                animator.SetBool("ShockOsc/_Any_Cooldown", state == "Cooldown");
                animator.Update(0); animator.Update(.05f); animator.Update(.05f);
                int enabled = receivers.Count(c => c.enabled && c.gameObject.activeInHierarchy);
                Require(enabled == (state == "Disabled" ? 0 : count), "Contact enablement failed in " + state);
                var colours = new List<Vector4>();
                foreach (var renderer in renderers)
                {
                    var block = new MaterialPropertyBlock(); renderer.GetPropertyBlock(block);
                    foreach (var property in emissionNames)
                    {
                        colours.Add(block.GetVector(property));
                        foreach (var material in renderer.sharedMaterials) if (material.HasProperty(property)) colours.Add(material.GetVector(property));
                    }
                }
                if (state == "Active") Require(colours.Any(c => c.y > 0 && c.y > c.x), "Active green emission did not animate");
                if (state == "Cooldown") Require(colours.Any(c => c.x > 0 && c.x > c.y), "Cooldown red emission did not animate");
                snapshots.Add(new { state, enabledContacts = enabled, emission = colours.Select(c => new[] { c.x, c.y, c.z, c.w }).ToArray() });
            }
            int triangles = avatar.GetComponentsInChildren<MeshFilter>(true).Sum(m => m.sharedMesh.triangles.Length / 3)
                + avatar.GetComponentsInChildren<SkinnedMeshRenderer>(true).Sum(m => m.sharedMesh.triangles.Length / 3);
            int slots = renderers.Sum(r => r.sharedMaterials.Length);
            Require(triangles == count * 244, "Build changed triangle count");
            if (optimize) Require(renderers.Length == 1 && slots == 1, "Optimizer did not produce one renderer and material slot");
            Require(renderers.SelectMany(r => r.sharedMaterials).All(m => m != null && m.shader != null && !ShaderUtil.ShaderHasError(m.shader)), "Shader compilation failed");
            var emissionCurves = fx.animationClips.SelectMany(c => AnimationUtility.GetCurveBindings(c).Where(b => b.propertyName.Contains("EmissionColor")).Select(b => new { property = b.propertyName, max = AnimationUtility.GetEditorCurve(c, b).keys.Max(k => k.value) })).ToArray();
            Require(emissionCurves.Any(c => c.property.EndsWith(".g") && c.max > 0) && emissionCurves.Any(c => c.property.EndsWith(".r") && c.max > 0), "Green/red emission curves were lost");
            return new { count, optimized = optimize, triangles, renderers = renderers.Length, materialSlots = slots, menuControls, configInteractions, expressionParameters, invalidBindings = invalid, snapshots, emissionCurves, contacts = receivers.Select(c => new { c.parameter, path = AnimationUtility.CalculateTransformPath(c.transform, avatar.transform) }).ToArray() };
        }
        finally { EditorSceneManager.ClosePreviewScene(scene); }
    }

    static List<VRCExpressionsMenu.Control> ReadMenu(VRCExpressionsMenu root)
    {
        var controls = new List<VRCExpressionsMenu.Control>();
        var seen = new HashSet<VRCExpressionsMenu>();
        Action<VRCExpressionsMenu> visit = null;
        visit = menu => {
            if (menu == null || !seen.Add(menu)) return;
            Require(menu.controls.Count <= 8, "Built menu exceeds VRChat's eight-control limit: " + menu.name);
            foreach (var control in menu.controls)
            {
                controls.Add(control);
                if (control.type == VRCExpressionsMenu.Control.ControlType.SubMenu) visit(control.subMenu);
            }
        };
        visit(root);
        return controls;
    }

    static object CheckMenus(VRCAvatarDescriptor descriptor)
    {
        var controls = ReadMenu(descriptor.expressionsMenu);
        var toggles = controls.Where(c => c.parameter != null && c.parameter.name == "Shocker/Toggle").ToArray();
        Require(toggles.Length == 1, "Expected one shared Shocker toggle; found " + toggles.Length);
        Require(toggles[0].type == VRCExpressionsMenu.Control.ControlType.Toggle, "Shocker must remain a toggle");
        var bools = new[] { "Paused", "ModeIntensity", "ModeDuration" };
        var floats = new[] { "Intensity", "MinIntensity", "MaxIntensity", "Duration", "MinDuration", "MaxDuration", "CooldownTime", "HoldTime" };
        foreach (string setting in bools.Concat(floats))
        {
            string name = "ShockOsc/_Config/_All/" + setting;
            bool isBool = bools.Contains(setting);
            var items = controls.Where(c => isBool ? c.parameter != null && c.parameter.name == name
                : c.subParameters != null && c.subParameters.Any(p => p.name == name)).ToArray();
            Require(items.Length == 1, "Expected one configuration control for " + name + "; found " + items.Length);
            Require(items[0].type == (isBool ? VRCExpressionsMenu.Control.ControlType.Toggle : VRCExpressionsMenu.Control.ControlType.RadialPuppet), "Incorrect configuration control type: " + name);
            var parameters = descriptor.expressionParameters.parameters.Where(p => p.name == name).ToArray();
            Require(parameters.Length == 1 && parameters[0].valueType == (isBool ? VRCExpressionParameters.ValueType.Bool : VRCExpressionParameters.ValueType.Float), "Configuration parameter missing, duplicated or mistyped: " + name);
            Require(!parameters[0].saved && !parameters[0].networkSynced, "Bridge settings must be unsaved and local: " + name);
            var fx = (AnimatorController)descriptor.baseAnimationLayers.First(l => l.type == VRCAvatarDescriptor.AnimLayerType.FX).animatorController;
            Require(fx.parameters.Count(p => p.name == name && p.type == (isBool ? AnimatorControllerParameterType.Bool : AnimatorControllerParameterType.Float)) == 1, "Configuration animator parameter missing, duplicated or renamed: " + name);
        }
        return controls.Select(c => new { c.name, type = c.type.ToString(), parameter = c.parameter?.name, subParameters = c.subParameters?.Select(p => p.name).ToArray() }).ToArray();
    }

    static int CheckConfigInteractions(Animator animator)
    {
        int checkedValues = 0;
        foreach (var parameter in animator.parameters.Where(p => p.name.StartsWith("ShockOsc/_Config/_All/", StringComparison.Ordinal)))
        {
            if (parameter.type == AnimatorControllerParameterType.Bool)
            {
                foreach (bool value in new[] { true, false })
                {
                    animator.SetBool(parameter.name, value); animator.Update(.05f);
                    Require(animator.GetBool(parameter.name) == value, "Animator overwrites the configuration toggle: " + parameter.name);
                    checkedValues++;
                }
            }
            else
            {
                foreach (float value in new[] { 0f, .25f, .75f, 1f, 0f })
                {
                    animator.SetFloat(parameter.name, value); animator.Update(.05f);
                    Require(Mathf.Approximately(animator.GetFloat(parameter.name), value), "Animator overwrites the configuration radial: " + parameter.name);
                    checkedValues++;
                }
            }
        }
        Require(checkedValues == 46, "Not all configuration controls were exercised");
        return checkedValues;
    }
}
