using System.IO;
using UnityEditor;
using UnityEngine;
namespace StationGreybox.Editor
{
    public static class StationGreyboxBuildRequest
    {
        public static void ValidateSaved()
        {
            UnityEditor.SceneManagement.EditorSceneManager.OpenScene("Assets/LevelDesign/Station/Greybox/StationGreybox.unity");
            StationGreyboxGenerator.ValidateRoutes();
        }
        public static void Run()
        {
            const string request="Library/StationGreyboxBuild.request";
            if(EditorApplication.isPlayingOrWillChangePlaymode)return;
            if(File.Exists(request)) File.Delete(request);
            try { StationGreyboxGenerator.CreateTestScene(); StationGreyboxGenerator.ValidateRoutes(); File.WriteAllText("Library/StationGreyboxBuild.result","SUCCESS"); }
            catch(System.Exception e) { File.WriteAllText("Library/StationGreyboxBuild.result",e.ToString()); throw; }
        }
    }
}
