using System.Numerics;

namespace SimpleHeels;

public interface IOffsetProvider {
    public Vector3 GetOffset();
    public float GetRotation();
    public PitchRoll GetPitchRoll(); // Pitch, Yaw, Roll
    
    public void Deconstruct(out float x, out float y, out float z, out float r, out float roll, out float pitch) {
        var o = GetOffset();
        x = o.X;
        y = o.Y;
        z = o.Z;
        r = GetRotation();
        (roll, pitch) = GetPitchRoll();
    }

    public bool Is(IOffsetProvider other) {
        if (this is RelativeEmoteOffsetProvider t && other is RelativeEmoteOffsetProvider o) {
            return Equals(o.EmoteOffset, t.EmoteOffset) && Equals(o.BaseOffset, t.BaseOffset);
        }

        if (this is RelativeEmoteOffsetProvider t2) {
            return Equals(t2.BaseOffset, other) || Equals(t2.EmoteOffset, other);
        }

        if (other is RelativeEmoteOffsetProvider o2) {
            return Equals(o2.BaseOffset, this) || Equals(o2.EmoteOffset, other);
        }
        
        return Equals(other);
    }
}
