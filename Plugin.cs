using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.Memory;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace SimpleHeels;

public unsafe class Plugin : IDalamudPlugin {
    internal static bool IsDebug;
    private static bool _updateAll;

    private readonly ConfigWindow configWindow;
    private readonly ExtraDebug extraDebug;
    private readonly WindowSystem windowSystem;

    public Dictionary<uint, Vector3> BaseOffsets = new();

    [Signature("E8 ?? ?? ?? ?? 0F B6 9F ?? ?? ?? ?? 48 8D 8F", DetourName = nameof(CloneActorDetour))]
    private Hook<CloneActor>? cloneActor;

    private bool isDisposing;

    private uint nextUpdateIndex;

    [Signature("E8 ?? ?? ?? ?? 0F 28 74 24 ?? 80 3D", DetourName = nameof(SetDrawOffsetDetour))]
    private Hook<SetDrawOffset>? setDrawOffset;

    [Signature("E8 ?? ?? ?? ?? 83 FE 01 75 0D", DetourName = nameof(SetDrawRotationDetour))]
    private Hook<SetDrawRotation>? setDrawRotationHook;

    [Signature("48 89 5C 24 ?? 48 89 74 24 ?? 57 48 83 EC 20 80 89 ?? ?? ?? ?? ?? 48 8B D9", DetourName = nameof(TerminateCharacterDetour))]
    private Hook<TerminateCharacter>? terminateCharacterHook;

    public Plugin(DalamudPluginInterface pluginInterface) {
#if DEBUG
        IsDebug = true;
#endif
        using var _ = PerformanceMonitors.Run("Plugin Startup");
        pluginInterface.Create<PluginService>();
        
        DoConfigBackup(pluginInterface);

        Config = pluginInterface.GetPluginConfig() as PluginConfig ?? new PluginConfig();
        Config.Initialize();

        PluginService.PluginInterface.UiBuilder.DisableGposeUiHide = Config.ConfigInGpose;
        PluginService.PluginInterface.UiBuilder.DisableCutsceneUiHide = Config.ConfigInCutscene;

        windowSystem = new WindowSystem(Assembly.GetExecutingAssembly().FullName);
        configWindow = new ConfigWindow($"{Name} | Config", this, Config) {
#if DEBUG
            IsOpen = Config.DebugOpenOnStartup
#endif
        };
        windowSystem.AddWindow(configWindow);

        extraDebug = new ExtraDebug(this, Config) { IsOpen = Config.ExtendedDebugOpen };
        windowSystem.AddWindow(extraDebug);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += () => OnCommand(string.Empty, string.Empty);

        PluginService.Commands.AddHandler("/heels", new CommandInfo(OnCommand) { HelpMessage = $"Open the {Name} config window.", ShowInHelp = true });
        
        ApiProvider.Init(this);
        PluginService.Framework.Update += OnFrameworkUpdate;
        PluginService.HoodProvider.InitializeFromAttributes(this);
        setDrawOffset?.Enable();
        cloneActor?.Enable();
        setDrawRotationHook?.Enable();
        terminateCharacterHook?.Enable();
        RequestUpdateAll();

        for (var i = 0U; i < Constants.ObjectLimit; i++) NeedsUpdate[i] = true;
    }

    public string Name => "Simple Heels";

    public static PluginConfig Config { get; private set; }

    public bool[] ManagedIndex { get; } = new bool[Constants.ObjectLimit];
    public static bool[] NeedsUpdate { get; } = new bool[Constants.ObjectLimit];

    private float[] RotationOffsets { get; } = new float[Constants.ObjectLimit];

    public static Dictionary<uint, IpcCharacterConfig> IpcAssignedData { get; } = new();

    public static Dictionary<uint, (string name, ushort homeWorld)> ActorMapping { get; } = new();

    public void Dispose() {
        isDisposing = true;
        PluginService.Log.Verbose("Dispose");
        PluginService.Framework.Update -= OnFrameworkUpdate;

        for (var i = 0U; i < Constants.ObjectLimit; i++)
            if (i == 0 || ManagedIndex[i])
                UpdateObjectIndex(i);

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

        setDrawRotationHook?.Disable();
        setDrawRotationHook?.Dispose();
        setDrawRotationHook = null;

        terminateCharacterHook?.Disable();
        terminateCharacterHook?.Dispose();
        terminateCharacterHook = null;
    }

