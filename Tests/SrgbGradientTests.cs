using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

internal static class SrgbGradientTests
{
    private static readonly FieldInfo? ChannelField = typeof(MeshBuilder).GetField("_uv2", BindingFlags.Instance | BindingFlags.NonPublic);
    private static float Linear(float c) => c <= 0.04045f ? c / 12.92f : (float)Math.Pow((c + 0.055f) / 1.055f, 2.4);
    private static float Srgb(float c) => c <= 0.0031308f ? c * 12.92f : 1.055f * (float)Math.Pow(c, 1d / 2.4d) - 0.055f;
    private static float Byte(float c) => (float)Math.Round(c * 255f) / 255f;
    private static Color CanvasColour(Color c) => new Color(Byte(Linear(Byte(c.r))), Byte(Linear(Byte(c.g))), Byte(Linear(Byte(c.b))), Byte(c.a));

    // Managed mirror of the VECTOR colour block in shader/VectorDither.shader.
    private static Color Shade(Vector4 stored, Color vertex) => stored.w > 0.5f
        ? new Color(Linear(stored.x), Linear(stored.y), Linear(stored.z), vertex.a) : vertex;

    internal static bool Channel(MeshBuilder mesh, List<ImagePlacement>? images)
    {
        var channel = ChannelField?.GetValue(mesh) as List<Vector4>;
        if (channel == null || channel.Count != mesh.currentVertCount) return false;
        for (var i = 0; i < channel.Count; i++)
        {
            var c = mesh.Colour(i); var uv = channel[i];
            var picture = false;
            if (images != null)
                foreach (var image in images)
                    if (image.ShapeIndex >= 0 && i >= mesh.ShapeStart(image.ShapeIndex) && i < mesh.ShapeStart(image.ShapeIndex + 1)) picture = true;
            if (uv.x != c.r || uv.y != c.g || uv.z != c.b || Math.Abs(uv.w - (picture ? 0f : 1f)) > 1e-6f) return false;
        }
        return true;
    }

    internal static void Run(TestRun run)
    {
        var a = new Color(95f/255f,217f/255f,168f/255f,1f);
        var b = new Color(46f/255f,139f/255f,110f/255f,1f);
        var vertex = Color.Lerp(CanvasColour(a), CanvasColour(b), 0.5f);
        var value = Shade(new Vector4((a.r+b.r)/2f,(a.g+b.g)/2f,(a.b+b.b)/2f,1f), vertex);
        var red = Srgb(value.r) * 255f; var green = Srgb(value.g) * 255f; var blue = Srgb(value.b) * 255f;
        run.Check("sRGB: browser midpoint", Math.Abs(red-70f) <= 1f && Math.Abs(green-178f) <= 1f && Math.Abs(blue-139f) <= 1f,
            $"({red:F5}, {green:F5}, {blue:F5})");
        var native = CanvasColour(new Color(0.1234f,0.5f,0.9f,0.3755f));
        run.Check("sRGB: absent channel and pictures keep native colour", Shade(new Vector4(0.9f,0.8f,0.7f,0f), native) == native,
            "presence flag leaves the texture-tinted vertex colour unchanged");
        run.Check("sRGB: native alpha is unchanged", Shade(new Vector4(0.1f,0.2f,0.3f,1f), native).a == native.a,
            "alpha bypasses colour conversion");
        var dark = 5f/255f;
        var darkValue = Shade(new Vector4(dark,dark,dark,1f), CanvasColour(new Color(dark,dark,dark,1f)));
        run.Check("sRGB: dark flat keeps #050505", Math.Abs(Srgb(darkValue.r)-dark) <= 1e-7f, $"{Srgb(darkValue.r)*255f:F6}/255");
        var maxError = 0f;
        for (var i = 0; i < 1025; i++)
        {
            var c = i/1024f;
            var shaded = Shade(new Vector4(c,c,c,1f), CanvasColour(new Color(c,c,c,1f)));
            maxError = Math.Max(maxError, Math.Abs(Srgb(shaded.r)-c));
        }
        run.Check("sRGB: flat round-trip keeps float precision", maxError <= 3e-7f, $"maximum error {maxError:R}");
        MeshBuilder Fade(string end)
        {
            var scene = SceneParser.Parse(SceneText.ToProps("SCENE w=100 h=100 fit=stretch\nDEFS{GL id=g units=bbox x1=0 y1=0 x2=1 y2=0 stops=[[0,#5FD9A8],[1,"+end+"]] }\nR x=0 y=0 w=100 h=100 f=@g fea=0", "sRGB fade")!)!;
            var result = new MeshBuilder();
            Tessellator.Emit(result, scene, new EvalContext(), new Rect(0f,0f,100f,100f), 1f,true,new TessellationStats());
            return result;
        }
        var blackFade = Fade("#00000000"); var hueFade = Fade("#5FD9A800");
        run.Check("sRGB: transparent black and same-hue stops still match", Channel(blackFade,null) && Channel(hueFade,null)
            && FingerprintTests.MeshChecksum(blackFade,true) == FingerprintTests.MeshChecksum(hueFade,true), "premultiplied stop sampling and stored RGB remain identical");
        var mesh = new MeshBuilder();
        void Rebuild()
        {
            mesh.Clear();
            for (var i=0; i<128; i++) mesh.AddVert(new Vector3(i,0f), new Color(0.01001f,0.001f,0.5f,0.1255f), Vector2.zero);
        }
        for (var i=0; i<100; i++) Rebuild();
        var before = GC.GetAllocatedBytesForCurrentThread();
        for (var i=0; i<1000; i++) Rebuild();
        var allocated = GC.GetAllocatedBytesForCurrentThread()-before;
        run.Check("sRGB: retained channel allocates zero on rebuild", Channel(mesh,null) && allocated == 0, $"{allocated} bytes / 1000 rebuilds");
        var gate = FingerprintTests.FindGateFolder(AppContext.BaseDirectory);
        var root = Directory.GetParent(Directory.GetParent(gate)!.FullName)!.FullName;
        var graphic = File.ReadAllText(Path.Combine(root,"VectorGraphic.cs"));
        run.Check("sRGB: live and capture request colour channel", graphic.Contains("AdditionalCanvasShaderChannels.TexCoord2")
            && graphic.Contains("EnsureCoverageChannel();"), "same UpdateGeometry path, including capture clones");
        var builder = File.ReadAllText(Path.Combine(root,"MeshBuilder.cs"));
        run.Check("sRGB: full and sliced meshes upload colour channel", builder.Contains("mesh.SetUVs(2, _uv2);")
            && builder.Contains("mesh.SetUVs(2, _uv2, from, to - from);"), "float4 channel beside existing UVs");
    }
}
