using System;
using System.IO;
using System.Reflection;
using UnityEngine;

namespace ScriptedScreensVector.Tests;

internal static class GateTests
{
    internal static void Run(TestRun run)
    {
        // Expected bytes come from the measured table in REFERENCE, independently of the parser.
        foreach (var pair in new[] {
            ("red", "FF0000"), ("cyan", "00FFFF"), ("blue", "0000FF"), ("darkblue", "00008B"),
            ("lightblue", "ADD8E6"), ("purple", "800080"), ("yellow", "FFFF00"), ("lime", "00FF00"),
            ("fuchsia", "FF00FF"), ("white", "FFFFFF"), ("silver", "C0C0C0"), ("grey", "808080"),
            ("black", "000000"), ("orange", "FFA500"), ("brown", "A52A2A"), ("maroon", "800000"),
            ("green", "008000"), ("olive", "808000"), ("navy", "000080"), ("teal", "008080"),
            ("aqua", "00FFFF"), ("magenta", "FF00FF"), ("transparent", "00000000") })
        {
            var expected = Bytes(pair.Item2);
            foreach (var name in new[] { pair.Item1, pair.Item1.ToUpperInvariant() })
                run.Check($"managed colour: {name}", HexColor.TryParse(name, out var actual) && actual == expected,
                    pair.Item2);
        }
        foreach (var pair in new[] { ("#aBc", "AABBCC"), ("#aBcD", "AABBCCDD"),
            ("#ABCDEF", "ABCDEF"), ("#abcdef", "ABCDEF"), ("#aBcDeF12", "ABCDEF12"), ("#00000000", "00000000") })
            run.Check($"managed colour: {pair.Item1}",
                HexColor.TryParse(pair.Item1, out var actual) && actual == Bytes(pair.Item2), pair.Item2);
        foreach (var bad in new string?[] { null, "", "gray", "FFFFFF", "#FFFFF", "#GGGGGG", "# ff00f",
            "#fff ", " red", "red ", " #fff", "rgba(1,2,3,1)", "hsl(0,0%,0%)", "none" })
            run.Check($"managed colour rejects: {bad ?? "<null>"}", !HexColor.TryParse(bad, out _), "raw Unity rules");
        run.Check("colour wrapper normalizes gray and whitespace",
            Colours.TryParse(" GRAY ", out var grey) && grey == Bytes("808080"), "wrapper remains separate");
        run.Check("colour hook has no externally mutable field",
            typeof(Colours).GetField("Parser", BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic) == null
            && typeof(Colours).GetProperty("Parser", BindingFlags.Static | BindingFlags.NonPublic)?.GetSetMethod(true)?.IsPrivate == true,
            "installer is compiled only in the suite");

        var root = Directory.GetCurrentDirectory();
        if (Path.GetFileName(root) == "Tests")
            root = Directory.GetParent(root)!.FullName;
        var folder = Path.Combine(root, "Tests", "gate");
        foreach (var start in new[] { root, Path.Combine(folder, "scenes") })
            run.Check("gate discovery from " + Path.GetRelativePath(root, start),
                FingerprintTests.FindGateFolder(start) == folder, "walks ancestors to Tests/gate");

        var handson = File.ReadAllText(Path.Combine(folder, "scenes", "InGameTest-handson.txt"));
        run.Check("handson concrete square-console scene",
            handson.StartsWith("SCENE w=224 h=224 fit=stretch\n", StringComparison.Ordinal)
            && handson.Contains("G t=[12,0] {", StringComparison.Ordinal)
            && handson.Contains("src=\"data:image/gif;base64,", StringComparison.Ordinal)
            && handson.Contains("src=\"data:image/bmp;base64,", StringComparison.Ordinal), "Lua defaults: W=H=460");
        var deep = File.ReadAllText(Path.Combine(folder, "scenes", "InGameTest-0.11.84.txt"));
        run.Check("deep fixture expands both 400-level expressions",
            deep.Contains("w=\"=" + string.Concat(System.Linq.Enumerable.Repeat("abs(", 400)) + "1" + new string(')', 400) + "\"", StringComparison.Ordinal)
            && deep.Contains("w=\"=" + string.Concat(System.Linq.Enumerable.Repeat("$a[", 400)) + "0" + new string(']', 400) + "\"", StringComparison.Ordinal),
            "real generated expression input");

        FingerprintTests.PrepareImages(Path.Combine(folder, "scenes"));
        var picture = SceneParser.Parse(SceneText.ToProps(File.ReadAllText(Path.Combine(folder, "scenes", "17-pictures.txt")), "pictures")!)!;
        var pictureMesh = new MeshBuilder();
        Tessellator.Emit(pictureMesh, picture, new EvalContext(), new Rect(0f, 0f, 400f, 400f), 2f, true, new TessellationStats());
        run.Check("picture fixture draws loaded geometry", pictureMesh.currentVertCount > 16,
            $"{pictureMesh.currentVertCount} vertices; background alone has 16");

        var original = FingerprintTests.MeshChecksum(Mesh());
        run.Check("mesh checksum sees interior positions", original != FingerprintTests.MeshChecksum(Mesh(center: 0.6f)), "counts and bounds unchanged");
        run.Check("mesh checksum sees colours", original != FingerprintTests.MeshChecksum(Mesh(colour: Color.red)), "same geometry");
        run.Check("mesh checksum sees alpha", original != FingerprintTests.MeshChecksum(Mesh(colour: new Color(1f, 1f, 1f, 0.5f))), "same geometry");
        run.Check("mesh checksum sees texture UVs", original != FingerprintTests.MeshChecksum(Mesh(uv: new Vector2(0.1f, 0.2f))), "same geometry");
        run.Check("mesh checksum sees coverage UVs", original != FingerprintTests.MeshChecksum(Mesh(coverage: 0.5f)), "same geometry");
        run.Check("mesh checksum sees triangle indices", original != FingerprintTests.MeshChecksum(Mesh(reversed: true)), "same vertices");
    }

    private static Color Bytes(string hex)
    {
        var rgba = Convert.ToUInt32(hex.Length == 6 ? hex + "FF" : hex, 16);
        return new Color((rgba >> 24) / 255f, ((rgba >> 16) & 255) / 255f, ((rgba >> 8) & 255) / 255f, (rgba & 255) / 255f);
    }

    private static MeshBuilder Mesh(float center = 0.5f, Color? colour = null, Vector2? uv = null,
        float coverage = 1f, bool reversed = false)
    {
        var mesh = new MeshBuilder();
        foreach (var point in new[] { Vector3.zero, new Vector3(1f, 0f), new Vector3(1f, 1f), new Vector3(0f, 1f), new Vector3(center, center) })
            mesh.AddVert(point, colour ?? Color.white, uv ?? Vector2.zero, coverage);
        mesh.AddTriangle(0, reversed ? 4 : 1, reversed ? 1 : 4);
        mesh.AddTriangle(1, 2, 4);
        mesh.AddTriangle(2, 3, 4);
        mesh.AddTriangle(3, 0, 4);
        return mesh;
    }
}
