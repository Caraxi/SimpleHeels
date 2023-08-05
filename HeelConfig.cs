namespace SimpleHeels;

public enum ModelSlot : byte {
    Top = 1,
    Legs = 3,
    Feet = 4,
}

public class HeelConfig {
    public bool Enabled;
    public bool PathMode;
    public string? Label = string.Empty;
    public string? Path = string.Empty;
    public ushort ModelId;
    public float Offset;
    public ModelSlot Slot = ModelSlot.Feet;
    public ModelSlot RevertSlot = ModelSlot.Feet;
    public bool Locked;
}
