using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.SubKinds;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Newtonsoft.Json;
#pragma warning disable CS0659

namespace SimpleHeels;

public class IpcCharacterConfig : CharacterConfig {

    [JsonIgnore] public string IpcJson { get; private set; } = string.Empty;

    public TempOffset? TempOffset;
    public TempOffset? MinionPosition;
    public TempOffset? EmotePosition;
    public string PluginVersion = string.Empty;
    public Dictionary<string, string> Tags = new();
    
    
    public unsafe IpcCharacterConfig(Plugin plugin, IPlayerCharacter player) {
        if (player == null) throw new Exception("No Player");

        if (plugin.TryGetCharacterConfig(player, out var characterConfig, false) && characterConfig != null) {
            DefaultOffset = characterConfig.GetFirstMatch(player, false)?.GetOffset().Y ?? 0;
            EmoteConfigs = characterConfig?.EmoteConfigs?.Where(e => e.Enabled).Select(e => e.IpcClone()).ToList() ?? new List<EmoteConfig>();
        }

        if (Plugin.Tags.TryGetValue(player.EntityId, out var tags)) Tags = tags;
        if (player.ObjectIndex < Constants.ObjectLimit && Plugin.TempOffsets[player.ObjectIndex] != null) {
            TempOffset = Plugin.TempOffsets[player.ObjectIndex]?.Clone() ?? null;
        }
        var chr = (Character*)player.Address;
        if (Plugin.Config.ApplyStaticMinionPositions) {
            
            if (chr->CompanionData.CompanionObject != null && Utils.StaticMinions.Value.Contains(chr->CompanionData.CompanionObject->Character.GameObject.BaseId)) {
                var drawObj = chr->CompanionData.CompanionObject->Character.GameObject.DrawObject;
                if (drawObj != null) {
                    var p = drawObj->Object.Position;
                    MinionPosition = new TempOffset(drawObj->Position.X, drawObj->Position.Y, drawObj->Position.Z, drawObj->Rotation.EulerAngles.Y * MathF.PI / 180);
                }
            }
        }

        if (chr->Mode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop) {
            // Precise Positioning
            EmotePosition = new TempOffset(chr->GameObject.Position.X, chr->GameObject.Position.Y, chr->GameObject.Position.Z, chr->GameObject.Rotation);
        }

        PluginVersion = Assembly.GetExecutingAssembly()?.GetName()?.Version?.ToString() ?? string.Empty;
    }

    public IpcCharacterConfig() { }

    public override bool ShouldSerializeEnabled() => false;

    public override bool ShouldSerializeHeelsConfig() => false;

    public override bool ShouldSerializeSittingOffsetZ() => false;

    public override bool ShouldSerializeSittingOffsetY() => false;

    public override bool ShouldSerializeGroundSitOffset() => false;

    public override bool ShouldSerializeSleepOffset() => false;

    public override bool ShouldSerializeIgnoreModelOffsets() => false;

    public bool ShouldSerializeTags() => Tags is { Count: > 0 };

    public override bool ShouldSerializeEmoteConfigs() => EmoteConfigs is { Count: > 0 };
    public override bool ShouldSerializeDefaultOffset() => MathF.Abs(DefaultOffset) > Constants.FloatDelta;

    public bool ShouldSerializeTempOffset() => TempOffset != null;
    public bool ShouldSerializeMinionPosition() => MinionPosition != null;
    public bool ShouldSerializeEmotePosition() => EmotePosition != null;
    public bool ShouldSerializePluginVersion() => !string.IsNullOrWhiteSpace(PluginVersion) && (
        ShouldSerializeDefaultOffset() || ShouldSerializeEmotePosition() || ShouldSerializeEmoteConfigs() || 
        ShouldSerializeTempOffset() || ShouldSerializeMinionPosition() || ShouldSerializeTags()
    );

    public override bool ShouldSerializeVersion() => ShouldSerializePluginVersion();

    public static IpcCharacterConfig? FromString(string json) {
        if (string.IsNullOrWhiteSpace(json)) return new IpcCharacterConfig().Initialize() as IpcCharacterConfig;

        try {
            var config = JsonConvert.DeserializeObject<IpcCharacterConfig>(json);
            if (config == null) return null;
            config.IpcJson = json;
            config.Initialize();
            return config;
        } catch (Exception ex) {
            PluginService.Log.Error(ex, "Error decoding IPC Character Config");
            PluginService.Log.Error(json);
            return null;
        }
    }

    public override string ToString() {
        try {
            ApiProvider.IsSerializing = true;
            return JsonConvert.SerializeObject(this);
        } finally {
            ApiProvider.IsSerializing = false;
        }
    }
    
    public override unsafe IOffsetProvider? GetFirstMatch(Character* character, bool checkEmote = true) {
        if (character == null) return null;

        if (TempOffset != null) return TempOffset;
        
        if (checkEmote && EmoteConfigs != null) {
            var emote = EmoteIdentifier.Get(character);
            if (emote != null) {
                var e = EmoteConfigs.FirstOrDefault(ec => ec.Enabled && (ec.Emote == emote || ec.LinkedEmotes.Contains(emote)));
                if (e != null) {
                    if (e.RelativeOffset) return new RelativeEmoteOffsetProvider(e, GetFirstMatch(character, false));
                    return e;
                }
            }
        }

        if (character->GameObject.DrawObject == null) return null;
        if (character->GameObject.DrawObject->Object.GetObjectType() != ObjectType.CharacterBase) return null;
        var characterBase = (CharacterBase*)character->GameObject.DrawObject;
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human) return null;
        return this;
    }

    public override bool Equals(object? obj) {
        if (obj is not IpcCharacterConfig other) return false;
        return JsonConvert.SerializeObject(this) == JsonConvert.SerializeObject(other);
    }
}
