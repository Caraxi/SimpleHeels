using System.Numerics;

namespace SimpleHeels; 

public record TempOffset(float X, float Y, float Z, float R) : IOffsetProvider {
    public Vector3 GetOffset() => new(X, Y, Z);

    public float GetRotation() => R;
}
