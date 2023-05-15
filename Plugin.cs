using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Reflection;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.Logging;
using Dalamud.Memory;
using Dalamud.Plugin;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace SimpleHeels;

public unsafe class Plugin : IDalamudPlugin {
    public string Name => "Simple Heels";
    
    public PluginConfig Config { get; }
    
    private readonly ConfigWindow configWindow;
    private readonly WindowSystem windowSystem;

    internal static bool IsDebug;
    internal static bool IsEnabled;

    private delegate void SetDrawOffset(GameObjectExt* gameObject, float x, float y, float z);

    [Signature("E8 ?? ?? ?? ?? 0F 28 74 24 ?? 80 3D", DetourName = nameof(SetDrawOffsetDetour))]
    private Hook<SetDrawOffset>? setDrawOffset;

    private void SetDrawOffsetDetour(GameObjectExt* gameObject, float x, float y, float z) {
        if (gameObject->GameObject.ObjectIndex < 200 && managedIndex[gameObject->GameObject.ObjectIndex]) {
            PluginLog.Log("Game Applied Offset. Releasing Control");
            if (gameObject->GameObject.ObjectIndex == 0) {
                LegacyApiProvider.OnOffsetChange(0);
            }
            managedIndex[gameObject->GameObject.ObjectIndex] = false;
        }
        setDrawOffset?.Original(gameObject, x, y, z);
    }

    public Plugin(DalamudPluginInterface pluginInterface) {
        
        pluginInterface.Create<PluginService>();

        Config = pluginInterface.GetPluginConfig() as PluginConfig ?? new PluginConfig();

        windowSystem = new WindowSystem(Assembly.GetExecutingAssembly().FullName);
        configWindow = new ConfigWindow($"{Name} | Config", this, Config) {
            #if DEBUG
            IsOpen = Config.DebugOpenOnStartup
            #endif
        };
        windowSystem.AddWindow(configWindow);
        
        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += () => OnCommand(string.Empty, string.Empty);

        PluginService.Commands.AddHandler("/heels", new CommandInfo(OnCommand) {
            HelpMessage = $"Open the {Name} config window.",
            ShowInHelp = true
        });
        
        #if DEBUG
        IsDebug = true;
        #endif

        if (PluginService.Commands.Commands.ContainsKey("/xlheels")) {
            PluginService.ChatGui.PrintError($"{Name} cannot be started while 'Heels Plugin' is installed. Please uninstall Heels Plugin");
            PluginService.Framework.Update += WaitForHeelsPlugin;
        } else {
            EnablePlugin();
        }
    }

    private void EnablePlugin() {
        if (IsEnabled) return;
        IsEnabled = true;
        LegacyApiProvider.Init(this);
        PluginService.Framework.Update += OnFrameworkUpdate;
        SignatureHelper.Initialise(this);
        setDrawOffset?.Enable();
    }

    private int nextUpdateIndex;
    private static bool _updateAll;
    private readonly bool[] managedIndex = new bool[200];
    
    public static void RequestUpdateAll() {
        _updateAll = true;
    }

    private void UpdateObjectIndex(int updateIndex) {
        if (updateIndex is < 0 or >= 200) return;
        var character = PluginService.Objects[updateIndex] as PlayerCharacter;
        if (character == null) {
            managedIndex[updateIndex] = false;
            return;
        }
        var obj = (GameObjectExt*)character.Address;
        if (!managedIndex[updateIndex] && obj->DrawOffset.Y != 0) {

            if (updateIndex == 0) LegacyApiProvider.OnOffsetChange(0);
            return;
        }
        
        var chr = (Character*)character.Address;
        var offset = GetOffset((GameObjectExt*)character.Address);
        if (offset == null) {
            if (managedIndex[updateIndex]) {
                managedIndex[updateIndex] = false;
                setDrawOffset?.Original(obj, obj->DrawOffset.X, 0, obj->DrawOffset.Z);
            }
            if (updateIndex == 0) LegacyApiProvider.OnOffsetChange(0);
            return;
        }
        
        if (MathF.Abs(obj->DrawOffset.Y - offset.Value) > 0.00001f) {
            PluginLog.Debug($"Update Player Offset: {character.Name.TextValue} => {offset} ({chr->Mode} / {chr->ModeParam})");
            setDrawOffset?.Original(obj, obj->DrawOffset.X, offset.Value, obj->DrawOffset.Z);
            managedIndex[updateIndex] = true;
            if (updateIndex == 0) LegacyApiProvider.OnOffsetChange(offset.Value);
        }
    }
    
