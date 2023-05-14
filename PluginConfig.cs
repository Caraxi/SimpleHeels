using System.Collections.Generic;
using Dalamud.Configuration;

namespace SimpleHeels; 

public class PluginConfig : IPluginConfiguration {
    public int Version { get; set; } = 1;

    public Dictionary<uint, Dictionary<string, CharacterConfig>> WorldCharacterDictionary = new();
    
    public bool DebugOpenOnStartup = true;
    public bool ShowPlusMinusButtons = false;
    public float PlusMinusDelta = 0.001f;

    public bool TryGetCharacterConfig(string name, uint world, out CharacterConfig? characterConfig) {
        characterConfig = null;
        if (!WorldCharacterDictionary.TryGetValue(world, out var w)) return false;
        return w.TryGetValue(name, out characterConfig);
    }

    public bool TryAddCharacter(string name, uint homeWorld) {
        if (!WorldCharacterDictionary.ContainsKey(homeWorld)) WorldCharacterDictionary.Add(homeWorld, new Dictionary<string, CharacterConfig>());
        if (WorldCharacterDictionary.TryGetValue(homeWorld, out var world)) {
            return world.TryAdd(name, new CharacterConfig());
        }

        return false;
    }
}
