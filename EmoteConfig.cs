using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;

namespace SimpleHeels;

public class EmoteConfig : IOffsetProvider {
    [NonSerialized] public bool Editing = false;

    public EmoteIdentifier Emote;
    public bool Enabled = true;
    public string Label = string.Empty;
    public HashSet<EmoteIdentifier> LinkedEmotes = new();
    public bool Locked = false;

    public Vector3 Offset = new(0, 0, 0);
    public float Rotation = 0f;

    public Vector3 GetOffset() => Offset;

    public float GetRotation() => Rotation;

    public bool ShouldSerializeEnabled() => ApiProvider.IsSerializing == false;

    public bool ShouldSerializeLocked() => ApiProvider.IsSerializing == false;

    public bool ShouldSerializeLabel() => ApiProvider.IsSerializing == false;

    public bool ShouldSerializeLinkedEmotes() => LinkedEmotes.Count > 0;

    public bool MatchesEmote(params EmoteIdentifier?[] emoteIds) => emoteIds.Any(e => e != null && (e == Emote || LinkedEmotes.Contains(e)));
}
