using UnityEngine;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// The arithmetic behind <c>SC</c>: how far it can scroll, and which way each input moves it.
/// </summary>
/// <remarks>
/// Small, but it is the part that breaks. A sign error here is a container that scrolls
/// backwards, and a clamp error is one that slides its content off into nothing -- both of
/// which look like a rendering fault in game rather than an arithmetic one, and neither of
/// which any other test would notice.
/// </remarks>
internal static class ScrollTests
{
    private static ScrollRegion Region(float view, float content)
    {
        return new ScrollRegion
        {
            Id = "list",
            Rect = Rect.MinMaxRect(0f, 0f, 100f, 100f),
            View = view,
            Content = content,
            ToScene = 2f,
        };
    }

    internal static void Clamps(TestRun run)
    {
        var region = Region(view: 100f, content: 260f);

        run.Check("scroll: hidden content", region.Max, 160d, 0.001d, 0, 0);
        run.Check("scroll: clamps at the top", region.Clamp(-50f), 0d, 0.001d, 0, 0);
        run.Check("scroll: clamps at the bottom", region.Clamp(9999f), 160d, 0.001d, 0, 0);
        run.Check("scroll: passes a legal offset", region.Clamp(40f), 40d, 0.001d, 0, 0);
    }

    internal static void ShortContentCannotScroll(TestRun run)
    {
        // Content that fits is the common case for a list that has not filled up yet, and it
        // must not drift: an offset of anything but zero would slide rows out of the box.
        var region = Region(view: 100f, content: 60f);

        run.Check("scroll: short content has no room", region.Max, 0d, 0.001d, 0, 0);
        run.Check("scroll: short content stays at zero", region.Clamp(30f), 0d, 0.001d, 0, 0);
    }

    internal static void StepSizes(TestRun run)
    {
        var region = Region(view: 100f, content: 400f);

        // A fifth of the viewport per notch, so five notches cross one screenful.
        run.Check("scroll: wheel step is a fifth of the view", region.WheelStep, 20d, 0.001d, 0, 0);

        // Wheel up (positive scrollDelta.y) shows earlier content, which is a smaller offset.
        var afterWheelUp = region.Clamp(100f + -1f * region.WheelStep);
        run.Check("scroll: wheel up moves toward the top", afterWheelUp, 80d, 0.001d, 0, 0);

        // Dragging up in canvas space (positive y) reveals later content, a larger offset.
        var afterDragUp = region.Clamp(100f + 10f * region.ToScene);
        run.Check("scroll: drag up moves toward the bottom", afterDragUp, 120d, 0.001d, 0, 0);
    }
}