    private void* TerminateCharacterDetour(Character* character) {
        if (character->GameObject.ObjectIndex < Constants.ObjectLimit) {
            if (ManagedIndex[character->GameObject.ObjectIndex])
                PluginService.Log.Debug($"Managed Character#{character->GameObject.ObjectIndex} Destroyed");
            ManagedIndex[character->GameObject.ObjectIndex] = false;
            BaseOffsets.Remove(character->GameObject.ObjectIndex);
            NeedsUpdate[character->GameObject.ObjectIndex] = false;
        }

        return terminateCharacterHook!.Original(character);
    }

    public bool TryGetCharacterConfig(PlayerCharacter playerCharacter, out CharacterConfig? characterConfig, bool allowIpc = true) {
        var character = (Character*)playerCharacter.Address;
        if (character == null) {
            characterConfig = null;
            return false;
        }

        return TryGetCharacterConfig(character, out characterConfig, allowIpc);
    }

    public bool TryGetCharacterConfig(Character* character, [NotNullWhen(true)] out CharacterConfig? characterConfig, bool allowIpc = true) {
        using var performance = PerformanceMonitors.Run("TryGetCharacterConfig");

        if (allowIpc && character->GameObject.ObjectID != Constants.InvalidObjectId && IpcAssignedData.TryGetValue(character->GameObject.ObjectID, out var ipcCharacterConfig)) {
            characterConfig = ipcCharacterConfig;
            return true;
        }

        string name;
        ushort homeWorld;
        if (ActorMapping.TryGetValue(character->GameObject.ObjectIndex, out var mappedActor)) {
            name = mappedActor.name;
            homeWorld = mappedActor.homeWorld;
        } else {
            name = MemoryHelper.ReadSeString((nint)character->GameObject.Name, 64).TextValue;
            homeWorld = character->HomeWorld;
        }

        if (Config.TryGetCharacterConfig(name, homeWorld, character->GameObject.DrawObject, out characterConfig) && characterConfig != null) return true;

        characterConfig = null;
        return false;
    }

    private void SetDrawOffsetDetour(GameObject* gameObject, float x, float y, float z) {
        try {
            if (gameObject->ObjectIndex < Constants.ObjectLimit) {
                BaseOffsets[gameObject->ObjectIndex] = new Vector3(x, y, z);
                if (ManagedIndex[gameObject->ObjectIndex]) {
                    UpdateObjectIndex(gameObject->ObjectIndex);
                    return;
                }
            }
        } catch (Exception ex) {
            PluginService.Log.Error(ex, "Error within SetDrawOffsetDetour");
        }

        setDrawOffset?.Original(gameObject, x, y, z);
    }

    private void* SetDrawRotationDetour(GameObject* gameObject, float rotation) {
        if (gameObject->ObjectIndex < Constants.ObjectLimit && ManagedIndex[gameObject->ObjectIndex])
            rotation += RotationOffsets[gameObject->ObjectIndex];
        return setDrawRotationHook!.Original(gameObject, rotation);
    }

    private void* CloneActorDetour(Character** destinationArray, Character* source, uint copyFlags) {
        try {
            var destination = destinationArray[1];
            if (destination == null) return cloneActor!.Original(destinationArray, source, copyFlags);
            if (destination->GameObject.ObjectIndex < 200) {
                PluginService.Log.Warning($"Game attempting to clone Actor#{source->GameObject.ObjectIndex} to Actor#{destination->GameObject.ObjectIndex}. Something seems wrong.");
                return cloneActor!.Original(destinationArray, source, copyFlags);
            }

            ActorMapping.Remove(destination->GameObject.ObjectIndex);
            var name = MemoryHelper.ReadSeString(new nint(source->GameObject.GetName()), 64);
            ActorMapping.Add(destination->GameObject.ObjectIndex, (name.TextValue, source->HomeWorld));
            if (destination->GameObject.ObjectIndex < Constants.ObjectLimit && source->GameObject.ObjectIndex < Constants.ObjectLimit) ManagedIndex[destination->GameObject.ObjectIndex] = ManagedIndex[source->GameObject.ObjectIndex];
            PluginService.Log.Verbose($"Game cloned Actor#{source->GameObject.ObjectIndex} to Actor#{destination->GameObject.ObjectIndex} [{name} @ {source->HomeWorld}]");
        } catch (Exception ex) {
            PluginService.Log.Error(ex, "Error handling CloneActor");
        }

        return cloneActor!.Original(destinationArray, source, copyFlags);
    }

