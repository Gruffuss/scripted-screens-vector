# Building `vectorshaders.bundle`

`VectorDither.shader` is Unity's `UI/Default` with a per-pixel dither added. Unity cannot compile
shader source at runtime, so it ships as an AssetBundle beside the DLL.

The mod build copies `shader/build/*.bundle` into the deployed mod folder. It does not build it:
that needs the Unity editor.

## Steps

1. Open a Unity **2022.3** project. Any 2022.3 patch works -- the engine version stamped in a
   bundle only has to be the same major.minor as the player, and mods in the wild ship bundles
   built with 2022.3.7f1 that a 2022.3.62f3 game loads. Unity 6 does **not** work: it writes a
   newer serialized-file version, and that is exactly the case where `LoadFromFile` returns null
   with no explanation.
2. Copy `VectorDither.shader` into `Assets/`.
3. Copy `BuildVectorShaderBundle.cs` into `Assets/Editor/`.
4. Select `VectorDither.shader` in the Project window and check the inspector's **AssetBundle**
   field at the bottom reads `vectorshaders.bundle` — the editor script sets it, but set it by hand if
   the menu item has not been run yet.
5. Menu **ScriptedScreens Vector → Build shader bundle**. It writes
   `<project>/ShaderBundles/vectorshaders.bundle`.
6. Copy that file to `shader/build/vectorshaders.bundle` here, then build the mod as usual.

The `.manifest` file Unity writes beside the bundle is not needed and is not shipped.

The menu item deletes the output folder and passes `ForceRebuildAssetBundle`, and that is not
belt-and-braces. `BuildAssetBundleOptions.None` is incremental, and the pipeline decides what to
rebuild from the manifests left in the output folder: a dependency orphaned by an earlier build
stays orphaned across every later one, with no error. Edit the shader, rebuild without forcing it,
and you ship the previous variant set -- which looks exactly like the dither not working.

## Building it headless

```
"C:/Program Files/Unity/Hub/Editor/<version>/Editor/Unity.exe" -batchmode -quit -nographics   -projectPath <project> -executeMethod BuildVectorShaderBundle.Build -logFile -
```

A minimal empty project is enough and is the right place for it: a mod project with an exporter
package wired in may assign asset bundle names across all assets and sweep the shader into its
own bundle.

## Verifying it

The engine version is plain text in the first couple of hundred bytes of the bundle, so the
version can be checked without launching anything:

```
head -c 200 vectorshaders.bundle | tr -c '[:print:]' '
' | grep -E '^[0-9]{4}\.[0-9]+\.[0-9]+[fab][0-9]+$'
```

If `LoadFromFile` ever returns null on a later build, a version mismatch is **not** the first
suspect: 2022.3 tolerates patch differences, and a mod in the wild ships a 2022.3.7f1 bundle the
2022.3.62f3 game loads. Matching exactly is belt-and-braces. The likelier cause is a stale
artifact, which is what the forced clean rebuild above exists to prevent -- so check that the
bundle was actually rewritten before doubting the editor version.

`AssetBundle.LoadFromFile` otherwise rejects a bad bundle with no detail beyond returning null, so
the log line is the next check: on a good load the mod logs
`Dither shader loaded; gradients dither at 1/255.` at startup.

Then run `InGameTest-dither.lua`. It has two halves and both matter:

- the glows are the banding the shader exists to remove;
- the **clip check** draws a bar far outside the scene's viewbox. `RectMask2D` on the surface root
  must cut it at the console's edge. If the shader lost `_ClipRect`, `UNITY_UI_CLIP_RECT` or the
  stencil block in a rewrite, that bar escapes the screen and it is obvious.

## Changing the shader

Keep the copy of `UI-Default` verbatim. Only the four blocks marked `VECTOR` are ours. If a future
Unity version changes `UI-Default`, re-copy it and re-apply those four.
