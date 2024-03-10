using System.Numerics;

namespace SimpleHeels;

public class TempOffset(float x, float y, float z, float r) : IOffsetProvider {
    public float X = x;
    public float Y = y;
    public float Z = z;
    public float R = r;

    public Vector3 GetOffset() => new(X, Y, Z);

    public float GetRotation() => R;

    public TempOffset Clone() => new(X, Y, Z, R);
}
