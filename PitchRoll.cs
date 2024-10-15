using System;

namespace SimpleHeels;

public record PitchRoll(float Pitch, float Roll) {
    public static readonly PitchRoll Zero = new(0, 0);

    public static bool Equal(PitchRoll? a, PitchRoll? b) {
        if (a == null && b == null) return true;
        if (a == null || b == null) return false;
        return MathF.Abs(a.Pitch - b.Pitch) < Constants.FloatDelta && MathF.Abs(a.Roll - b.Roll) < Constants.FloatDelta;
    }
}
