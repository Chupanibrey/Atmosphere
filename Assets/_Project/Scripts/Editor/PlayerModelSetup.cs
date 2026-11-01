using System.IO;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

public static class PlayerModelSetup
{
    const string ModelPath = "Assets/_Project/Models/casual-male-3d-character-t-pose/source/model.fbx";
    const string IdlePath = "Assets/_Project/Animations/Player/Idle.fbx";
    const string WalkPath = "Assets/_Project/Animations/Player/Walking.fbx";
    const string RunPath = "Assets/_Project/Animations/Player/Running.fbx";
    const string CrouchPath = "Assets/_Project/Animations/Player/Crouched Walking.fbx";
    const string JumpPath = "Assets/_Project/Animations/Player/Jumping.fbx";
    const string FallPath = "Assets/_Project/Animations/Player/Falling Idle.fbx";
    const string ControllerPath = "Assets/_Project/Animations/Player/Player.controller";

    public static string ClipMotion()
    {
        var report = new System.Text.StringBuilder();
        report.Append(MotionRange(LoadClip(IdlePath))).Append(" | ");
        report.Append(MotionRange(LoadClip(WalkPath))).Append(" | ");
        report.Append(MotionRange(LoadClip(RunPath)));
        return report.ToString();
    }

    static string MotionRange(AnimationClip clip)
    {
        float min = float.PositiveInfinity;
        float max = float.NegativeInfinity;
        int curves = 0;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.propertyName.IndexOf("Arm", System.StringComparison.Ordinal) < 0)
                continue;

            var curve = AnimationUtility.GetEditorCurve(clip, binding);
            if (curve == null)
                continue;

