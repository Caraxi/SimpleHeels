﻿using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Text.RegularExpressions;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.Memory;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using World = Lumina.Excel.GeneratedSheets2.World;

namespace SimpleHeels;

public unsafe class Plugin : IDalamudPlugin {
    internal static bool IsDebug;
    private static bool _updateAll;

    private readonly ConfigWindow configWindow;
    private readonly ExtraDebug extraDebug;
    private readonly WindowSystem windowSystem;
    private readonly TempOffsetOverlay tempOffsetOverlay;

    public Dictionary<uint, Vector3> BaseOffsets = new();

    [Signature("E8 ?? ?? ?? ?? 0F B6 9E ?? ?? ?? ?? 48 8D 8E", DetourName = nameof(CloneActorDetour))]
    private Hook<CloneActor>? cloneActor;

    private bool isDisposing;

    private uint nextUpdateIndex;

    [Signature("E8 ?? ?? ?? ?? 0F 28 74 24 ?? 80 3D", DetourName = nameof(SetDrawOffsetDetour))]
    private Hook<SetDrawOffset>? setDrawOffset;

    [Signature("E8 ?? ?? ?? ?? 83 FE 01 75 0D", DetourName = nameof(SetDrawRotationDetour))]
    private Hook<SetDrawRotation>? setDrawRotationHook;

    [Signature("48 89 5C 24 ?? 57 48 83 EC 20 80 89", DetourName = nameof(TerminateCharacterDetour))]
    private Hook<TerminateCharacter>? terminateCharacterHook;

    [Signature("E8 ?? ?? ?? ?? 45 84 FF 75 40", DetourName = nameof(SetModeDetour))]
    private Hook<SetMode>? setModeHook;

    public Plugin(IDalamudPluginInterface pluginInterface) {
#if DEBUG
        IsDebug = true;
#endif
        using var _ = PerformanceMonitors.Run($"Plugin Startup");
        pluginInterface.Create<PluginService>();
        
        PluginService.Log.Information($"Starting SimpleHeels - D: {Util.GetGitHash()}- CS: {FFXIVClientStructs.ThisAssembly.Git.Commit}[{FFXIVClientStructs.ThisAssembly.Git.Commits}]");
        
        
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

        tempOffsetOverlay = new TempOffsetOverlay($"{Name} | Temp Offset", this, Config);
        windowSystem.AddWindow(tempOffsetOverlay);

        pluginInterface.UiBuilder.Draw += windowSystem.Draw;
        pluginInterface.UiBuilder.OpenConfigUi += () => OnCommand(string.Empty, string.Empty);

        PluginService.Commands.AddHandler("/heels", new CommandInfo(OnCommand) { 
            HelpMessage = $"Open the {Name} config window.\n" +
            "/heels renamechar \"<source char name>|<source world>\" \"<target char name>|<target world>\" → Rename existing character config to new character config", ShowInHelp = true 
        });
        
        ApiProvider.Init(this);
        PluginService.Framework.Update += OnFrameworkUpdate;
        PluginService.HoodProvider.InitializeFromAttributes(this);
        setDrawOffset?.Enable();
        cloneActor?.Enable();
        setDrawRotationHook?.Enable();
        terminateCharacterHook?.Enable();
        setModeHook?.Enable();
        RequestUpdateAll();

        for (var i = 0U; i < Constants.ObjectLimit; i++) NeedsUpdate[i] = true;
    }

    public string Name => "Simple Heels";

    public static PluginConfig Config { get; private set; } = new();

    public bool[] ManagedIndex { get; } = new bool[Constants.ObjectLimit];
    public static bool[] NeedsUpdate { get; } = new bool[Constants.ObjectLimit];

    public static TempOffset?[] TempOffsets { get; } = new TempOffset?[Constants.ObjectLimit];
    public static EmoteIdentifier?[] TempOffsetEmote { get; } = new EmoteIdentifier?[Constants.ObjectLimit];
    
