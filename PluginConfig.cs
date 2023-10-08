using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace SimpleHeels; 

public class PluginConfig : IPluginConfiguration {
    public int Version { get; set; } = 1;

    public Dictionary<uint, Dictionary<string, CharacterConfig>> WorldCharacterDictionary = new();
    public List<GroupConfig> Groups = new();
    
    public bool Enabled = true;
    public bool DebugOpenOnStartup = true;
    public bool ShowPlusMinusButtons = true;
    public float PlusMinusDelta = 0.001f;
    public bool UseModelOffsets = true;
    public bool ConfigInGpose = true;
    public bool ConfigInCutscene = false;
    public bool HideKofi = false;

    public string ModelEditorLastFolder = string.Empty;
    
    public float DismissedChangelog = 0;

    public unsafe bool TryGetCharacterConfig(string name, uint world, DrawObject* drawObject, out CharacterConfig? characterConfig) {
        characterConfig = null;
        if (WorldCharacterDictionary.TryGetValue(world, out var w)) {
            if (w.TryGetValue(name, out characterConfig)) {
                return true;
            }
        }

        characterConfig = Groups.FirstOrDefault(g => g.Matches(drawObject, name, world));
        return characterConfig != null;
    }

    public bool TryAddCharacter(string name, uint homeWorld) {
        if (!WorldCharacterDictionary.ContainsKey(homeWorld)) WorldCharacterDictionary.Add(homeWorld, new Dictionary<string, CharacterConfig>());
        if (WorldCharacterDictionary.TryGetValue(homeWorld, out var world)) {
            return world.TryAdd(name, new CharacterConfig());
        }

        return false;
    }
}
