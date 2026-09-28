// Drop this into Assets/Editor/ of a Unity 2022.3 project that also holds VectorDither.shader.
// Menu: ScriptedScreens Vector -> Build shader bundle.
#if UNITY_EDITOR
using System.IO;
using UnityEditor;
using UnityEngine;

internal static class BuildVectorShaderBundle
{
    private const string BundleName = "vectorshaders.bundle";
    private const string ShaderFile = "VectorDither.shader";

    [MenuItem("ScriptedScreens Vector/Build shader bundle")]
    private static void Build()
    {
        var path = FindShader();
        if (path == null)
        {
            Debug.LogError($"{ShaderFile} is not in this project; copy it in first.");
            return;
        }

        // The bundle assignment lives in the .meta file, so set it rather than expecting the
        // user to have clicked the inspector field.
        var importer = AssetImporter.GetAtPath(path);
        if (importer != null && importer.assetBundleName != BundleName)
        {
            importer.assetBundleName = BundleName;
            importer.SaveAndReimport();
        }

        var output = Path.Combine(Path.GetDirectoryName(Application.dataPath), "ShaderBundles");
        Directory.CreateDirectory(output);

        BuildPipeline.BuildAssetBundles(
            output,
            BuildAssetBundleOptions.None,
            BuildTarget.StandaloneWindows64);

        Debug.Log($"Built {BundleName} in {output}. Copy it to the mod's shader/build/ folder.");
    }

    private static string FindShader()
    {
        foreach (var guid in AssetDatabase.FindAssets("t:Shader"))
        {
            var path = AssetDatabase.GUIDToAssetPath(guid);
            if (Path.GetFileName(path) == ShaderFile)
                return path;
        }

        return null;
    }
}
#endif
