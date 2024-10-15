using System;
using System.Numerics;

namespace SimpleHeels;

public record RelativeEmoteOffsetProvider(EmoteConfig EmoteOffset, IOffsetProvider? BaseOffset) : IOffsetProvider {
    public Vector3 GetOffset() => BaseOffset == null ? EmoteOffset.GetOffset() : EmoteOffset.GetOffset() + BaseOffset.GetOffset();
    public float GetRotation() => EmoteOffset.GetRotation();
    
    public PitchRoll GetPitchRoll() => PitchRoll.Zero;
}
