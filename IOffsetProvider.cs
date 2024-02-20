using System.Numerics;

namespace SimpleHeels;

public interface IOffsetProvider {
    public Vector3 GetOffset();
    public float GetRotation();
}
