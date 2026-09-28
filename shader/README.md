# Building `vectorshaders.bundle`

`VectorDither.shader` is Unity's `UI/Default` with a per-pixel dither added. Unity cannot compile
shader source at runtime, so it ships as an AssetBundle beside the DLL.

The mod build copies `shader/build/*.bundle` into the deployed mod folder. It does not build it:
that needs the Unity editor.

## Steps

1. Open a Unity 2022.3 project (any of them; the bundle only has to match the game's Unity version
   and the StandaloneWindows64 target).
2. Copy `VectorDither.shader` into `Assets/`.
3. Copy `BuildVectorShaderBundle.cs` into `Assets/Editor/`.
4. Select `VectorDither.shader` in the Project window and check the inspector's **AssetBundle**
   field at the bottom reads `vectorshaders.bundle` — the editor script sets it, but set it by hand if
   the menu item has not been run yet.
5. Menu **ScriptedScreens Vector → Build shader bundle**. It writes
   `<project>/ShaderBundles/vectorshaders.bundle`.
6. Copy that file to `shader/build/vectorshaders.bundle` here, then build the mod as usual.

The `.manifest` file Unity writes beside the bundle is not needed and is not shipped.

## Verifying it

`AssetBundle.LoadFromFile` rejects a bundle from a different Unity version or platform with no
detail beyond returning null, so the log line is the check: on a good load the mod logs
`Dither shader loaded; gradients dither at 1/255.` at startup.

Then run `InGameTest-dither.lua`. It has two halves and both matter:

- the glows are the banding the shader exists to remove;
- the **clip check** draws a bar far outside the scene's viewbox. `RectMask2D` on the surface root
  must cut it at the console's edge. If the shader lost `_ClipRect`, `UNITY_UI_CLIP_RECT` or the
  stencil block in a rewrite, that bar escapes the screen and it is obvious.

## Changing the shader

Keep the copy of `UI-Default` verbatim. Only the four blocks marked `VECTOR` are ours. If a future
Unity version changes `UI-Default`, re-copy it and re-apply those four.
