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
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace SimpleHeels;

public unsafe class Plugin : IDalamudPlugin {
    public string Name => "Simple Heels";
    
    public PluginConfig Config { get; }
    
    private readonly ConfigWindow configWindow;
    private readonly WindowSystem windowSystem;

    internal static bool IsDebug;
    internal static bool IsEnabled;

    private delegate void SetDrawOffset(GameObject* gameObject, float x, float y, float z);

    [Signature("E8 ?? ?? ?? ?? 0F 28 74 24 ?? 80 3D", DetourName = nameof(SetDrawOffsetDetour))]
    private Hook<SetDrawOffset>? setDrawOffset;

    private void SetDrawOffsetDetour(GameObject* gameObject, float x, float y, float z) {
        if (gameObject->ObjectIndex < 200 && managedIndex[gameObject->ObjectIndex]) {
            PluginLog.Log("Game Applied Offset. Releasing Control");
            if (gameObject->ObjectIndex == 0) {
                LegacyApiProvider.OnOffsetChange(0);
            }
            managedIndex[gameObject->ObjectIndex] = false;
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
        var obj = (GameObject*)character.Address;
        if (!managedIndex[updateIndex] && obj->DrawOffset.Y != 0) {

            if (updateIndex == 0) LegacyApiProvider.OnOffsetChange(0);
            return;
        }
        
        var chr = (Character*)character.Address;
        var offset = GetOffset((GameObject*)character.Address);
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

            return;
        }

        if (!Config.Enabled) return;
        
        nextUpdateIndex %= 200;
        var updateIndex = nextUpdateIndex++;
        if (updateIndex != 0 && !managedIndex[updateIndex]) {
            UpdateObjectIndex(nextUpdateIndex++);
        }

        for (var i = 0; i < 200; i++) {
            if (i == 0 || managedIndex[i]) UpdateObjectIndex(i);
        }
        
    }

    private void OnCommand(string command, string args) {
        switch (args.ToLowerInvariant()) {
            case "debug":
                IsDebug = !IsDebug;
                return;
            case "enable":
                Config.Enabled = true;
                RequestUpdateAll();
                break;
            case "disable":
                Config.Enabled = false;
                RequestUpdateAll();
                break;
            case "toggle":
                Config.Enabled = !Config.Enabled;
                RequestUpdateAll();
                break;
            default:
                configWindow.IsOpen = !configWindow.IsOpen;
                break;
        }
    }

    public static Dictionary<(string, uint), float> IpcAssignedOffset { get; } = new();
    
    private float? GetOffsetFromConfig(string name, uint homeWorld, Human* human) {
        if (isDisposing) return null;
        if (IpcAssignedOffset.TryGetValue((name, homeWorld), out var offset)) return offset;
        if (!Config.TryGetCharacterConfig(name, homeWorld, out var characterConfig) || characterConfig == null) {
            return null;
        }

        string? feetModelPath = null;
        string? topModelPath = null;
        string? legsModelPath = null;

        var firstMatch = characterConfig.HeelsConfig.OrderBy(hc => hc.Slot).FirstOrDefault(hc => {
            if (!hc.Enabled) return false;

            switch (hc.Slot) {
                case ModelSlot.Feet:
                    feetModelPath ??= GetModelPath(human, ModelSlot.Feet);
                    return (hc.PathMode == false && hc.ModelId == human->Feet.Id) || (hc.PathMode && feetModelPath != null && feetModelPath.Equals(hc.Path));
                case ModelSlot.Top:
                    topModelPath ??= GetModelPath(human, ModelSlot.Top);
                    return (hc.PathMode == false && hc.ModelId == human->Top.Id) || (hc.PathMode && topModelPath != null && topModelPath.Equals(hc.Path));
                case ModelSlot.Legs:
                    legsModelPath ??= GetModelPath(human, ModelSlot.Legs);
                    return (hc.PathMode == false && hc.ModelId == human->Legs.Id) || (hc.PathMode && legsModelPath != null && legsModelPath.Equals(hc.Path));
                default:
                    return false;
            }
        });
        return firstMatch?.Offset ?? null;
    }

    public static string? GetModelPath(Human* human, ModelSlot slot) {
        if (human == null) return null;
        var modelArray = human->CharacterBase.Models;
        if (modelArray == null) return null;
        var feetModel = modelArray[(byte)slot];
        if (feetModel == null) return null;
        var modelResource = feetModel->ModelResourceHandle;
        if (modelResource == null) return null;
        return modelResource->ResourceHandle.FileName.ToString();
    }
    
    public float? GetOffset(GameObject* gameObject) {
        if (isDisposing) return null;
        if (!Config.Enabled) return null;
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
        var configuredOffset = GetOffsetFromConfig(name.TextValue, character->HomeWorld, human);
        if (configuredOffset != null) return configuredOffset;
        
        if (Config.UseModelOffsets) {
            float? CheckModelSlot(ModelSlot slot) {
                var modelArray = human->CharacterBase.Models;
                if (modelArray == null) return null;
                var feetModel = modelArray[(byte)slot];
                if (feetModel == null) return null;
                var modelResource = feetModel->ModelResourceHandle;
                if (modelResource == null) return null;

                foreach (var attr in modelResource->Attributes) {
                    var str = MemoryHelper.ReadStringNullTerminated(new nint(attr.Item1.Value));
                    if (str.StartsWith("heels_offset=", StringComparison.OrdinalIgnoreCase)) {
                        if (float.TryParse(str[13..], out var offsetAttr)) {
                            return offsetAttr * human->CharacterBase.DrawObject.Object.Scale.Y;
                        }
                    }
                }

                return null;
            }

            return CheckModelSlot(ModelSlot.Top) ?? CheckModelSlot(ModelSlot.Legs) ?? CheckModelSlot(ModelSlot.Feet);
        }

        return null;
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
