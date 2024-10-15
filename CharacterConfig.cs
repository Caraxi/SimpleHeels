using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace SimpleHeels;

public class CharacterConfig : IOffsetProvider {
    public float DefaultOffset = 0f;
    public List<EmoteConfig>? EmoteConfigs;

    public bool Enabled = true;
    public List<HeelConfig> HeelsConfig = new();
    public bool IgnoreModelOffsets = false;
    public uint Version = 2;

    public Vector3 GetOffset() {
        return new Vector3(0, DefaultOffset, 0);
    }

    public float GetRotation() {
        return 0f;
    }
    
    public PitchRoll GetPitchRoll() => PitchRoll.Zero;

    public virtual bool ShouldSerializeIgnoreModelOffsets() {
        return true;
    }

    public CharacterConfig Initialize() {
        if (EmoteConfigs != null) return this;
        
        EmoteConfigs = new List<EmoteConfig> {
            new() {
                Enabled = true, 
                Emote = new EmoteIdentifier(1, 0), 
                LinkedEmotes = new HashSet<EmoteIdentifier>() {
                    new(1, 1),
                    new(1, 2),
                    new(1, 3),
                }, 
                Offset = new Vector3(0, MathF.Abs(GroundSitOffset) >= Constants.FloatDelta ? GroundSitOffset : 0f, 0)
            },
            new() { 
                Enabled = true,
                Emote = new EmoteIdentifier(2, 0),
                LinkedEmotes = new HashSet<EmoteIdentifier> {
                    new(2, 1),
                    new(2, 2),
                    new(2, 3),
                    new(2, 4),
                },
                Offset = new Vector3(0, MathF.Abs(SittingOffsetY) >= Constants.FloatDelta ? SittingOffsetY : 0f, MathF.Abs(SittingOffsetZ) >= Constants.FloatDelta ? SittingOffsetZ : 0f) 
            },
            new() {
                Enabled = true,
                Emote = new EmoteIdentifier(3, 0), 
                LinkedEmotes = new HashSet<EmoteIdentifier> {
                    new(3, 1),
                    new(3, 2),
                }, 
                Offset = new Vector3(0, MathF.Abs(SleepOffset) >= Constants.FloatDelta ? SleepOffset : 0f, 0)
            }
        };
        
        
        
        return this;
    }

    public unsafe bool TryGetFirstMatch(Character* character, [NotNullWhen(true)] out IOffsetProvider? offsetProvider, bool checkEmote = true) {
        offsetProvider = GetFirstMatch(character, checkEmote);
        return offsetProvider != null;
    }
    
    public unsafe bool TryGetFirstMatch(GameObject* character, [NotNullWhen(true)] out IOffsetProvider? offsetProvider, bool checkEmote = true) {
        offsetProvider = GetFirstMatch(character, checkEmote);
        return offsetProvider != null;
    }

    public unsafe IOffsetProvider? GetFirstMatch(IPlayerCharacter playerCharacter, bool checkEmote = true) {
        var character = (Character*)playerCharacter.Address;
        if (character == null) return null;
        return GetFirstMatch(character, checkEmote);
    }

    public virtual unsafe IOffsetProvider? GetFirstMatch(Character* character, bool checkEmote = true) {
        if (character == null) return null;
        return GetFirstMatch(&character->GameObject, checkEmote);
    }

    public unsafe IOffsetProvider? GetFirstMatch(GameObject* character, bool checkEmote = true) {
        if (!Enabled) return null;
        if (character == null) return null;
        if (checkEmote && character->IsCharacter()) {
            var emoteId = EmoteIdentifier.Get((Character*)character);
            if (emoteId != null) {
                var e = EmoteConfigs?.FirstOrDefault(ec => ec.Enabled && (ec.Emote == emoteId || ec.LinkedEmotes.Contains(emoteId)));
                if (e != null) {
                    if (e.RelativeOffset) return new RelativeEmoteOffsetProvider(e, GetFirstMatch(character, false));
                    return e;
                }
            }
        }

        if (character->DrawObject == null) return null;
        if (character->DrawObject->Object.GetObjectType() != ObjectType.CharacterBase) return null;
        var characterBase = (CharacterBase*)character->DrawObject;
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human) return null;

