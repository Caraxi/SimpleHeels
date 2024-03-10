using System.Numerics;

namespace SimpleHeels;

public interface IOffsetProvider {
    public Vector3 GetOffset();
    public float GetRotation();

    public void Deconstruct(out float x, out float y, out float z, out float r) {
        var o = GetOffset();
        x = o.X;
        y = o.Y;
        z = o.Z;
        r = GetRotation();
    }
}
