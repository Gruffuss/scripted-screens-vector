using UnityEngine;

namespace ScriptedScreensVector.Tests;

/// <summary>
/// Per-name glide timing: the curves themselves, and what a payload's `ease` does to a value.
/// </summary>
/// <remarks>
/// The curve numbers are CSS's, checked against the definitions rather than against this
/// implementation — `ease-out` is `cubic-bezier(0, 0, 0.58, 1)` and both must agree, which is
/// the one comparison that cannot pass by accident.
/// </remarks>
internal static class EasingTests
{
    internal static void Curves(TestRun run)
    {
        var linear = Easing.Parse("linear");
        run.Check("ease: linear is the identity",
            Mathf.Abs(linear.Evaluate(0.25f) - 0.25f) < 0.001f && Mathf.Abs(linear.Evaluate(0.8f) - 0.8f) < 0.001f,
            $"{linear.Evaluate(0.25f)}, {linear.Evaluate(0.8f)}");

        // Every curve is pinned at both ends, whatever it does in between.
        foreach (var name in new[] { "linear", "ease", "ease-in", "ease-out", "ease-in-out", "steps(4)" })
        {
            var curve = Easing.Parse(name);
            run.Check($"ease: {name} starts at 0 and ends at 1",
                Mathf.Abs(curve.Evaluate(0f)) < 0.001f && Mathf.Abs(curve.Evaluate(1f) - 1f) < 0.001f,
                $"{curve.Evaluate(0f)} .. {curve.Evaluate(1f)}");
        }

        // ease-out leads, ease-in trails. Halfway is where the difference is plainest.
        var easeOut = Easing.Parse("ease-out");
        var easeIn = Easing.Parse("ease-in");
        run.Check("ease: ease-out is ahead and ease-in behind at the midpoint",
            easeOut.Evaluate(0.5f) > 0.6f && easeIn.Evaluate(0.5f) < 0.4f,
            $"out {easeOut.Evaluate(0.5f):F3}, in {easeIn.Evaluate(0.5f):F3}");

        // The named curves ARE cubic-beziers; if these disagree, one of the two is wrong.
        var spelledOut = Easing.Parse("cubic-bezier(0, 0, 0.58, 1)");
        var worst = 0f;
        for (var i = 0; i <= 20; i++)
        {
            var p = i / 20f;
            worst = Mathf.Max(worst, Mathf.Abs(spelledOut.Evaluate(p) - easeOut.Evaluate(p)));
        }

        run.Check("ease: ease-out equals its cubic-bezier spelling", worst < 0.002f, $"worst {worst:F4}");

        // A known point of CSS `ease`, which is cubic-bezier(0.25, 0.1, 0.25, 1).
        var css = Easing.Parse("ease");
        run.Check("ease: `ease` is ahead of linear early on",
            css.Evaluate(0.25f) > 0.35f && css.Evaluate(0.25f) < 0.55f, $"{css.Evaluate(0.25f):F3}");

        // steps(4), CSS's default jump-end: nothing moves until the first boundary.
        var steps = Easing.Parse("steps(4)");
        run.Check("ease: steps(4) holds then jumps",
            Mathf.Abs(steps.Evaluate(0.2f)) < 0.001f
            && Mathf.Abs(steps.Evaluate(0.3f) - 0.25f) < 0.001f
            && Mathf.Abs(steps.Evaluate(0.6f) - 0.5f) < 0.001f,
            $"{steps.Evaluate(0.2f)}, {steps.Evaluate(0.3f)}, {steps.Evaluate(0.6f)}");

        // Monotonic: a glide that goes backwards reads as a glitch, not as a curve.
        var previous = -1f;
        var climbs = true;
        for (var i = 0; i <= 50; i++)
        {
            var at = css.Evaluate(i / 50f);
            if (at < previous - 0.0001f)
                climbs = false;

            previous = at;
        }

        run.Check("ease: a curve never goes backwards", climbs, "");

        // A typo glides rather than jumping, and says so.
        var reported = string.Empty;
        var unknown = Easing.Parse("ease-sideways", message => reported = message);
        run.Check("ease: an unknown curve falls back to linear and reports",
            unknown.IsLinear && reported.Contains("ease-sideways", System.StringComparison.Ordinal), reported);
    }

    internal static void Payload(TestRun run)
    {
        // A name with its own glide reads from its own fraction, not the scene's.
        var context = new EvalContext { Blend = 0.5f };
        context.Scalars["bar"] = 100f;
        context.Previous["bar"] = 0f;
        context.Scalars["other"] = 100f;
        context.Previous["other"] = 0f;
        context.NameBlend["bar"] = 0.25f;

        run.Check("ease: a timed name uses its own blend",
            Mathf.Abs(context.Scalar("bar") - 25f) < 0.001f && Mathf.Abs(context.Scalar("other") - 50f) < 0.001f,
            $"bar {context.Scalar("bar")}, other {context.Scalar("other")}");

        // Rebasing mid-glide keeps what each name is SHOWING, not what the scene average shows.
        context.Rebase(0.5f);
        run.Check("ease: rebase holds each name where it is",
            Mathf.Abs(context.Previous["bar"] - 25f) < 0.001f && Mathf.Abs(context.Previous["other"] - 50f) < 0.001f,
            $"bar {context.Previous["bar"]}, other {context.Previous["other"]}");

        // An array element follows its name's timing too.
        var arrays = new EvalContext { Blend = 1f };
        arrays.Arrays["h"] = new[] { 10f, 20f };
        arrays.PreviousArrays["h"] = new[] { 0f, 0f };
        arrays.NameBlend["h"] = 0.5f;
        run.Check("ease: an array element uses its name's blend",
            Mathf.Abs(arrays.Element("h", 1) - 10f) < 0.001f, $"{arrays.Element("h", 1)}");

        // A later payload restating the name without timing gives it back the scene's blend.
        var merged = Payload(("bar", 40f), keep: true, seconds: 0.6f);
        merged.MergeFrom(Payload(("bar", 80f), keep: true, seconds: 0f));
        run.Check("ease: a restatement without timing clears the old timing",
            !merged.Eased.ContainsKey("bar") && merged.Scalars["bar"] == 80f,
            $"{merged.Eased.Count} timings, bar {merged.Scalars["bar"]}");

        // ...and one that restates it WITH timing keeps the latest.
        var retimed = Payload(("bar", 40f), keep: true, seconds: 0.6f);
        retimed.MergeFrom(Payload(("bar", 80f), keep: true, seconds: 0.2f));
        run.Check("ease: a restatement with timing wins",
            retimed.Eased.TryGetValue("bar", out var timing) && Mathf.Abs(timing.Seconds - 0.2f) < 0.001f,
            $"{(retimed.Eased.TryGetValue("bar", out var t) ? t.Seconds : -1f)}");
    }

    /// <summary>A payload of one value, optionally with a stated glide.</summary>
    private static EvalContext Payload((string Name, float Value) value, bool keep, float seconds)
    {
        var context = new EvalContext { KeepUnmentioned = keep };
        context.Scalars[value.Name] = value.Value;

        if (seconds > 0f)
            context.Eased[value.Name] = (seconds, Easing.Parse("ease-out"), 0f);

        return context;
    }
}
