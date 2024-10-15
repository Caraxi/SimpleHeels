using System.Numerics;

namespace SimpleHeels;

public enum ModelSlot : byte {
    Top = 1,
    Legs = 3,
    Feet = 4
}

public class HeelConfig : IOffsetProvider {
    public bool Enabled;
    public string? Label = string.Empty;
    public bool Locked;
    public ushort ModelId;
    public float Offset;
    public string? Path = string.Empty;
    public bool PathMode;
    public ModelSlot RevertSlot = ModelSlot.Feet;
    public ModelSlot Slot = ModelSlot.Feet;

    public Vector3 GetOffset() => Vector3.Zero with { Y = Offset };

    public float GetRotation() => 0f;

    public PitchRoll GetPitchRoll() => PitchRoll.Zero;
}