            curves++;
            foreach (var key in curve.keys)
            {
            if (Mathf.Abs(key.value) < 0.02f)
                continue;

                if (key.time < min)
                    min = key.time;
                if (key.time > max)
                    max = key.time;
            }
        }

        if (curves == 0)
            return clip.name + " no-arm-curves len=" + clip.length.ToString("0.00");

        if (float.IsPositiveInfinity(min))
            return clip.name + " arms-flat len=" + clip.length.ToString("0.00") + " curves=" + curves;

        return clip.name + " " + min.ToString("0.00") + "-" + max.ToString("0.00")
            + " len=" + clip.length.ToString("0.00") + " curves=" + curves;
    }

    public static string SamplePoses()
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(root);
        var hand = FindDeep(instance.transform, "LeftHand");
        var report = new System.Text.StringBuilder();
        AnimationMode.StartAnimationMode();
        try
        {
            Sample(instance, hand, LoadClip(IdlePath), new[] { 0f, 0.4f, 2f, 4f, 7.5f }, report);
            Sample(instance, hand, LoadClip(WalkPath), new[] { 0f, 0.25f, 0.5f, 0.9f }, report);
            Sample(instance, hand, LoadClip(RunPath), new[] { 0f, 0.2f, 0.45f }, report);
        }
        finally
        {
            AnimationMode.StopAnimationMode();
            Object.DestroyImmediate(instance);
        }

        return report.ToString();
    }

    static void Sample(GameObject instance, Transform hand, AnimationClip clip, float[] times, System.Text.StringBuilder report)
    {
        report.Append(clip.name).Append(" len=").Append(clip.length.ToString("0.00")).Append(" ");
        foreach (var time in times)
        {
            AnimationMode.SampleAnimationClip(instance, clip, Mathf.Min(time, clip.length));
            report.Append(time.ToString("0.00")).Append(":").Append(hand.localPosition.y.ToString("0.00")).Append(" ");
        }
    }

    static Transform FindDeep(Transform root, string name)
    {
        if (root.name == name)
            return root;

        for (int i = 0; i < root.childCount; i++)
        {
            var found = FindDeep(root.GetChild(i), name);
            if (found != null)
                return found;
        }

        return null;
    }

    public static string Diagnose()
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var animator = root.GetComponentInChildren<Animator>(true);
        string avatar = "no-animator";
        if (animator != null)
        {
            avatar = animator.avatar == null
                ? "avatar-null"
                : animator.avatar.name + " human=" + animator.avatar.isHuman + " valid=" + animator.avatar.isValid;
        }

        var idle = LoadClip(IdlePath);
        var walk = LoadClip(WalkPath);
        return avatar
            + " idle=" + idle.length.ToString("0.00") + "s"
            + " walk=" + walk.length.ToString("0.00") + "s"
            + " controller=" + (AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath) != null);
    }

    public static string Measure()
    {
        var root = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var instance = (GameObject)PrefabUtility.InstantiatePrefab(root);
        var renderers = instance.GetComponentsInChildren<Renderer>(true);
        var bounds = new Bounds(instance.transform.position, Vector3.zero);
        bool any = false;
        var names = new System.Text.StringBuilder();
        foreach (var renderer in renderers)
        {
            if (!any)
            {
                bounds = renderer.bounds;
                any = true;
            }
            else
            {
                bounds.Encapsulate(renderer.bounds);
            }

            names.Append(renderer.name).Append('|');
        }

        float height = bounds.size.y;
        float min = bounds.min.y;
        Object.DestroyImmediate(instance);
        return names + " h=" + height.ToString("0.00") + " min=" + min.ToString("0.00");
    }

    public static string Attach()
    {
        const string prefabPath = "Assets/_Project/Prefabs/Player.prefab";
        var root = PrefabUtility.LoadPrefabContents(prefabPath);
        var body = root.GetComponent<PlayerBody>();
        if (body == null)
            body = root.AddComponent<PlayerBody>();

        var serialized = new SerializedObject(body);
        serialized.FindProperty("model").objectReferenceValue = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        serialized.FindProperty("controller").objectReferenceValue =
            AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
        serialized.ApplyModifiedPropertiesWithoutUndo();

        PrefabUtility.SaveAsPrefabAsset(root, prefabPath);
        PrefabUtility.UnloadPrefabContents(root);
        return "attached";
    }

    public static string Configure()
    {
        var avatar = ConfigureModel();
        ConfigureClip(IdlePath, avatar, "Idle");
        ConfigureClip(WalkPath, avatar, "Walk");
        ConfigureClip(RunPath, avatar, "Run");
        ConfigureClip(CrouchPath, avatar, "Crouch");
        var controller = BuildController();
        return "ok " + controller.name;
    }

    static Avatar ConfigureModel()
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CreateFromThisModel;
        importer.importCameras = false;
        importer.importLights = false;
        importer.SaveAndReimport();

        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is Avatar avatar)
                return avatar;
        }

        throw new FileNotFoundException("Avatar was not created for " + ModelPath);
    }

    static void ConfigureClip(string path, Avatar avatar, string clipName)
    {
        var importer = (ModelImporter)AssetImporter.GetAtPath(path);
        importer.animationType = ModelImporterAnimationType.Human;
        importer.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        importer.sourceAvatar = avatar;
        importer.importCameras = false;
        importer.importLights = false;

        var clips = importer.defaultClipAnimations;
        if (clips == null || clips.Length == 0)
            clips = importer.clipAnimations;

        for (int i = 0; i < clips.Length; i++)
        {
            clips[i].name = clipName;
            clips[i].loopTime = true;
            clips[i].loopPose = true;
            clips[i].lockRootRotation = true;
            clips[i].lockRootHeightY = true;
            clips[i].lockRootPositionXZ = true;
            clips[i].keepOriginalPositionY = true;
            clips[i].keepOriginalOrientation = true;
        }

        importer.clipAnimations = clips;
        importer.SaveAndReimport();
    }

    static AnimatorController BuildController()
    {
        if (File.Exists(ControllerPath))
            AssetDatabase.DeleteAsset(ControllerPath);

        var controller = AnimatorController.CreateAnimatorControllerAtPath(ControllerPath);
        controller.AddParameter("Speed", AnimatorControllerParameterType.Float);
        controller.AddParameter("Crouch", AnimatorControllerParameterType.Bool);
        controller.AddParameter("Rate", AnimatorControllerParameterType.Float);

        var idle = LoadClip(IdlePath);
        var walk = LoadClip(WalkPath);
        var run = LoadClip(RunPath);
        var crouch = LoadClip(CrouchPath);

        var machine = controller.layers[0].stateMachine;
        var locomotion = machine.AddState("Locomotion");
        locomotion.motion = MakeLocomotion(controller, idle, walk, run);
        var crouchState = machine.AddState("Crouch");
        crouchState.motion = crouch;
        crouchState.speedParameter = "Rate";
        crouchState.speedParameterActive = true;
        machine.defaultState = locomotion;

        var toCrouch = locomotion.AddTransition(crouchState);
        toCrouch.hasExitTime = false;
        toCrouch.duration = 0.2f;
        toCrouch.offset = 0f;
        toCrouch.AddCondition(AnimatorConditionMode.If, 0f, "Crouch");

        var toMove = crouchState.AddTransition(locomotion);
        toMove.hasExitTime = false;
        toMove.duration = 0.22f;
        toMove.offset = 0f;
        toMove.AddCondition(AnimatorConditionMode.IfNot, 0f, "Crouch");

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return controller;
    }

    static BlendTree MakeLocomotion(AnimatorController controller, AnimationClip idle, AnimationClip walk, AnimationClip run)
    {
        var tree = new BlendTree
        {
            name = "Locomotion",
            blendParameter = "Speed",
            useAutomaticThresholds = false,
            blendType = BlendTreeType.Simple1D
        };
        tree.AddChild(idle, 0f);
        tree.AddChild(walk, 1.5f);
        tree.AddChild(run, 4.5f);
        AssetDatabase.AddObjectToAsset(tree, controller);
        return tree;
    }

    public static string AddAir()
    {
        var avatar = FindAvatar();
        ConfigureAir(avatar);

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        var machine = controller.layers[0].stateMachine;
        var locomotion = FindState(machine, "Locomotion");
        var crouch = FindState(machine, "Crouch");

        RemoveAirState(machine, "Jump");
        RemoveAirState(machine, "Fall");
        RemoveAirState(machine, "Falling Idle");
        RemoveAirState(machine, "Land");

        EnsureParameter(controller, "Jump", AnimatorControllerParameterType.Trigger);
        EnsureParameter(controller, "Fall", AnimatorControllerParameterType.Bool);
        EnsureParameter(controller, "Grounded", AnimatorControllerParameterType.Bool);

        var jump = machine.AddState("Jump");
        jump.motion = FindClip(JumpPath, "Jump");
        jump.speed = 1.7f;

        var fall = machine.AddState("Fall");
        fall.motion = FindClip(JumpPath, "Fall");

        var fallingIdle = machine.AddState("Falling Idle");
        fallingIdle.motion = FindClip(FallPath, "Falling Idle");

        var land = machine.AddState("Land");
        land.motion = FindClip(JumpPath, "Land");

        AddTrigger(locomotion, jump, "Jump", 0.06f);
        AddBool(locomotion, fallingIdle, "Fall", true, 0.12f, false, 0f);
        AddBool(crouch, fallingIdle, "Fall", true, 0.12f, false, 0f);

        AddBool(jump, land, "Grounded", true, 0.05f, false, 0f);
        AddExit(jump, fall, 0.92f, 0.04f);

        AddBool(fall, land, "Grounded", true, 0.05f, false, 0f);

        AddBool(fallingIdle, land, "Grounded", true, 0.1f, false, 0f);
        AddTrigger(land, jump, "Jump", 0.06f);
        AddBool(land, crouch, "Crouch", true, 0.16f, true, 0.8f);
        AddBool(land, locomotion, "Crouch", false, 0.16f, true, 0.8f);

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();

        var tree = locomotion.motion as BlendTree;
        float max = tree != null ? tree.maxThreshold : -1f;
        return "max=" + max.ToString("0.00")
            + " jump=" + jump.motion.name + ":" + ((AnimationClip)jump.motion).length.ToString("0.00")
            + " fall=" + ((AnimationClip)fall.motion).length.ToString("0.00")
            + " idle=" + ((AnimationClip)fallingIdle.motion).length.ToString("0.00")
            + " land=" + ((AnimationClip)land.motion).length.ToString("0.00");
    }

    static Avatar FindAvatar()
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is Avatar avatar)
                return avatar;
        }

        throw new FileNotFoundException("Avatar was not created for " + ModelPath);
    }

    static void ConfigureAir(Avatar avatar)
    {
        AssetDatabase.ImportAsset(JumpPath, ImportAssetOptions.ForceUpdate);
        AssetDatabase.ImportAsset(FallPath, ImportAssetOptions.ForceUpdate);

        var jumpImporter = (ModelImporter)AssetImporter.GetAtPath(JumpPath);
        var fallImporter = (ModelImporter)AssetImporter.GetAtPath(FallPath);
        if (HasClip(jumpImporter, "Jump") && HasClip(jumpImporter, "Fall") && HasClip(jumpImporter, "Land")
            && HasClip(fallImporter, "Falling Idle"))
            return;

        jumpImporter.animationType = ModelImporterAnimationType.Human;
        jumpImporter.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        jumpImporter.sourceAvatar = avatar;
        jumpImporter.importCameras = false;
        jumpImporter.importLights = false;

        var existing = jumpImporter.clipAnimations;
        if (existing == null || existing.Length == 0)
            existing = jumpImporter.defaultClipAnimations;

        var source = existing[0];
        jumpImporter.clipAnimations = new[]
        {
            Slice(source, "Jump", 0, 50, false, -203655887218126122L),
            Slice(source, "Fall", 50, 74, false, 4061839508520268033L),
            Slice(source, "Land", 74, 113, false, 7382917461029384756L)
        };
        jumpImporter.SaveAndReimport();

        fallImporter.animationType = ModelImporterAnimationType.Human;
        fallImporter.avatarSetup = ModelImporterAvatarSetup.CopyFromOther;
        fallImporter.sourceAvatar = avatar;
        fallImporter.importCameras = false;
        fallImporter.importLights = false;

        var fallClips = fallImporter.clipAnimations;
        if (fallClips == null || fallClips.Length == 0)
            fallClips = fallImporter.defaultClipAnimations;

        var idle = Slice(fallClips[0], "Falling Idle", (int)fallClips[0].firstFrame, (int)fallClips[0].lastFrame, true, ClipId(fallClips[0]));
        fallImporter.clipAnimations = new[] { idle };
        fallImporter.SaveAndReimport();
    }

    static bool HasClip(ModelImporter importer, string clipName)
    {
        var clips = importer.clipAnimations;
        if (clips == null)
            return false;

        foreach (var clip in clips)
        {
            if (clip.name == clipName)
                return true;
        }

        return false;
    }

    static ModelImporterClipAnimation Slice(ModelImporterClipAnimation source, string clipName, int first, int last, bool loop, long id)
    {
        source.name = clipName;
        source.firstFrame = first;
        source.lastFrame = last;
        source.loopTime = loop;
        source.loopPose = loop;
        source.lockRootRotation = true;
        // Дуга прыжка остаётся в root motion и не двигает скелет: камера сидит на глазах.
        source.lockRootHeightY = false;
        source.lockRootPositionXZ = true;
        source.keepOriginalPositionY = false;
        source.heightFromFeet = true;
        source.keepOriginalOrientation = true;
        source.keepOriginalPositionXZ = false;
        object boxed = source;
        IdField().SetValue(boxed, id);
        return (ModelImporterClipAnimation)boxed;
    }

    static long ClipId(ModelImporterClipAnimation clip)
    {
        object boxed = clip;
        return (long)IdField().GetValue(boxed);
    }

    static System.Reflection.FieldInfo IdField()
    {
        return typeof(ModelImporterClipAnimation).GetField(
            "internalID",
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public);
    }

    static void EnsureParameter(AnimatorController controller, string parameterName, AnimatorControllerParameterType type)
    {
        foreach (var parameter in controller.parameters)
        {
            if (parameter.name == parameterName)
                return;
        }

        controller.AddParameter(parameterName, type);
    }

    static AnimatorState FindState(AnimatorStateMachine machine, string stateName)
    {
        foreach (var child in machine.states)
        {
            if (child.state.name == stateName)
                return child.state;
        }

        throw new FileNotFoundException("Animator state was not found: " + stateName);
    }

    static void RemoveAirState(AnimatorStateMachine machine, string stateName)
    {
        AnimatorState found = null;
        foreach (var child in machine.states)
        {
            if (child.state.name == stateName)
                found = child.state;
        }

        if (found == null)
            return;

        foreach (var child in machine.states)
        {
            var transitions = child.state.transitions;
            for (int i = transitions.Length - 1; i >= 0; i--)
            {
                if (transitions[i].destinationState == found)
                    child.state.RemoveTransition(transitions[i]);
            }
        }

        machine.RemoveState(found);
    }

    static void AddTrigger(AnimatorState from, AnimatorState to, string parameterName, float duration)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = false;
        transition.hasFixedDuration = true;
        transition.duration = duration;
        transition.offset = 0f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(AnimatorConditionMode.If, 0f, parameterName);
    }

    static void AddBool(AnimatorState from, AnimatorState to, string parameterName, bool on, float duration, bool exit, float exitTime)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = exit;
        transition.exitTime = exitTime;
        transition.hasFixedDuration = true;
        transition.duration = duration;
        transition.offset = 0f;
        transition.canTransitionToSelf = false;
        transition.AddCondition(on ? AnimatorConditionMode.If : AnimatorConditionMode.IfNot, 0f, parameterName);
    }

    static void AddExit(AnimatorState from, AnimatorState to, float exitTime, float duration)
    {
        var transition = from.AddTransition(to);
        transition.hasExitTime = true;
        transition.exitTime = exitTime;
        transition.hasFixedDuration = true;
        transition.duration = duration;
        transition.offset = 0f;
        transition.canTransitionToSelf = false;
    }

    static AnimationClip FindClip(string path, string clipName)
    {
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip clip && clip.name == clipName)
                return clip;
        }

        throw new FileNotFoundException(clipName + " was not found in " + path);
    }

    public static string SmoothJump()
    {
        GroundAirClips();

        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        EnsureParameter(controller, "AirRate", AnimatorControllerParameterType.Float);
        var parameters = controller.parameters;
        for (int i = 0; i < parameters.Length; i++)
        {
            if (parameters[i].name == "AirRate")
                parameters[i].defaultFloat = 1f;
        }

        controller.parameters = parameters;

        var jump = FindState(controller.layers[0].stateMachine, "Jump");
        var transitions = jump.transitions;
        for (int i = transitions.Length - 1; i >= 0; i--)
            jump.RemoveTransition(transitions[i]);

        jump.motion = FindClip(JumpPath, "Arc");
        jump.speed = 1f;
        jump.speedParameter = "AirRate";
        jump.speedParameterActive = true;

        EditorUtility.SetDirty(controller);
        AssetDatabase.SaveAssets();
        return ProbeFeet();
    }

    static void GroundAirClips()
    {
        var jumpImporter = (ModelImporter)AssetImporter.GetAtPath(JumpPath);
        var clips = jumpImporter.clipAnimations;
        bool hasArc = false;
        var next = new ModelImporterClipAnimation[clips.Length + 1];
        int count = 0;
        for (int i = 0; i < clips.Length; i++)
        {
            if (clips[i].name == "Arc")
            {
                hasArc = true;
                next[count++] = Slice(clips[i], "Arc", 0, 113, false, ClipId(clips[i]));
                continue;
            }

            next[count++] = Slice(clips[i], clips[i].name, (int)clips[i].firstFrame, (int)clips[i].lastFrame, clips[i].loopTime, ClipId(clips[i]));
        }

        if (!hasArc)
            next[count++] = Slice(clips[0], "Arc", 0, 113, false, 5918273645019283746L);

        if (count != next.Length)
        {
            var trimmed = new ModelImporterClipAnimation[count];
            for (int i = 0; i < count; i++)
                trimmed[i] = next[i];
            next = trimmed;
        }

        jumpImporter.clipAnimations = next;
        jumpImporter.SaveAndReimport();

        var fallImporter = (ModelImporter)AssetImporter.GetAtPath(FallPath);
        var fallClips = fallImporter.clipAnimations;
        fallClips[0] = Slice(fallClips[0], "Falling Idle", (int)fallClips[0].firstFrame, (int)fallClips[0].lastFrame, true, ClipId(fallClips[0]));
        fallImporter.clipAnimations = fallClips;
        fallImporter.SaveAndReimport();
    }

    public static string SoleGap()
    {
        var root = new GameObject("sole-probe");
        Mesh baked = null;
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            model.transform.localPosition = new Vector3(0f, 0.106f, 0f);
            model.transform.localScale = Vector3.one * 0.887f;

            var animator = model.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.Update(0f);

            var report = new System.Text.StringBuilder();
            ReportSole(animator, "Locomotion", 0f, 0f, report);
            ReportSole(animator, "Locomotion", 0.25f, 1.5f, report);
            ReportSole(animator, "Locomotion", 0.5f, 4.5f, report);
            ReportSole(animator, "Crouch", 0f, 0f, report);
            return report.ToString();
        }
        finally
        {
            if (baked != null)
                UnityEngine.Object.DestroyImmediate(baked);
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void ReportSole(Animator animator, string stateName, float normalizedTime, float speed, System.Text.StringBuilder report)
    {
        animator.SetFloat("Speed", speed);
        animator.SetFloat("Rate", 1f);
        animator.SetBool("Crouch", stateName == "Crouch");
        animator.Play(stateName, 0, normalizedTime);
        animator.Update(0f);

        float min = float.MaxValue;
        var skins = animator.GetComponentsInChildren<SkinnedMeshRenderer>();
        var baked = new Mesh();
        for (int s = 0; s < skins.Length; s++)
        {
            if (!skins[s].enabled || skins[s].sharedMesh == null)
                continue;

            baked.Clear();
            skins[s].BakeMesh(baked);
            var vertices = baked.vertices;
            for (int i = 0; i < vertices.Length; i++)
            {
                float y = skins[s].transform.TransformPoint(vertices[i]).y;
                if (y < min)
                    min = y;
            }
        }

        UnityEngine.Object.DestroyImmediate(baked);
        report.Append(stateName).Append(" t=").Append(normalizedTime.ToString("0.00"))
            .Append(" speed=").Append(speed.ToString("0.0"))
            .Append(" sole=").Append(min.ToString("0.000")).Append(" | ");
    }

    public static string ProbeFeet()
    {
        var root = new GameObject("feet-probe");
        try
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
            var model = (GameObject)PrefabUtility.InstantiatePrefab(prefab, root.transform);
            model.transform.localPosition = new Vector3(0f, 0.106f, 0f);
            model.transform.localScale = Vector3.one * 0.887f;

            var animator = model.GetComponentInChildren<Animator>();
            animator.runtimeAnimatorController = AssetDatabase.LoadAssetAtPath<RuntimeAnimatorController>(ControllerPath);
            animator.applyRootMotion = false;
            animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            animator.SetFloat("Speed", 0f);
            animator.Update(0f);

            var report = new System.Text.StringBuilder();
            SampleSole(animator, "Locomotion", 0f, report);
            SampleSole(animator, "Jump", 0f, report);
            SampleSole(animator, "Jump", 0.44f, report);
            SampleSole(animator, "Jump", 0.65f, report);
            SampleSole(animator, "Jump", 0.85f, report);
            SampleSole(animator, "Falling Idle", 0.3f, report);
            SampleSole(animator, "Land", 0.05f, report);
            SampleSole(animator, "Land", 0.25f, report);
            return report.ToString();
        }
        finally
        {
            UnityEngine.Object.DestroyImmediate(root);
        }
    }

    static void SampleSole(Animator animator, string stateName, float normalizedTime, System.Text.StringBuilder report)
    {
        animator.Play(stateName, 0, normalizedTime);
        animator.Update(0f);

        float sole = float.MaxValue;
        var renderers = animator.GetComponentsInChildren<Renderer>();
        for (int i = 0; i < renderers.Length; i++)
        {
            if (!renderers[i].enabled)
                continue;
            if (renderers[i].bounds.min.y < sole)
                sole = renderers[i].bounds.min.y;
        }

        float foot = float.MaxValue;
        var transforms = animator.GetComponentsInChildren<Transform>();
        for (int i = 0; i < transforms.Length; i++)
        {
            string bone = transforms[i].name;
            if (bone != "LeftFoot" && bone != "RightFoot" && bone != "LeftToeBase" && bone != "RightToeBase")
                continue;
            if (transforms[i].position.y < foot)
                foot = transforms[i].position.y;
        }

        report.Append(stateName).Append("@").Append(normalizedTime.ToString("0.00"))
            .Append(" mesh=").Append(sole.ToString("0.000"))
            .Append(" foot=").Append(foot.ToString("0.000")).Append(" | ");
    }

    public static string DescribeAir()
    {
        var report = new System.Text.StringBuilder();
        DescribeFile(JumpPath, report);
        DescribeFile(FallPath, report);
        return report.ToString();
    }

    static void DescribeFile(string path, System.Text.StringBuilder report)
    {
        var assets = AssetDatabase.LoadAllAssetsAtPath(path);
        foreach (var asset in assets)
        {
            if (asset is not AnimationClip clip || clip.name.StartsWith("__preview__"))
                continue;

            report.Append(clip.name)
                .Append(" len=").Append(clip.length.ToString("0.00"))
                .Append(" fps=").Append(clip.frameRate.ToString("0"))
                .Append(" loop=").Append(clip.isLooping ? 1 : 0)
                .Append(" ");
            AppendHeight(clip, report);
            report.Append(" | ");
        }
    }

    static void AppendHeight(AnimationClip clip, System.Text.StringBuilder report)
    {
        EditorCurveBinding? match = null;
        foreach (var binding in AnimationUtility.GetCurveBindings(clip))
        {
            if (binding.propertyName != "RootT.y"
                && binding.propertyName != "m_LocalPosition.y")
                continue;

            if (binding.propertyName == "RootT.y"
                || binding.path.EndsWith("Hips")
                || binding.path.Length == 0)
            {
                match = binding;
                if (binding.propertyName == "RootT.y")
                    break;
            }
        }

        if (match == null)
        {
            report.Append("no-y");
            return;
        }

        var height = match.Value;
        var curve = AnimationUtility.GetEditorCurve(clip, height);
        report.Append(height.path).Append(":").Append(height.propertyName).Append(" ");
        if (curve == null)
            return;

        float min = float.MaxValue;
        float max = float.MinValue;
        for (float time = 0f; time <= clip.length + 0.001f; time += 0.1f)
        {
            float value = curve.Evaluate(Mathf.Min(time, clip.length));
            if (value < min)
                min = value;
            if (value > max)
                max = value;
            report.Append(time.ToString("0.0")).Append("=").Append(value.ToString("0.00")).Append(" ");
        }

        report.Append("range ").Append(min.ToString("0.00")).Append("..").Append(max.ToString("0.00"));
    }

    public static string Rebake()
    {
        Avatar avatar = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is Avatar found)
                avatar = found;
        }

        if (avatar == null)
            throw new FileNotFoundException("Avatar was not created for " + ModelPath);

        ConfigureClip(IdlePath, avatar, "Idle");
        ConfigureClip(WalkPath, avatar, "Walk");
        ConfigureClip(RunPath, avatar, "Run");
        ConfigureClip(CrouchPath, avatar, "Crouch");
        BuildController();
        return "rebaked";
    }

    public static string AddWalk()
    {
        Avatar avatar = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(ModelPath))
        {
            if (asset is Avatar found)
                avatar = found;
        }

        if (avatar == null)
            throw new FileNotFoundException("Avatar was not created for " + ModelPath);

        ConfigureClip(WalkPath, avatar, "Walk");
        BuildController();
        return "walk";
    }

    public static string AssignTextures()
    {
        const string root = "Assets/_Project/Models/casual-male-3d-character-t-pose";
        const string folder = root + "/materials";
        if (!AssetDatabase.IsValidFolder(folder))
            AssetDatabase.CreateFolder(root, "materials");

        var shader = Shader.Find("Universal Render Pipeline/Lit");
        if (shader == null)
            throw new FileNotFoundException("URP Lit shader was not found.");

        var importer = (ModelImporter)AssetImporter.GetAtPath(ModelPath);
        Bind(importer, folder, shader, "AvatarBody", "AvatarBodyMale_Color_1K.jpeg", "AvatarBodyMale_Normal1_1K.jpeg", false);
        Bind(importer, folder, shader, "AvatarHead", "AvatarHeadMale_Color_1K.jpeg", "AvatarHeadMale_Normal1_1K.jpeg", false);
        Bind(importer, folder, shader, "AvatarLeftEyeball", "AvatarEyes_Color_512.jpeg", "AvatarEyes_Normal_512.jpeg", false);
        Bind(importer, folder, shader, "AvatarRightEyeball", "AvatarEyes_Color_512.jpeg", "AvatarEyes_Normal_512.jpeg", false);
        Bind(importer, folder, shader, "AvatarTeethLower", "AvatarTeeth_Color_1K.jpeg", "AvatarTeeth_Normal1_1K.jpeg", false);
        Bind(importer, folder, shader, "AvatarTeethUpper", "AvatarTeeth_Color_1K.jpeg", "AvatarTeeth_Normal1_1K.jpeg", false);
        Bind(importer, folder, shader, "haircut", "HairCard0_Color_1K.png", "HairCard0_Normal_1K.jpeg", true);
        Bind(importer, folder, shader, "AvatarEyelashes", "HairCard0_Color_1K.png", "HairCard0_Normal_1K.jpeg", true);
        Bind(importer, folder, shader, "outfit_bottom", "pants_Dzoraget_Color_1K.jpeg", "pants_Dzoraget_Normal_1K.jpeg", false);
        Bind(importer, folder, shader, "outfit_shoes", "sneakers_AZAT_Color_1K.jpeg", "sneakers_AZAT_Normal_1K.jpeg", false);
        Bind(importer, folder, shader, "outfit_top", "longsleeve_Debed_Color_1K.jpeg", "longsleeve_Debed_Normal_1K.jpeg", false);
        importer.SaveAndReimport();
        return "textures";
    }

    static void Bind(ModelImporter importer, string folder, Shader shader, string materialName, string colorFile, string normalFile, bool cutout)
    {
        const string textures = "Assets/_Project/Models/casual-male-3d-character-t-pose/textures/";
        var normalPath = textures + normalFile;
        var normalImporter = (TextureImporter)AssetImporter.GetAtPath(normalPath);
        if (normalImporter != null && normalImporter.textureType != TextureImporterType.NormalMap)
        {
            normalImporter.textureType = TextureImporterType.NormalMap;
            normalImporter.SaveAndReimport();
        }

        var materialPath = folder + "/" + materialName + ".mat";
        var material = AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        if (material == null)
        {
            material = new Material(shader);
            AssetDatabase.CreateAsset(material, materialPath);
        }

        material.shader = shader;
        material.SetTexture("_BaseMap", AssetDatabase.LoadAssetAtPath<Texture2D>(textures + colorFile));
        material.SetTexture("_BumpMap", AssetDatabase.LoadAssetAtPath<Texture2D>(normalPath));
        material.EnableKeyword("_NORMALMAP");
        material.SetFloat("_BumpScale", 1f);
        if (cutout)
        {
            material.SetFloat("_AlphaClip", 1f);
            material.SetFloat("_Cutoff", 0.4f);
            material.EnableKeyword("_ALPHATEST_ON");
            material.SetOverrideTag("RenderType", "TransparentCutout");
            material.renderQueue = (int)UnityEngine.Rendering.RenderQueue.AlphaTest;
        }

        EditorUtility.SetDirty(material);
        importer.AddRemap(new AssetImporter.SourceAssetIdentifier(typeof(Material), materialName), material);
    }

    static AnimationClip LoadClip(string path)
    {
        AnimationClip found = null;
        foreach (var asset in AssetDatabase.LoadAllAssetsAtPath(path))
        {
            if (asset is AnimationClip clip && !clip.name.StartsWith("__preview__"))
                found = clip;
        }

        if (found == null)
            throw new FileNotFoundException("No clip in " + path);

        return found;
    }
}
