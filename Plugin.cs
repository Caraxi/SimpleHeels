using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.Linq;
using System.Numerics;
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
    public const int ObjectLimit = 596;
    
    public string Name => "Simple Heels";
    
    public PluginConfig Config { get; }
    
    private readonly ConfigWindow configWindow;
    private readonly WindowSystem windowSystem;

    internal static bool IsDebug;
    internal static bool IsEnabled;

    private delegate void SetDrawOffset(GameObject* gameObject, float x, float y, float z);

    [Signature("E8 ?? ?? ?? ?? 0F 28 74 24 ?? 80 3D", DetourName = nameof(SetDrawOffsetDetour))]
    private Hook<SetDrawOffset>? setDrawOffset;

    private delegate void* CloneActor(Character* destination, Character* source, uint a3);
    [Signature("E8 ?? ?? ?? ?? 0F B6 9F ?? ?? ?? ?? 48 8D 8F", DetourName = nameof(CloneActorDetour))]
    private Hook<CloneActor>? cloneActor;

    private void SetDrawOffsetDetour(GameObject* gameObject, float x, float y, float z) {
        try {
            if (gameObject->IsCharacter() && gameObject->ObjectKind == 1 && gameObject->SubKind == 4) {
                var character = (Character*)gameObject;
                if (character->Mode == Character.CharacterModes.InPositionLoop && character->ModeParam == 2) {
                    // Sitting
                    if (TryGetSittingOffset(character, out var offsetY, out var offsetZ)) {
                        PluginLog.LogDebug($"Applied Sitting Offset [{offsetY}, {offsetZ}]");
                        ManagedIndex[gameObject->ObjectIndex] = false;
                    
                        if (gameObject->ObjectIndex == 0) {
                            ApiProvider.SittingPositionChanged(offsetY, offsetZ);
                        }

                        AppliedSittingOffset[gameObject->ObjectIndex] = new Vector2(offsetY, offsetZ);
                        setDrawOffset?.Original(gameObject, x, y + offsetY, z + offsetZ);
                       
                        return;
                    }
                }
            }

            AppliedSittingOffset[gameObject->ObjectIndex] = null;
            if (gameObject->ObjectIndex < ObjectLimit && ManagedIndex[gameObject->ObjectIndex]) {
                PluginLog.LogDebug("Game Applied Offset. Releasing Control");
                ManagedIndex[gameObject->ObjectIndex] = false;
            }
        } catch (Exception ex) {
            PluginLog.Error(ex, "Error handling SetDrawOffset");
        }
        
        setDrawOffset?.Original(gameObject, x, y, z);
    }

    private void* CloneActorDetour(Character* destination, Character* source, uint copyFlags) {
        try {
            ActorMapping.Remove(destination->GameObject.ObjectIndex);
            var name = MemoryHelper.ReadSeString(new nint(source->GameObject.GetName()), 64);
            ActorMapping.Add(destination->GameObject.ObjectIndex, (name.TextValue, source->HomeWorld));
            if (destination->GameObject.ObjectIndex < ObjectLimit && source->GameObject.ObjectIndex < ObjectLimit) {
                ManagedIndex[destination->GameObject.ObjectIndex] = ManagedIndex[source->GameObject.ObjectIndex];
            }
            PluginLog.Verbose($"Game cloned Actor#{source->GameObject.ObjectIndex} to Actor#{destination->GameObject.ObjectIndex} [{name} @ {source->HomeWorld}]");
        
        } catch (Exception ex) {
            PluginLog.Error(ex, "Error handling CloneActor");
        }
        
        return cloneActor!.Original(destination, source, copyFlags);
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
#if DEBUG
        windowSystem.AddWindow(new ExtraDebug(this, Config) {
            IsOpen = Config.DebugOpenOnStartup
        });
#endif
        
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
        ApiProvider.Init(this);
        PluginService.Framework.Update += OnFrameworkUpdate;
        SignatureHelper.Initialise(this);
        setDrawOffset?.Enable();
        cloneActor?.Enable();
        RequestUpdateAll();
    }

    private int nextUpdateIndex;
    private static bool _updateAll;
    
    public bool[] ManagedIndex { get; }= new bool[ObjectLimit];
    public Vector2?[] AppliedSittingOffset { get; }= new Vector2?[ObjectLimit];
    
    public static void RequestUpdateAll() {
        _updateAll = true;
    }

    private bool UpdateObjectIndex(int updateIndex) {
        if (updateIndex is < 0 or >= ObjectLimit) return true;

        var obj = GameObjectManager.GetGameObjectByIndex(updateIndex);
        if (obj == null) {
            ManagedIndex[updateIndex] = false;
            return false;
        }
        
        if (!obj->IsCharacter()) {
            if (ManagedIndex[updateIndex]) 
                setDrawOffset?.Original(obj, obj->DrawOffset.X, 0, obj->DrawOffset.Z);
            ManagedIndex[updateIndex] = false;
            return false;
        }

        if (obj->DrawObject == null) {
            return false;
        }

        if (!ManagedIndex[updateIndex] && obj->DrawOffset.Y != 0) {
            if (updateIndex == 0) {
                ApiProvider.StandingOffsetChanged(0);
            }
            return false;
        }
        
        var offset = GetOffset(obj);
        if (offset == null) {
            if (ManagedIndex[updateIndex]) {
                ManagedIndex[updateIndex] = false;
                setDrawOffset?.Original(obj, obj->DrawOffset.X, 0, obj->DrawOffset.Z);
            }

            if (updateIndex == 0) {
                ApiProvider.StandingOffsetChanged(0);
            }
            return true;
        }
        
        if (MathF.Abs(obj->DrawOffset.Y - offset.Value) > 0.00001f) {
            setDrawOffset?.Original(obj, obj->DrawOffset.X, offset.Value, obj->DrawOffset.Z);
            ManagedIndex[updateIndex] = true;
            if (updateIndex == 0) {
                ApiProvider.StandingOffsetChanged(offset.Value);
            }
        }

        return true;
    }
    
    private void OnFrameworkUpdate(Framework framework) {
        if (_updateAll) {
            _updateAll = false;
            for (var i = 0; i < ObjectLimit; i++) {
                UpdateObjectIndex(i);
                TryUpdateSittingPosition(i);
            }

            return;
        }

        if (!Config.Enabled) return;

        var throttle = 20;
        while (throttle-- > 0) {
            nextUpdateIndex %= ObjectLimit;
            var updateIndex = nextUpdateIndex++;
            if (updateIndex != 0 && !ManagedIndex[updateIndex]) {
                if (UpdateObjectIndex(updateIndex)) 
                    break;
            }
        }
        for (var i = 0; i < ObjectLimit; i++) {
            if (i == 0 || ManagedIndex[i]) UpdateObjectIndex(i);
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
    public static Dictionary<(string, uint), AssignedData> IpcAssignedData { get; } = new();

    public static Dictionary<uint, (string name, ushort homeWorld)> ActorMapping { get; } = new();
    
    private float? GetOffsetFromConfig(string name, uint homeWorld, Human* human) {
        if (isDisposing) return null;
        if (IpcAssignedData.TryGetValue((name, homeWorld), out var data)) return data.Offset;
        if (IpcAssignedOffset.TryGetValue((name, homeWorld), out var offset)) return offset;
        if (!Config.TryGetCharacterConfig(name, homeWorld, &human->CharacterBase.DrawObject, out var characterConfig) || characterConfig == null) {
            return null;
        }

        var firstMatch = characterConfig.GetFirstMatch(human);
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
    
    public float? GetOffset(GameObject* gameObject, bool bypassStandingCheck = false) {
        if (isDisposing) return null;
        if (!Config.Enabled) return null;
        if (gameObject == null) return null;
        var drawObject = gameObject->DrawObject;
        if (drawObject == null) return null;
        if (drawObject->Object.GetObjectType() != ObjectType.CharacterBase) return null;
        var characterBase = (CharacterBase*)drawObject;
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human) return null;
        var human = (Human*)characterBase;
        var character = (Character*)gameObject;
        if (!bypassStandingCheck) {
            if (character->Mode == Character.CharacterModes.InPositionLoop && character->ModeParam is 1 or 2 or 3) return null;
            if (character->Mode == Character.CharacterModes.EmoteLoop && character->ModeParam is 21) return null;
        }
        
        var name = MemoryHelper.ReadSeString(new nint(gameObject->GetName()), 64).TextValue;
        var homeWorld = character->HomeWorld;

        if (character->GameObject.ObjectIndex >= 200 && ActorMapping.TryGetValue(character->GameObject.ObjectIndex, out var mapping)) {
            if (string.IsNullOrWhiteSpace(name)) name = mapping.name;
            if (homeWorld == ushort.MaxValue) homeWorld = mapping.homeWorld;
        }
        
        var configuredOffset = GetOffsetFromConfig(name, homeWorld, human);
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
                        if (float.TryParse(str[13..].Replace(',', '.'), CultureInfo.InvariantCulture, out var offsetAttr)) {
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

        for (var i = 0; i < ObjectLimit; i++) {
            if (i == 0 || ManagedIndex[i]) UpdateObjectIndex(i);
            if (AppliedSittingOffset[i] != null) TryUpdateSittingPosition(i);
        }
        
        ApiProvider.DeInit();
        PluginService.Commands.RemoveHandler("/heels");
        windowSystem.RemoveAllWindows();
        
        PluginService.PluginInterface.SavePluginConfig(Config);
        
        setDrawOffset?.Disable();
        setDrawOffset?.Dispose();
        setDrawOffset = null!;
        
        cloneActor?.Disable();
        cloneActor?.Dispose();
        cloneActor = null!;
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

    public bool TryGetSittingOffset(GameObject* gameObject, out float y, out float z, bool bypassSittingCheck = false) {
        y = 0;
        z = 0;
        if (gameObject == null) return false;
        if (!(gameObject->ObjectKind == 1 && gameObject->SubKind == 4 )) return false;
        return TryGetSittingOffset((Character*)gameObject, out y, out z, bypassSittingCheck);
    }
    
    public bool TryGetSittingOffset(Character* character, out float y, out float z, bool bypassSittingCheck = false) {
        y = 0;
        z = 0;
        if (isDisposing) return false;
        if (character == null) return false;
        if (!bypassSittingCheck && (character->Mode != Character.CharacterModes.InPositionLoop || character->ModeParam != 2)) return false;
        var name = MemoryHelper.ReadSeString(new nint(character->GameObject.GetName()), 64).TextValue;
        var homeWorld = character->HomeWorld;

        if (character->GameObject.ObjectIndex >= 200 && ActorMapping.TryGetValue(character->GameObject.ObjectIndex, out var mapping)) {
            if (string.IsNullOrWhiteSpace(name)) name = mapping.name;
            if (homeWorld == ushort.MaxValue) homeWorld = mapping.homeWorld;
        }
        
        if (IpcAssignedData.TryGetValue((name, homeWorld), out var data)) {
            if (data is { SittingPosition: 0, SittingHeight: 0 }) return false;
            y = data.SittingHeight;
            z = data.SittingPosition;
            return true;
        }
        
        if (!Config.TryGetCharacterConfig(name, homeWorld, character->GameObject.DrawObject, out var characterConfig) || characterConfig == null) return false;
        if (characterConfig is { SittingOffsetY: 0, SittingOffsetZ: 0 }) return false;
        
        y = characterConfig.SittingOffsetY;
        z = characterConfig.SittingOffsetZ;
        return true;

    }

    public void TryUpdateSittingPosition(string name, uint world) {
        var player = PluginService.Objects.FirstOrDefault(t => t is PlayerCharacter playerCharacter && playerCharacter.Name.TextValue == name && playerCharacter.HomeWorld.Id == world);
        if (player == null) return;
        TryUpdateSittingPosition((GameObject*) player.Address);
    }

    public void TryUpdateSittingPositions() {
        foreach (var player in PluginService.Objects.Where(p => p is PlayerCharacter)) {
            TryUpdateSittingPosition((GameObject*) player.Address);
        }
    }

    public void TryUpdateSittingPosition(int index) {
        var player = PluginService.Objects[index] as PlayerCharacter;
        if (player == null) return;
        TryUpdateSittingPosition((GameObject*) player.Address);
    }

    public void TryUpdateSittingPosition(GameObject* gameObject) {
        if (gameObject->ObjectKind != 1 || gameObject->SubKind != 4) return;
        
        var character = (Character*)gameObject;
        if (character->Mode != Character.CharacterModes.InPositionLoop || character->ModeParam != 2) return;

        var currentOffset = AppliedSittingOffset[gameObject->ObjectIndex];
        SetDrawOffsetDetour(gameObject, gameObject->DrawOffset.X, gameObject->DrawOffset.Y - (currentOffset?.X ?? 0), gameObject->DrawOffset.Z - (currentOffset?.Y ?? 0));
    }
    
}
