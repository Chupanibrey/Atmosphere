#if UNITY_EDITOR
using System.Linq;
using UnityEditor;
using UnityEditor.Animations;
using UnityEngine;

/// <summary>
/// Привязывает клип монтировки к контроллеру и префабу игрока после импорта модели.
/// </summary>
static class CrowbarViewSetup
{
    const string ModelPath = "Assets/_Project/Animations/Crowbar/source/arms@crowbar.fbx";
    const string ControllerPath = "Assets/_Project/Animations/Crowbar/Crowbar.controller";
    const string PlayerPath = "Assets/_Project/Prefabs/Player.prefab";

    [InitializeOnLoadMethod]
    static void Schedule()
    {
        EditorApplication.delayCall += Bind;
    }

    public static void Bind()
    {
        if (EditorApplication.isPlayingOrWillChangePlaymode)
            return;

        var model = AssetDatabase.LoadAssetAtPath<GameObject>(ModelPath);
        var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(ControllerPath);
        if (model == null || controller == null)
            return;

        var clip = AssetDatabase.LoadAllAssetsAtPath(ModelPath)
            .OfType<AnimationClip>()
            .FirstOrDefault(item => !item.name.StartsWith("__preview__"));

        bool dirty = false;
        if (clip != null && controller.layers.Length > 0)
        {
            var state = controller.layers[0].stateMachine.states
                .Select(child => child.state)
                .FirstOrDefault(item => item != null && item.name == "Attack");
            if (state != null && state.motion != clip)
            {
                state.motion = clip;
                EditorUtility.SetDirty(controller);
                dirty = true;
            }
        }

        var player = AssetDatabase.LoadAssetAtPath<GameObject>(PlayerPath);
        var crowbar = player != null ? player.GetComponent<PlayerCrowbar>() : null;
        if (crowbar != null)
        {
            var serialized = new SerializedObject(crowbar);
            dirty |= Assign(serialized, "viewModel", model);
            dirty |= Assign(serialized, "controller", controller);
            dirty |= Assign(serialized, "armsMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Animations/Crowbar/Arms.mat"));
            dirty |= Assign(serialized, "crowbarMaterial", AssetDatabase.LoadAssetAtPath<Material>("Assets/_Project/Animations/Crowbar/Crowbar.mat"));
            if (serialized.hasModifiedProperties)
                serialized.ApplyModifiedPropertiesWithoutUndo();
        }

        if (dirty)
            AssetDatabase.SaveAssets();
    }

    static bool Assign(SerializedObject serialized, string property, Object value)
    {
        if (value == null)
            return false;

        var prop = serialized.FindProperty(property);
        if (prop == null || prop.objectReferenceValue == value)
            return false;

        prop.objectReferenceValue = value;
        return true;
    }
}

sealed class CrowbarImportHook : AssetPostprocessor
{
    static void OnPostprocessAllAssets(string[] imported, string[] deleted, string[] moved, string[] movedFrom)
    {
        if (imported.Any(path => path.EndsWith("arms@crowbar.fbx") || path.EndsWith("Crowbar.controller")))
            EditorApplication.delayCall += CrowbarViewSetup.Bind;
    }
}
#endif
