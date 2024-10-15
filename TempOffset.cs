

using System;
using System.Numerics;

namespace SimpleHeels;

public class TempOffset(float x = 0, float y = 0, float z = 0, float r = 0, float pitch = 0, float roll = 0) : IOffsetProvider {
    public float X = x;
    public float Y = y;
    public float Z = z;
    public float R = r;
    public float Pitch = pitch;
    public float Roll = roll;

    public System.Numerics.Vector3 GetOffset() => new(X, Y, Z);

    public float GetRotation() => R;

    public PitchRoll GetPitchRoll() => new(Pitch, Roll);

    public TempOffset Clone() => new(X, Y, Z, R, Pitch, Roll);
}
