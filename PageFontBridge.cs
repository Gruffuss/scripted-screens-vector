using System;
using System.Diagnostics;
using System.Linq;
using System.Reflection;

namespace ScriptedScreensVector;

/// <summary>Calls the optional fonts mod. Requests and callbacks stay on the Unity main thread.</summary>
internal static class PageFontBridge
{
    internal static readonly PageFonts Requests = new();
    private static readonly Lazy<(MethodInfo? Method, string Reason)> Api = new(FindApi);
    private static readonly Lazy<Func<int>?> RegistrationCount = new(FindRegistrations);

    /// <summary>
    /// Faces the fonts mod has registered this session by every route -- page requests, its own
    /// folders, downloads, the game's own fonts as their bundles load -- or -1 without it, or with
    /// a version that predates the count. It only ever increases.
    /// </summary>
    /// <remarks>
    /// A delegate rather than <c>PropertyInfo.GetValue</c>, which boxes the int on every read:
    /// a graphic still waiting on a face reads this once a frame.
    /// </remarks>
    internal static int Registrations() => RegistrationCount.Value?.Invoke() ?? -1;

    private static Func<int>? FindRegistrations()
    {
        try
        {
            var getter = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "ScriptedScreensFonts")
                .Select(a => a.GetType("ScriptedScreensFonts.FontApi"))
                .FirstOrDefault(t => t != null)
                ?.GetProperty("Registrations", BindingFlags.Public | BindingFlags.Static)
                ?.GetGetMethod();
            return getter == null || getter.ReturnType != typeof(int)
                ? null
                : (Func<int>)Delegate.CreateDelegate(typeof(Func<int>), getter);
        }
        catch (Exception)
        {
            return null;
        }
    }

    private static double Now() => Stopwatch.GetTimestamp() / (double)Stopwatch.Frequency;

    private static (MethodInfo?, string) FindApi()
    {
        try
        {
            var api = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "ScriptedScreensFonts")
                .Select(a => a.GetType("ScriptedScreensFonts.FontApi"))
                .FirstOrDefault(t => t != null);
            if (api == null)
                return (null, "ScriptedScreens Fonts mod not installed");

            var method = api.GetMethod("RequestFontWithReason",
                new[] { typeof(string), typeof(Action<string[], string>) });
            return (method, "fonts mod has no compatible RequestFontWithReason API");
        }
        catch (Exception ex)
        {
            return (null, $"font API lookup failed: {ex.GetBaseException().Message}");
        }
    }

    internal static void Request(string link)
    {
        if (!Requests.Begin(link, Now()))
            return;

        try
        {
            var (method, reason) = Api.Value;
            if (method == null)
            {
                Requests.Refuse(link, reason, Now());
                return;
            }

            var done = new Action<string[], string>((faces, failure) =>
            {
                Requests.Complete(link, faces, failure, Now());
                VectorGraphic.PageFontChanged(link);
            });

            if (method.Invoke(null, new object[] { link, done }) is not true)
                Requests.Refuse(link, "fonts mod refused the link (invalid URL, host not allowed, or loader not running)", Now());
        }
        catch (Exception ex)
        {
            Requests.Refuse(link, $"font API failed: {ex.GetBaseException().Message}", Now());
        }
    }
}
