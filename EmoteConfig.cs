using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SimpleHeels;

public class EmoteConfig : IOffsetProvider {

    public EmoteConfig IpcClone() {
        return new EmoteConfig {
            Emote = new EmoteIdentifier(Emote.EmoteModeId, Emote.CPoseState),
            LinkedEmotes = LinkedEmotes.Select(l => new EmoteIdentifier(l.EmoteModeId, l.CPoseState)).ToHashSet(),
            Offset = new Vector3(Offset.X, Offset.Y, Offset.Z),
            Rotation = Rotation,
            RelativeOffset = RelativeOffset,
        };
    }
    
    [NonSerialized] public bool Editing = false;

    public EmoteIdentifier Emote = new(0, 0);
    public bool Enabled = true;
    public string Label = string.Empty;
    public HashSet<EmoteIdentifier> LinkedEmotes = new();
    public bool Locked = false;

    public Vector3 Offset = new(0, 0, 0);
    public float Rotation = 0f;
    public bool RelativeOffset;

    public Vector3 GetOffset() => Offset;

    public float GetRotation() => Rotation;
    
    public PitchRoll GetPitchRoll() => PitchRoll.Zero;

    public bool ShouldSerializeEnabled() => ApiProvider.IsSerializing == false;

    public bool ShouldSerializeLocked() => ApiProvider.IsSerializing == false;

    public bool ShouldSerializeLabel() => ApiProvider.IsSerializing == false;

    public bool ShouldSerializeLinkedEmotes() => LinkedEmotes.Count > 0;

    public bool MatchesEmote(params EmoteIdentifier?[] emoteIds) => emoteIds.Any(e => e != null && (e == Emote || LinkedEmotes.Contains(e)));
}
