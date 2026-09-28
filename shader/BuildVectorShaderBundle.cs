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
            if (Application.isBatchMode)
                EditorApplication.Exit(1);

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

        // BuildAssetBundleOptions.None is INCREMENTAL, and BuildPipeline decides what to rebuild
        // from the manifests left in the output folder. A dependency orphaned by an earlier build
        // stays orphaned silently for ever -- you edit the shader, rebuild, and ship the previous
        // variant set with no error, which looks exactly like "the dither did not work". Wipe the
        // folder and force the rebuild; this bundle is one asset, so it costs nothing.
        if (Directory.Exists(output))
            Directory.Delete(output, recursive: true);

        Directory.CreateDirectory(output);

        BuildPipeline.BuildAssetBundles(
            output,
            BuildAssetBundleOptions.ForceRebuildAssetBundle,
            BuildTarget.StandaloneWindows64);

        // BuildAssetBundles does not throw and the editor exits 0 even when nothing was written,
        // so the only honest check is the file.
        var built = Path.Combine(output, BundleName);
        if (!File.Exists(built))
        {
            Debug.LogError($"Build produced no {BundleName} in {output}.");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);

            return;
        }

        Debug.Log($"Built {BundleName} ({new FileInfo(built).Length} bytes) in {output}. "
                  + "Copy it to the mod's shader/build/ folder.");
    }

    /// Loads the bundle back and reports what is actually in it. Worth its own menu item: if
    /// UNITY_UI_CLIP_RECT were stripped, nothing would fail until a console started drawing
    /// scenes outside its own screen in game.
    [MenuItem("ScriptedScreens Vector/Verify shader bundle")]
    private static void Verify()
    {
        var built = Path.Combine(
            Path.Combine(Path.GetDirectoryName(Application.dataPath), "ShaderBundles"), BundleName);

        if (!File.Exists(built))
        {
            Debug.LogError($"No {BundleName} to verify; build it first.");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);

            return;
        }

        var bundle = AssetBundle.LoadFromFile(built);
        if (bundle == null)
        {
            Debug.LogError($"{BundleName} would not load in this editor either.");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);

            return;
        }

        var ok = false;
        foreach (var shader in bundle.LoadAllAssets<Shader>())
        {
            // Shader.keywordSpace is the runtime view of the variants actually compiled into
            // this shader, which is exactly the question: did the multi_compile survive.
            var keywords = string.Join(", ", shader.keywordSpace.keywordNames);
            Debug.Log($"VERIFY shader \"{shader.name}\" supported={shader.isSupported} "
                      + $"passes={shader.passCount} properties={shader.GetPropertyCount()}");
            Debug.Log($"VERIFY keywords: {keywords}");

            var clip = keywords.Contains("UNITY_UI_CLIP_RECT");
            var dither = shader.FindPropertyIndex("_VectorDither") >= 0;
            var clipRect = shader.FindPropertyIndex("_ClipRect") >= 0;

            Debug.Log($"VERIFY UNITY_UI_CLIP_RECT={clip} _VectorDither={dither}");
            ok = shader.isSupported && clip && dither;

            // _ClipRect is set per renderer rather than declared as a Property, so its absence
            // from the property list is expected and is not a failure.
            Debug.Log($"VERIFY _ClipRect declared as a property: {clipRect} (expected False)");
        }

        bundle.Unload(unloadAllLoadedObjects: true);

        if (!ok)
        {
            Debug.LogError("VERIFY FAILED: the bundle is missing the shader, the clip variant or the dither property.");
            if (Application.isBatchMode)
                EditorApplication.Exit(1);
        }
        else
        {
            Debug.Log("VERIFY OK");
        }
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
