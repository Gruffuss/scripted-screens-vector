using System;

namespace ScriptedScreensVector;

internal struct DataCadence
{
    internal const float MinInterval = .05f;
    private const float MaxInterval = 1.5f;
    private float _lastPayloadAt;

    internal float Arrive(float now)
    {
        var interval = Math.Clamp(now - _lastPayloadAt, MinInterval, MaxInterval);
        _lastPayloadAt = now;
        return interval;
    }
}