    private void DoConfigBackup(DalamudPluginInterface pluginInterface) {
        try {
            var configFile = pluginInterface.ConfigFile;
            if (!configFile.Exists) return;

            var backupDir = Path.Join(configFile.Directory!.Parent!.FullName, "backups", "SimpleHeels");
            var dir = new DirectoryInfo(backupDir);
            if (!dir.Exists) dir.Create();
            if (!dir.Exists) throw new Exception("Backup Directory does not exist");

            var latestFile = new FileInfo(Path.Join(backupDir, "SimpleHeels.latest.json"));

            var needsBackup = false;

            if (latestFile.Exists) {
                var latest = File.ReadAllText(latestFile.FullName);
                var current = File.ReadAllText(configFile.FullName);
                if (current != latest) needsBackup = true;
            } else {
                needsBackup = true;
            }

            if (needsBackup) {
                if (latestFile.Exists) {
                    var t = latestFile.LastWriteTime;
                    File.Move(latestFile.FullName, Path.Join(backupDir, $"SimpleHeels.{t.Year}{t.Month:00}{t.Day:00}{t.Hour:00}{t.Minute:00}{t.Second:00}.json"));
                }

                File.Copy(configFile.FullName, latestFile.FullName);
                var allBackups = dir.GetFiles().Where(f => f.Name.StartsWith("SimpleHeels.2") && f.Name.EndsWith(".json")).OrderBy(f => f.LastWriteTime.Ticks).ToList();
                if (allBackups.Count > 10) {
                    PluginService.Log.Debug($"Removing Oldest Backup: {allBackups[0].FullName}");
                    File.Delete(allBackups[0].FullName);
                }
            }
        } catch (Exception exception) {
            PluginService.Log.Warning(exception, "Backup Skipped");
        }
    }

    public static void RequestUpdateAll() {
        for (var i = 0U; i < Constants.ObjectLimit; i++) NeedsUpdate[i] = true;
        _updateAll = true;
    }

    private bool UpdateObjectIndex(uint updateIndex) {
        if (updateIndex >= Constants.ObjectLimit) return true;
        NeedsUpdate[updateIndex] = false;

        var obj = GameObjectManager.GetGameObjectByIndex((int)updateIndex);
        if (isDisposing || obj == null || !obj->IsCharacter() || Config.Enabled == false) {
            if (obj != null && ManagedIndex[updateIndex] && BaseOffsets.TryGetValue(obj->ObjectIndex, out var baseOffset)) setDrawOffset!.Original(obj, baseOffset.X, baseOffset.Y, baseOffset.Z);
            ManagedIndex[updateIndex] = false;
            BaseOffsets.Remove(updateIndex);
            return false;
        }

        var character = (Character*)obj;
        if (updateIndex < 200 && character->ReaperShroud.Flags != 0) return true; // Ignore all changes when Reaper Shroud is active.

        using var performance = PerformanceMonitors.Run("UpdateObject");
        using var performance2 = PerformanceMonitors.Run($"UpdateObject:{updateIndex}", Config.DetailedPerformanceLogging);

        if (!TryGetCharacterConfig(character, out var characterConfig)) return false;

        if (!characterConfig.TryGetFirstMatch(character, out var offsetProvider)) return false;

        if (!BaseOffsets.TryGetValue(updateIndex, out var offset)) {
            var baseOffset = new Vector3(character->GameObject.DrawOffset.X, character->GameObject.DrawOffset.Y, character->GameObject.DrawOffset.Z);
            BaseOffsets[updateIndex] = baseOffset;
            offset = baseOffset;
        }

        var appliedOffset = offsetProvider.GetOffset();
        offset += appliedOffset;

        setDrawOffset!.Original(obj, offset.X, offset.Y, offset.Z);
        ManagedIndex[obj->ObjectIndex] = true;
        RotationOffsets[obj->ObjectIndex] = offsetProvider.GetRotation();

        if (updateIndex == 0) ApiProvider.UpdateLocal(appliedOffset, RotationOffsets[obj->ObjectIndex]);
        return true;
    }

