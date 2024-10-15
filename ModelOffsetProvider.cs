using System.Numerics;

namespace SimpleHeels;

public record ModelOffsetProvider(float Offset) : IOffsetProvider {
    public Vector3 GetOffset() {
        return new Vector3(0, Offset, 0);
    }

    public float GetRotation() {
        return 0f;
    }

    public PitchRoll GetPitchRoll() => PitchRoll.Zero;
}