    public static Dictionary<EmoteIdentifier, TempOffset> PreviousTempOffsets { get; } = new();

    private float[] RotationOffsets { get; } = new float[Constants.ObjectLimit];
    private PitchRoll[] PitchRolls { get; } = new PitchRoll[Constants.ObjectLimit];

    private byte[] SetGposeRotationCounter { get; } = new byte[Constants.ObjectLimit];

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
        
        setModeHook?.Disable();
        setModeHook?.Dispose();
        setModeHook = null;
    }

    private void* TerminateCharacterDetour(Character* character) {
        if (character->GameObject.ObjectIndex < Constants.ObjectLimit) {
            if (ManagedIndex[character->GameObject.ObjectIndex])
                PluginService.Log.Debug($"Managed Character#{character->GameObject.ObjectIndex} Destroyed");
            ManagedIndex[character->GameObject.ObjectIndex] = false;
            BaseOffsets.Remove(character->GameObject.ObjectIndex);
            NeedsUpdate[character->GameObject.ObjectIndex] = false;
            TempOffsets[character->GameObject.ObjectIndex] = null;
        }

        return terminateCharacterHook!.Original(character);
    }

    private void* SetModeDetour(Character* character, CharacterModes mode, byte modeParam) {
        var previousMode = character == null ? CharacterModes.None : character->Mode;
        try {
            return setModeHook!.Original(character, mode, modeParam);
        } finally {
            try {
                var m = mode;
                if (character->GameObject.ObjectIndex == 0 && (m is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop || previousMode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop)) {
                    ApiProvider.ForceUpdateLocal();
                }
            } catch (Exception ex) {
                PluginService.Log.Error(ex, "Error handling SetMode");
            }
        }
    }

    public bool TryGetCharacterConfig(IPlayerCharacter playerCharacter, out CharacterConfig? characterConfig, bool allowIpc = true) {
        var character = (Character*)playerCharacter.Address;
        if (character == null) {
            characterConfig = null;
            return false;
        }

        return TryGetCharacterConfig(character, out characterConfig, allowIpc);
    }

    public bool TryGetCharacterConfig(Character* character, [NotNullWhen(true)] out CharacterConfig? characterConfig, bool allowIpc = true) {
        using var performance = PerformanceMonitors.Run("TryGetCharacterConfig");

        if (allowIpc && character->GameObject.EntityId != Constants.InvalidObjectId && IpcAssignedData.TryGetValue(character->GameObject.EntityId, out var ipcCharacterConfig)) {
            characterConfig = ipcCharacterConfig;
            return true;
        }

        string name;
        ushort homeWorld;
        if (ActorMapping.TryGetValue(character->GameObject.ObjectIndex, out var mappedActor)) {
            name = mappedActor.name;
            homeWorld = mappedActor.homeWorld;
        } else {
            name = SeString.Parse(character->GameObject.Name).TextValue;
            homeWorld = character->HomeWorld;
        }

        if (Config.TryGetCharacterConfig(name, homeWorld, character->GameObject.DrawObject, out characterConfig) && characterConfig != null) return true;

        characterConfig = null;
        return false;
    }

    public bool TryGetMinionCharacterConfig(GameObject* minion, [NotNullWhen(true)] out CharacterConfig? characterConfig) {
        using var performance = PerformanceMonitors.Run("TryGetMinionCharacterConfig");
        if (minion == null) {
            characterConfig = null;
            return false;
        }
        
        string name;
        if (ActorMapping.TryGetValue(minion->ObjectIndex, out var mappedActor)) {
            name = mappedActor.name;
        } else {
            name = SeString.Parse(minion->Name).TextValue;
        }
        
        
        if (Config.TryGetCharacterConfig(name, ushort.MaxValue, minion->DrawObject, out characterConfig) && characterConfig != null) return true;

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

        setDrawOffset!.Original(gameObject, x, y, z);
    }

    private void* SetDrawRotationDetour(GameObject* gameObject, float rotation) {
        if (gameObject->ObjectIndex >= Constants.ObjectLimit || ManagedIndex[gameObject->ObjectIndex] == false) {
            return setDrawRotationHook!.Original(gameObject, rotation);
        }

        try {
            rotation += RotationOffsets[gameObject->ObjectIndex];
            return setDrawRotationHook!.Original(gameObject, rotation);
        } finally {
            if (gameObject->DrawObject != null) {

                if (!PluginService.ClientState.IsGPosing || SetGposeRotationCounter[gameObject->ObjectIndex] > 0) {
                    if (SetGposeRotationCounter[gameObject->ObjectIndex] > 0) SetGposeRotationCounter[gameObject->ObjectIndex] -= 1;
                    var t = FFXIVClientStructs.FFXIV.Common.Math.Quaternion.CreateFromYawPitchRoll(gameObject->Rotation + rotation, PitchRolls[gameObject->ObjectIndex].Pitch, PitchRolls[gameObject->ObjectIndex].Roll);
                    gameObject->DrawObject->Rotation = t;
                }
                
                
            }
        }
    }

    private void* CloneActorDetour(Character** destinationArray, Character* source, uint copyFlags) {
        try {
            var destination = destinationArray[1];
            if (destination == null) return cloneActor!.Original(destinationArray, source, copyFlags);
            if (destination->GameObject.ObjectIndex < 200) {
                return cloneActor!.Original(destinationArray, source, copyFlags);
            }

            ActorMapping.Remove(destination->GameObject.ObjectIndex);
            var name = MemoryHelper.ReadSeString(new nint(source->GameObject.GetName()), 64);
            ActorMapping.Add(destination->GameObject.ObjectIndex, (name.TextValue, source->HomeWorld));
            if (destination->GameObject.ObjectIndex < Constants.ObjectLimit && source->GameObject.ObjectIndex < Constants.ObjectLimit) {
                ManagedIndex[destination->GameObject.ObjectIndex] = ManagedIndex[source->GameObject.ObjectIndex];
                if (IpcAssignedData.TryGetValue(source->GameObject.EntityId, out var ipcCharacterConfig)) {
                    TempOffsets[destination->GameObject.ObjectIndex] = ipcCharacterConfig.TempOffset;
                    TempOffsetEmote[destination->GameObject.ObjectIndex] = ipcCharacterConfig.TempOffset == null ? null : EmoteIdentifier.Get(source);
                } else {
                    TempOffsets[destination->GameObject.ObjectIndex] = TempOffsets[source->GameObject.ObjectIndex];
                    TempOffsetEmote[destination->GameObject.ObjectIndex] = TempOffsetEmote[source->GameObject.ObjectIndex];
                }

                PitchRolls[destination->GameObject.ObjectIndex] = PitchRolls[source->GameObject.ObjectIndex];
                SetGposeRotationCounter[destination->GameObject.ObjectIndex] = 60;
                
                destination->GameObject.DrawOffset = source->GameObject.DrawOffset;
                if (BaseOffsets.TryGetValue(source->GameObject.ObjectIndex, out var baseOffset)) {
                    BaseOffsets[destination->GameObject.ObjectIndex] = baseOffset;
                }
            }
            PluginService.Log.Verbose($"Game cloned Actor#{source->GameObject.ObjectIndex} to Actor#{destination->GameObject.ObjectIndex} [{name} @ {source->HomeWorld}]");
        } catch (Exception ex) {
            PluginService.Log.Error(ex, "Error handling CloneActor");
        }

        return cloneActor!.Original(destinationArray, source, copyFlags);
    }

    private void DoConfigBackup(IDalamudPluginInterface pluginInterface) {
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

    private bool UpdateCompanion(uint updateIndex, GameObject* obj) {
        if (!Config.ApplyToMinions) return false;
        
        using var performance = PerformanceMonitors.Run("UpdateCompanionObject");
        using var performance2 = PerformanceMonitors.Run("UpdateObject");
        using var performance3 = PerformanceMonitors.Run($"UpdateObject:{updateIndex}", Config.DetailedPerformanceLogging);
        
        
        IOffsetProvider? offsetProvider = null;
        
        if (!IpcAssignedData.ContainsKey(obj->EntityId) && TempOffsets[updateIndex] != null) {
            offsetProvider = TempOffsets[updateIndex];
        }
        
        if (offsetProvider == null) {
            if (!TryGetMinionCharacterConfig(obj, out var characterConfig)) return false;
            if (!characterConfig.TryGetFirstMatch(obj, out offsetProvider, false)) return false;
        }
        
        if (!BaseOffsets.TryGetValue(updateIndex, out var offset)) {
            var baseOffset = new Vector3(obj->DrawOffset.X, obj->DrawOffset.Y, obj->DrawOffset.Z);
            BaseOffsets[updateIndex] = baseOffset;
            offset = baseOffset;
        }

        var appliedOffset = offsetProvider.GetOffset();
        offset += appliedOffset;

        if (Vector3.Distance(offset, obj->DrawOffset) > Constants.FloatDelta) {
            using (PerformanceMonitors.Run($"Set Offset:{updateIndex}", Config.DetailedPerformanceLogging))
            using (PerformanceMonitors.Run("Set Offset")) {
                setDrawOffset!.Original(obj, offset.X, offset.Y, offset.Z);
            }
        }

        ManagedIndex[obj->ObjectIndex] = true;
        RotationOffsets[obj->ObjectIndex] = offsetProvider.GetRotation();
        PitchRolls[obj->ObjectIndex] = offsetProvider.GetPitchRoll();
        
        return false;
    }

    private bool ObjectIsCompanionTurnedHuman(GameObject* gameObject) {
        if (gameObject == null) return false;
        if (gameObject->ObjectKind != ObjectKind.Companion) return false;
        if (gameObject->DrawObject == null) return false;
        if (gameObject->DrawObject->Object.GetObjectType() != ObjectType.CharacterBase) return false;
        var chrBase = (CharacterBase*)gameObject->DrawObject;
        return chrBase->GetModelType() == CharacterBase.ModelType.Human;
    }
    
    
    private bool UpdateObjectIndex(uint updateIndex) {
        if (setDrawOffset == null) return true;
        if (updateIndex >= Constants.ObjectLimit) return true;
        NeedsUpdate[updateIndex] = false;

        var obj = GameObjectManager.Instance()->Objects.IndexSorted[(int)updateIndex].Value;

        if (Config is { Enabled: true, ApplyToMinions: true } && ObjectIsCompanionTurnedHuman(obj)) 
            return UpdateCompanion(updateIndex, obj);

        bool ReleaseControl(bool r) {
            if (obj != null && ManagedIndex[updateIndex] && BaseOffsets.TryGetValue(obj->ObjectIndex, out var baseOffset)) setDrawOffset!.Original(obj, baseOffset.X, baseOffset.Y, baseOffset.Z);
            ManagedIndex[updateIndex] = false;
            BaseOffsets.Remove(updateIndex);
            RotationOffsets[updateIndex] = 0;
            PitchRolls[updateIndex] = PitchRoll.Zero;
            if (updateIndex == 0) ApiProvider.UpdateLocal(Vector3.Zero, 0, PitchRoll.Zero);
            return r;
        }

        if (isDisposing || obj == null || !obj->IsCharacter() || Config.Enabled == false) {
            return ReleaseControl(false);
        }

        var character = (Character*)obj;
        if (updateIndex < 200 && character->ReaperShroud.Flags != 0) return true; // Ignore all changes when Reaper Shroud is active.

        using var performance = PerformanceMonitors.Run("UpdateObject");
        using var performance2 = PerformanceMonitors.Run($"UpdateObject:{updateIndex}", Config.DetailedPerformanceLogging);

        if (Config is { Enabled: true } && character->CompanionData.CompanionObject != null) {
            var companion = (GameObject*) character->CompanionData.CompanionObject;
            if (companion->DrawObject != null) {
                if (updateIndex != 0 && Utils.StaticMinions.Value.Contains(companion->BaseId) && IpcAssignedData.TryGetValue(obj->EntityId, out var ipc) && ipc.MinionPosition != null) {
                    companion->DrawObject->Object.Position.X = ipc.MinionPosition.X;
                    companion->DrawObject->Object.Position.Y = ipc.MinionPosition.Y;
                    companion->DrawObject->Object.Position.Z = ipc.MinionPosition.Z;
                    companion->DrawObject->Object.Rotation = Quaternion.CreateFromYawPitchRoll(ipc.MinionPosition.R, 0, 0);
                }
            
                if (Config.ApplyStaticMinionPositions && updateIndex == 0 && Utils.StaticMinions.Value.Contains(companion->BaseId)) {
                    ApiProvider.UpdateMinion(companion->DrawObject->Object.Position, companion->DrawObject->Object.Rotation.EulerAngles.Y * MathF.PI / 180f);
                }
            }
        }

        IOffsetProvider? offsetProvider = null;
        
        if (!IpcAssignedData.ContainsKey(obj->EntityId) && TempOffsets[updateIndex] != null) {
            var emote = EmoteIdentifier.Get(character);
            var tEmote = TempOffsetEmote[updateIndex];
            if (TempOffsetEmote[updateIndex] == emote || (emote != null && tEmote != null && emote.EmoteModeId == tEmote.EmoteModeId)) {
                offsetProvider = TempOffsets[updateIndex];
            } else {
                PluginService.Log.Verbose($"Clearing Temp Offset for Object#{updateIndex} - Emote Changed");
                if (obj->ObjectIndex == 0 && TempOffsets[updateIndex] != null && TempOffsetEmote[updateIndex] != null) {
                    var emoteIndex = TempOffsetEmote[updateIndex];
                    var o = TempOffsets[updateIndex];
                    if (emoteIndex != null && o != null) {
                        PreviousTempOffsets[emoteIndex] = o;
                    }
                }
                
                TempOffsets[updateIndex] = null;
                TempOffsetEmote[updateIndex] = null;
            }
        }
        
        if (offsetProvider == null) {
            if (!TryGetCharacterConfig(character, out var characterConfig)) return ReleaseControl(false);
            if (!characterConfig.TryGetFirstMatch(character, out offsetProvider)) return ReleaseControl(false);
        }
        
        if (!BaseOffsets.TryGetValue(updateIndex, out var offset)) {
            var baseOffset = new Vector3(character->GameObject.DrawOffset.X, character->GameObject.DrawOffset.Y, character->GameObject.DrawOffset.Z);
            BaseOffsets[updateIndex] = baseOffset;
            offset = baseOffset;
        }

        var appliedOffset = offsetProvider.GetOffset();
        offset += appliedOffset;
        if (updateIndex != 0 && Config.UsePrecisePositioning && character->Mode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop && IpcAssignedData.TryGetValue(obj->EntityId, out var ipcCharacter) && ipcCharacter.EmotePosition != null) {
            using (PerformanceMonitors.Run($"Calculate Precise Position Offset:{updateIndex}", Config.DetailedPerformanceLogging))
            using (PerformanceMonitors.Run("Calculate Precise Position Offset")) {
                var pos = (Vector3) character->GameObject.Position;
                var emotePos = ipcCharacter.EmotePosition.GetOffset();
                if (Vector3.Distance(pos, emotePos) is > Constants.FloatDelta and < 1f ) {
                    PluginService.Log.Debug($"Apply Precise Position to Object#{updateIndex}");
                    character->GameObject.SetPosition(emotePos.X, emotePos.Y, emotePos.Z);
                }

                /*
                var rot = character->GameObject.Rotation;
                var emoteRot = ipcCharacter.EmotePosition.GetRotation();
                if (MathF.Abs(rot - emoteRot) > Constants.FloatDelta) {
                    PluginService.Log.Debug($"Apply Precise Rotation to Object#{updateIndex}");
                    character->GameObject.SetRotation(emoteRot);
                }
                */

            }
        }

        if (Vector3.Distance(offset, obj->DrawOffset) > Constants.FloatDelta) {
            using (PerformanceMonitors.Run($"Set Offset:{updateIndex}", Config.DetailedPerformanceLogging))
            using (PerformanceMonitors.Run("Set Offset")) {
                setDrawOffset!.Original(obj, offset.X, offset.Y, offset.Z);
            }
        }
        
        ManagedIndex[obj->ObjectIndex] = true;
        RotationOffsets[obj->ObjectIndex] = offsetProvider.GetRotation();
        PitchRolls[obj->ObjectIndex] = offsetProvider.GetPitchRoll();
        

        if (updateIndex == 0) ApiProvider.UpdateLocal(appliedOffset, RotationOffsets[obj->ObjectIndex], PitchRolls[obj->ObjectIndex]);
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
        var splitArgs = Regex.Matches(args, @"[\""].+?[\""]|[^ ]+")
            .Cast<Match>()
            .Select(m => m.Value)
            .ToList();
        if (splitArgs.Count > 0) {
            switch (splitArgs[0]
                        .ToLowerInvariant()) {
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
                case "temp":
                    Config.TempOffsetWindowOpen = !Config.TempOffsetWindowOpen;
                    break;
                case "renamechar":
                    if (splitArgs.Count != 3) {
                        break;
                    }

                    var sourceChar = splitArgs[1]
                        .Replace("\"", String.Empty)
                        .Split("|", StringSplitOptions.RemoveEmptyEntries);
                    var targetChar = splitArgs[2]
                        .Replace("\"", String.Empty)
                        .Split("|", StringSplitOptions.RemoveEmptyEntries);
                    if (sourceChar.Length != 2 || targetChar.Length != 2 || sourceChar.SequenceEqual(targetChar)) {
                        break;
                    }

                    var sourcename = sourceChar[0];
                    var sourceworld = PluginService.Data.GetExcelSheet<World>()
                        ?.FirstOrDefault(w => w.Name == sourceChar[1]);
                    var targetname = targetChar[0];
                    var targetworld = PluginService.Data.GetExcelSheet<World>()
                        ?.FirstOrDefault(w => w.Name == targetChar[1]);
                    if (targetworld == null || sourceworld == null) {
                        break;
                    }

                    var newAlreadyExists = Config.WorldCharacterDictionary.ContainsKey(targetworld.RowId) && Config.WorldCharacterDictionary[targetworld.RowId]
                        .ContainsKey(targetname);
                    var oldAlreadyExists = Config.WorldCharacterDictionary.ContainsKey(sourceworld.RowId) && Config.WorldCharacterDictionary[sourceworld.RowId]
                        .ContainsKey(sourcename);
                    if (newAlreadyExists || !oldAlreadyExists || !Config.TryAddCharacter(targetname, targetworld.RowId)) {
                        break;
                    }

                    Config.WorldCharacterDictionary[targetworld.RowId][targetname] = Config.WorldCharacterDictionary[sourceworld.RowId][sourcename];
                    Config.WorldCharacterDictionary[sourceworld.RowId]
                        .Remove(sourcename);
                    if (Config.WorldCharacterDictionary[sourceworld.RowId].Count == 0) {
                        Config.WorldCharacterDictionary.Remove(sourceworld.RowId);
                    }

                    break;
                default:
                    configWindow.ToggleWithWarning();
                    break;
            }
        } else {
            configWindow.ToggleWithWarning();
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
        return System.Text.Encoding.UTF8.GetString(modelResource->ResourceHandle.FileName.AsSpan());
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
    private delegate void* SetMode(Character* character, CharacterModes mode, byte modeParam);
}