    private void OnFrameworkUpdate(IFramework framework) {
        if (!PluginService.Condition.Any()) return;
        using var frameworkPerformance = PerformanceMonitors.Run("Framework Update");
        if (_updateAll) {
            _updateAll = false;
            for (var i = 0U; i < Constants.ObjectLimit; i++) UpdateObjectIndex(i);

            return;
        }

        if (!Config.Enabled) return;

        if (!PluginService.Condition[ConditionFlag.InCombat]) {
            var throttle = 10;
            while (throttle-- > 0) {
                nextUpdateIndex %= Constants.ObjectLimit;
                var updateIndex = nextUpdateIndex++;
                if (updateIndex != 0 && !ManagedIndex[updateIndex])
                    if (UpdateObjectIndex(updateIndex))
                        break;
            }
        }

        for (var i = 0U; i < Constants.ObjectLimit; i++)
            if (i == 0 || ManagedIndex[i])
                UpdateObjectIndex(i);
    }

    private void OnCommand(string command, string args) {
        switch (args.ToLowerInvariant()) {
            case "debug":
                IsDebug = !IsDebug;
                return;
            case "debug2":
                extraDebug.Toggle();
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
                configWindow.ToggleWithWarning();
                break;
        }
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

    private static float? CheckModelSlot(Human* human, ModelSlot slot) {
        var modelArray = human->CharacterBase.Models;
        if (modelArray == null) return null;
        var feetModel = modelArray[(byte)slot];
        if (feetModel == null) return null;
        var modelResource = feetModel->ModelResourceHandle;
        if (modelResource == null) return null;

        foreach (var attr in modelResource->Attributes) {
            var str = MemoryHelper.ReadStringNullTerminated(new nint(attr.Item1.Value));
            if (str.StartsWith("heels_offset=", StringComparison.OrdinalIgnoreCase)) {
                if (float.TryParse(str[13..].Replace(',', '.'), CultureInfo.InvariantCulture, out var offsetAttr)) return offsetAttr * human->CharacterBase.DrawObject.Object.Scale.Y;
            } else if (str.StartsWith("heels_offset_", StringComparison.OrdinalIgnoreCase)) {
                var valueStr = str[13..].Replace("n_", "-").Replace('a', '0').Replace('b', '1').Replace('c', '2').Replace('d', '3').Replace('e', '4').Replace('f', '5').Replace('g', '6').Replace('h', '7').Replace('i', '8').Replace('j', '9').Replace('_', '.');

                if (float.TryParse(valueStr, CultureInfo.InvariantCulture, out var value)) return value * human->CharacterBase.DrawObject.Object.Scale.Y;
            }
        }

        return null;
    }

    private static float? GetOffsetFromModels(Human* human) {
        if (human == null) return null;
        if (Config.UseModelOffsets) {
            using var useModelOffsetsPerformance = PerformanceMonitors.Run("GetOffsetFromModels");

            return CheckModelSlot(human, ModelSlot.Top) ?? CheckModelSlot(human, ModelSlot.Legs) ?? CheckModelSlot(human, ModelSlot.Feet) ?? null;
        }

        return null;
    }

    public static bool TryGetOffsetFromModels(Human* human, [NotNullWhen(true)] out float? offset) {
        offset = GetOffsetFromModels(human);
        return offset != null;
    }

    private delegate void SetDrawOffset(GameObject* gameObject, float x, float y, float z);

    private delegate void* CloneActor(Character** destination, Character* source, uint a3);

    private delegate void* SetDrawRotation(GameObject* gameObject, float rotation);

    private delegate void* TerminateCharacter(Character* character);
}