    private void OnFrameworkUpdate(Framework framework) {
        if (_updateAll) {
            _updateAll = false;
            for (var i = 0; i < 200; i++) {
                UpdateObjectIndex(i);
            }
        } else {
            nextUpdateIndex %= 200;
            var updateIndex = nextUpdateIndex++;
            if (updateIndex != 0 && !managedIndex[updateIndex]) {
                UpdateObjectIndex(nextUpdateIndex++);
            }
        }

        for (var i = 0; i < 200; i++) {
            if (i == 0 || managedIndex[i]) UpdateObjectIndex(i);
        }
        
    }

    private void OnCommand(string command, string args) {
        if (args.ToLower() == "debug") {
            IsDebug = !IsDebug;
            return;
        }
        configWindow.IsOpen = !configWindow.IsOpen;
    }

    public static Dictionary<(string, uint), float> IpcAssignedOffset { get; } = new();
    
    private float GetOffsetFromConfig(string name, uint homeWorld, ushort modelId) {
        if (isDisposing) return 0;
        if (IpcAssignedOffset.TryGetValue((name, homeWorld), out var offset)) return offset;
        if (!Config.TryGetCharacterConfig(name, homeWorld, out var characterConfig) || characterConfig == null) {
            return 0;
        }
        var firstMatch = characterConfig.HeelsConfig.FirstOrDefault(hc => hc.Enabled && hc.ModelId == modelId);
        return firstMatch?.Offset ?? 0;
    }

    public float? GetOffset(GameObjectExt* gameObject) {
        if (isDisposing) return null;
        if (gameObject == null) return null;
        if (!(gameObject->ObjectKind == 1 && gameObject->SubKind == 4)) return null;
        var drawObject = gameObject->DrawObject;
        if (drawObject == null) return null;
        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase) return null;
        var characterBase = (CharacterBase*)drawObject;
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human) return null;
        var human = (Human*)characterBase;
        
        var character = (Character*)gameObject;
        if (character->Mode == Character.CharacterModes.InPositionLoop && character->ModeParam is 1 or 2 or 3) return null;
        if (character->Mode == Character.CharacterModes.EmoteLoop && character->ModeParam is 21) return null;
        var name = MemoryHelper.ReadSeString(new nint(gameObject->GetName()), 64);
        return GetOffsetFromConfig(name.TextValue, character->HomeWorld, human->Feet.Id);
    }
    
    private bool isDisposing;

    public void Dispose() {
        isDisposing = true;
        PluginLog.Verbose($"Dispose");
        PluginService.Framework.Update -= WaitForHeelsPlugin;
        PluginService.Framework.Update -= OnFrameworkUpdate;

        for (var i = 0; i < 200; i++) {
            if (i == 0 || managedIndex[i]) UpdateObjectIndex(i);
        }
        
        LegacyApiProvider.DeInit();
        PluginService.Commands.RemoveHandler("/heels");
        windowSystem.RemoveAllWindows();
        
        PluginService.PluginInterface.SavePluginConfig(Config);
        
        setDrawOffset?.Disable();
        setDrawOffset?.Dispose();
        setDrawOffset = null!;
    }

    private readonly Stopwatch waitTimer = new Stopwatch();
    private void WaitForHeelsPlugin(Framework framework) {
        if (!waitTimer.IsRunning) waitTimer.Restart();
        if (waitTimer.ElapsedMilliseconds < 1000) return;
        waitTimer.Restart();
        if (PluginService.Commands.Commands.ContainsKey("/xlheels")) return;
        PluginService.Framework.Update -= WaitForHeelsPlugin;
        waitTimer.Stop();
        EnablePlugin();
    }
}
