namespace ScriptedScreensVector;

/// <summary>
/// Which screen scale the tessellator is handed when the projection cannot measure one.
/// </summary>
/// <remarks>
/// Its own file, and free of Unity, so the SUITE can own it. Two different things were tangled
/// together at the call site:
///
/// * *Does the projection resolve a scale?* — needs a real camera crossing a near plane, so it
///   cannot be tested headless. That half rests on in-game measurement.
/// * *What do we hand the job when it does not?* — pure logic over three inputs, and **that is
///   where the bug was**.
///
/// The bug: this used to be `known ? measured : 1f`. A scale of `1` means one screen pixel per
/// canvas unit, which is what a console viewed from enormously far away measures. Everything
/// derived in screen pixels is computed from it, so `Tessellator.FeatherWidth` returned 1.3
/// CANVAS UNITS of auto feather instead of about 0.02 at a real 60 px/unit — some sixty times
/// too wide, on every shape at once. Reported from a console as "ALL vectors became blurry when
/// too close", with TextMeshPro labels sharp beside them because text never consults this scale.
///
/// Declining to measure is correct: walk close enough and a corner of the surface crosses the
/// camera's near plane, where the projection is meaningless (measured in game: corners at
/// z 0.011–0.022 against a 0.010 near plane). Substituting a number wrong by orders of magnitude
/// is not. The surface was being drawn a frame ago, so the scale from then is at worst slightly
/// stale, where `1` is categorically wrong.
/// </remarks>
internal static class ScaleChoice
{
    /// <summary>
    /// The scale to tessellate at: the measured one, else the last that resolved, else 1.
    /// </summary>
    /// <param name="known">Whether <paramref name="measured"/> came back usable.</param>
    /// <param name="measured">The projection's answer this frame.</param>
    /// <param name="lastKnown">The last scale that resolved, or a non-positive value if none ever has.</param>
    internal static float Choose(bool known, float measured, float lastKnown)
    {
        if (known)
            return measured;

        // `1` is the first-ever case ONLY: nothing has ever resolved, so there is nothing better
        // to carry forward. Once anything has, reusing it always beats inventing a number.
        return lastKnown > 0f ? lastKnown : 1f;
    }
}