        if (TryGetFirstMatch((Human*)characterBase, out var heelConfig)) return heelConfig;

        if (!IgnoreModelOffsets && Plugin.TryGetOffsetFromModels((Human*)characterBase, out var modelOffset)) return new ModelOffsetProvider(modelOffset.Value);

        return this;
    }

    public unsafe bool TryGetFirstMatch(Human* human, [NotNullWhen(true)] out HeelConfig? heelConfig) {
        heelConfig = GetFirstMatch(human);
        return heelConfig != null;
    }

    public unsafe HeelConfig? GetFirstMatch(Human* human) {
        if (!Enabled) return null;
        string? feetModelPath = null;
        string? topModelPath = null;
        string? legsModelPath = null;

        return HeelsConfig.Select((hc, index) => (hc, index)).OrderBy(a => a.hc.Slot).ThenBy(a => a.index).Select(a => a.hc).FirstOrDefault(hc => {
            if (!hc.Enabled) return false;
            switch (hc.Slot) {
                case ModelSlot.Feet:
                    feetModelPath ??= Plugin.GetModelPath(human, ModelSlot.Feet);
                    return (hc.PathMode == false && hc.ModelId == human->Feet.Id) || (hc.PathMode && feetModelPath != null && feetModelPath.Equals(hc.Path, StringComparison.OrdinalIgnoreCase));
                case ModelSlot.Top:
                    topModelPath ??= Plugin.GetModelPath(human, ModelSlot.Top);
                    return (hc.PathMode == false && hc.ModelId == human->Top.Id) || (hc.PathMode && topModelPath != null && topModelPath.Equals(hc.Path, StringComparison.OrdinalIgnoreCase));
                case ModelSlot.Legs:
                    legsModelPath ??= Plugin.GetModelPath(human, ModelSlot.Legs);
                    return (hc.PathMode == false && hc.ModelId == human->Legs.Id) || (hc.PathMode && legsModelPath != null && legsModelPath.Equals(hc.Path, StringComparison.OrdinalIgnoreCase));
                default:
                    return false;
            }
        });
    }

    public unsafe EmoteConfig? GetEmoteConfig(Character* character) {
        if (character == null) return null;
        return GetEmoteConfig(EmoteIdentifier.Get(character));
    }

    public EmoteConfig? GetEmoteConfig(EmoteIdentifier? emoteId) {
        if (emoteId == null) return null;
        return EmoteConfigs?.FirstOrDefault(ec => ec.Enabled && ec.LinkedEmotes.Contains(emoteId));
    }

    public List<HeelConfig> GetDuplicates(HeelConfig hc, bool enabledOnly = false) {
        var l = new List<HeelConfig>();
        foreach (var h in HeelsConfig) {
            if (enabledOnly && h.Enabled == false) continue;
            if (h.PathMode != hc.PathMode) continue;
            if (h.Slot != hc.Slot) continue;

            if (h.PathMode) {
                if (!string.Equals(h.Path, hc.Path, StringComparison.OrdinalIgnoreCase)) continue;
            } else {
                if (h.ModelId != hc.ModelId) continue;
            }

            l.Add(h);
        }

        return l;
    }

    public virtual bool ShouldSerializeEnabled() => true;

    public virtual bool ShouldSerializeHeelsConfig() => true;

    public virtual bool ShouldSerializeEmoteConfigs() => true;

    public virtual bool ShouldSerializeVersion() => true;

    #region Legacy Data

    public float SittingOffsetZ = 0f;
    public float SittingOffsetY = 0f;
    public float GroundSitOffset = 0f;
    public float SleepOffset = 0f;

    public virtual bool ShouldSerializeSittingOffsetZ() => MathF.Abs(SittingOffsetZ) >= Constants.FloatDelta;

    public virtual bool ShouldSerializeSittingOffsetY() => MathF.Abs(SittingOffsetY) >= Constants.FloatDelta;

    public virtual bool ShouldSerializeGroundSitOffset() => MathF.Abs(GroundSitOffset) >= Constants.FloatDelta;

    public virtual bool ShouldSerializeSleepOffset() => MathF.Abs(SleepOffset) >= Constants.FloatDelta;

    public virtual bool ShouldSerializeDefaultOffset() => true;

    #endregion
}
