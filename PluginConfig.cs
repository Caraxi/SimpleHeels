using System.Collections.Generic;
using System.Linq;
using Dalamud.Configuration;
using Dalamud.Game.ClientState.Keys;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace SimpleHeels;

public class PluginConfig : IPluginConfiguration {
    public bool ConfigInCutscene = false;
    public bool ConfigInGpose = true;
    public bool DebugOpenOnStartup = true;
    public bool DetailedPerformanceLogging = false;

    public float DismissedChangelog = 0;

    public bool Enabled = true;
    public bool ExtendedDebugOpen = false;
    public List<GroupConfig> Groups = new();
    public bool HideKofi = false;

    public string ModelEditorLastFolder = string.Empty;
    public float PlusMinusDelta = 0.001f;
    public bool PreferModelPath = false;
    public bool ShowCopyUi = false;
    public bool ShowPlusMinusButtons = true;
    public bool UseModelOffsets = true;
    public bool ApplyToMinions = false;
    public bool RightClickResetValue = false;
    public bool CopyAttributeButton;

    public bool TempOffsetWindowOpen = false;
    public bool TempOffsetWindowLock = false;
    public bool TempOffsetWindowTooltips = true;
    public bool TempOffsetWindowTransparent = false;
    public bool TempOffsetWindowPlusMinus = true;
    public bool TempOffsetWindowLockInViewport = false;
    public bool TempOffsetPitchRoll = false;
    public bool TempOffsetShowGizmo = false;
    public VirtualKey[] TempOffsetGizmoHotkey = [];


    public bool ApplyStaticMinionPositions = true;
    public bool UsePrecisePositioning = true;

    public Dictionary<uint, Dictionary<string, CharacterConfig>> WorldCharacterDictionary = new();
    public int Version { get; set; } = 2;

    public void Initialize() {
        if (TempOffsetGizmoHotkey.Length == 0) {
            // Default to ALT on new installs, SHIFT on existing installation.
            TempOffsetGizmoHotkey = Version == 1 ? [VirtualKey.SHIFT] : [VirtualKey.MENU];
        }
        
        // Migrate Sit/Sleep offsets

        foreach (var w in WorldCharacterDictionary.Values)
        foreach (var c in w.Values)
            c.Initialize();

        foreach (var g in Groups) g.Initialize();
        Version = 2;
    }

    public unsafe bool TryGetCharacterConfig(string name, uint world, DrawObject* drawObject, out CharacterConfig? characterConfig) {
        characterConfig = null;
        if (WorldCharacterDictionary.TryGetValue(world, out var w))
            if (w.TryGetValue(name, out characterConfig) && characterConfig.Enabled)
                return true;

        characterConfig = Groups.FirstOrDefault(g => g.Matches(drawObject, name, world));
        return characterConfig != null;
    }

    public bool TryAddCharacter(string name, uint homeWorld) {
        if (!WorldCharacterDictionary.ContainsKey(homeWorld)) WorldCharacterDictionary.Add(homeWorld, new Dictionary<string, CharacterConfig>());
        if (WorldCharacterDictionary.TryGetValue(homeWorld, out var world)) return world.TryAdd(name, new CharacterConfig().Initialize());

        return false;
    }
}
