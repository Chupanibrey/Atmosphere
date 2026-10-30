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
