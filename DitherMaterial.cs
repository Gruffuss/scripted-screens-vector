using System.IO;
using UnityEngine;

namespace ScriptedScreensVector;

/// <summary>
/// The shared material every vector mesh is drawn with: Unity's UI/Default plus a per-pixel
/// dither on the output alpha. See <c>shader/VectorDither.shader</c> for why it has to be a
/// fragment shader and why the dither is on alpha.
/// </summary>
/// <remarks>
/// One material instance for the whole mod, as UI/Default is, so consoles still batch with each
/// other. Clipping needs nothing from us: a <c>RectMask2D</c> clips through
/// <c>CanvasRenderer.EnableRectClipping</c>, which feeds <c>_ClipRect</c> and switches the
/// <c>UNITY_UI_CLIP_RECT</c> variant on per renderer -- the shader only has to declare both,
/// which is exactly why it is a verbatim copy of UI-Default with four lines added.
/// </remarks>
internal static class DitherMaterial
{
    private const string BundleName = "vectorshaders.bundle";
    private const string ShaderName = "ScriptedScreensVector/UIDither";

    private static readonly int DitherId = Shader.PropertyToID("_VectorDither");

    private static bool _tried;
    private static Material? _material;

    /// <summary>Whether the shader is loaded, and so whether coverage may ride in a UV.</summary>
    internal static bool HasShader
    {
        get
        {
            Load();
            return _material != null;
        }
    }

    /// <summary>The material to draw with, or null to use UGUI's default.</summary>
    internal static Material? Shared
    {
        get
        {
            Load();
            return _material;
        }
    }

    /// <summary>
    /// Pushes the configured amplitude onto the material. Cheap enough to call whenever a
    /// graphic rebuilds, which is what lets the value be tuned in game without a restart.
    /// </summary>
    internal static void Refresh()
    {
        if (Shared != null)
            _material!.SetFloat(DitherId, VectorConfig.Dither);
    }

    private static void Load()
    {
        if (_tried)
            return;

        _tried = true;

        try
        {
            var here = Path.GetDirectoryName(typeof(DitherMaterial).Assembly.Location);
            if (string.IsNullOrEmpty(here))
            {
                ScriptedScreensVectorPlugin.Log?.LogError(
                    "Cannot locate the mod folder, so " + BundleName + " cannot be loaded; "
                    + "vector gradients will band.");
                return;
            }

            var path = Path.Combine(here, BundleName);
            if (!File.Exists(path))
            {
                ScriptedScreensVectorPlugin.Log?.LogError(
                    $"{BundleName} is missing from {here}; vector gradients will band. It ships "
                    + "with the mod, so an install that lost it is an incomplete install.");
                return;
            }

            var bundle = AssetBundle.LoadFromFile(path);
            if (bundle == null)
            {
                ScriptedScreensVectorPlugin.Log?.LogError(
                    $"{BundleName} would not load. It is built for StandaloneWindows64 on the "
                    + "game's Unity version; a bundle from another version or platform is "
                    + "rejected without further detail.");
                return;
            }

            Shader? shader = null;
            foreach (var candidate in bundle.LoadAllAssets<Shader>())
            {
                if (candidate != null && candidate.name == ShaderName)
                {
                    shader = candidate;
                    break;
                }
            }

            if (shader == null)
            {
                ScriptedScreensVectorPlugin.Log?.LogError(
                    $"{BundleName} holds no shader named \"{ShaderName}\"; vector gradients will band.");
                return;
            }

            if (!shader.isSupported)
            {
                ScriptedScreensVectorPlugin.Log?.LogError(
                    $"\"{ShaderName}\" is not supported on this graphics device; vector gradients will band.");
                return;
            }

            _material = new Material(shader) { hideFlags = HideFlags.HideAndDontSave };
            _material.SetFloat(DitherId, VectorConfig.Dither);
            ScriptedScreensVectorPlugin.Log?.LogInfo(
                $"Dither shader loaded; gradients dither at {VectorConfig.Dither:0.##}/255.");

            // How much precision there actually is between our mesh and the screen. Guessed at
            // twice and wrong both times, so it is recorded rather than assumed: in linear space
            // the right dither amplitude is not the same as in gamma, and with an HDR target
            // there is no 8-bit step to dither against at all until the final present.
            var camera = Camera.main;
            ScriptedScreensVectorPlugin.Log?.LogInfo(
                $"render target: colour space {QualitySettings.activeColorSpace}, "
                + (camera != null
                    ? $"main camera HDR {camera.allowHDR}, MSAA {camera.allowMSAA}, target "
                      + (camera.targetTexture != null ? camera.targetTexture.format.ToString() : "the backbuffer")
                    : "no main camera yet"));
        }
        catch (System.Exception ex)
        {
            ScriptedScreensVectorPlugin.Log?.LogError($"Dither shader failed to load: {ex}");
        }
    }
}
