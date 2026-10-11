using System;
using System.Linq;

namespace ScriptedScreensVector;

/// <summary>Calls the optional fonts mod. Requests and callbacks stay on the Unity main thread.</summary>
internal static class PageFontBridge
{
    internal static readonly PageFonts Requests = new();

    internal static void Request(string link)
    {
        if (!Requests.Begin(link))
            return;

        try
        {
            var api = AppDomain.CurrentDomain.GetAssemblies()
                .Where(a => a.GetName().Name == "ScriptedScreensFonts")
                .Select(a => a.GetType("ScriptedScreensFonts.FontApi"))
                .FirstOrDefault(t => t != null);

            if (api == null)
            {
                Requests.Refuse(link, "ScriptedScreens Fonts mod not installed");
                return;
            }

            var method = api.GetMethod("RequestFontWithReason",
                new[] { typeof(string), typeof(Action<string[], string>) });
            if (method == null)
            {
                Requests.Refuse(link, "fonts mod has no compatible RequestFontWithReason API");
                return;
            }

            var done = new Action<string[], string>((faces, reason) =>
            {
                Requests.Complete(link, faces, reason);
                VectorGraphic.PageFontChanged(link);
            });

            if (method.Invoke(null, new object[] { link, done }) is not true)
                Requests.Refuse(link, "fonts mod refused the link (invalid URL, host not allowed, or loader not running)");
        }
        catch (Exception ex)
        {
            Requests.Refuse(link, $"font API failed: {ex.GetBaseException().Message}");
        }
    }
}
