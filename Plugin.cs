using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;
using System.Threading;
using Dalamud.Game;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Command;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Hooking;
using Dalamud.Interface.Windowing;
using Dalamud.Memory;
using Dalamud.Plugin;
using Dalamud.Plugin.Services;
using Dalamud.Utility;
using Dalamud.Utility.Signatures;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using Lumina.Extensions;
using Newtonsoft.Json;
using Companion = FFXIVClientStructs.FFXIV.Client.Game.Character.Companion;
using World = Lumina.Excel.Sheets.World;

namespace SimpleHeels;

public unsafe class Plugin : IDalamudPlugin {
    public static CancellationTokenSource CancellationTokenSource { get; private set; } = new CancellationTokenSource();
    internal static bool IsDebug;
    private static bool _updateAll;

    private readonly ConfigWindow configWindow;
    private readonly ExtraDebug extraDebug;
    private readonly WindowSystem windowSystem;
    private readonly TempOffsetOverlay tempOffsetOverlay;

    public Dictionary<uint, Vector3> BaseOffsets = new();

    [Signature("E8 ?? ?? ?? ?? 8B 87 ?? ?? ?? ?? 85 C0 74 24", DetourName = nameof(CloneActorDetour))]
    private Hook<CloneActor>? cloneActor;

    private bool isDisposing;

    private uint nextUpdateIndex;

    [Signature("E8 ?? ?? ?? ?? 0F 28 74 24 ?? 80 3D", DetourName = nameof(SetDrawOffsetDetour))]
    private Hook<SetDrawOffset>? setDrawOffset;

    [Signature("E8 ?? ?? ?? ?? 83 FE 01 75 0D", DetourName = nameof(SetDrawRotationDetour))]
    private Hook<SetDrawRotation>? setDrawRotationHook;

    [Signature("48 89 5C 24 ?? 57 48 83 EC 20 80 89 ?? ?? ?? ?? ?? 48 8B D9", DetourName = nameof(TerminateCharacterDetour))]
    private Hook<TerminateCharacter>? terminateCharacterHook;

    [Signature("E8 ?? ?? ?? ?? 45 84 FF 75 40", DetourName = nameof(SetModeDetour))]
    private Hook<SetMode>? setModeHook;

    [Signature("E8 ?? ?? ?? ?? E9 ?? ?? ?? ?? 48 8B 59 70", DetourName = nameof(UpdateMountedPositionsDetour))]
    private Hook<UpdateMountedPositions>? updateMountedPositionsHook;

    [StructLayout(LayoutKind.Explicit, Size = 0x78)]
    public struct Attach {
        [FieldOffset(0x50)] public uint AttachType;
        [FieldOffset(0x58)] public Skeleton* Skeleton;
        [FieldOffset(0x60)] public DrawObject* AttachParentDrawObject;
    }
    
    private void* UpdateMountedPositionsDetour(Attach* attach) {
        try {
            return updateMountedPositionsHook!.Original(attach);
        } finally {
            if (!(attach == null || attach->AttachParentDrawObject == null || attach->Skeleton == null)) {
                for (var i = 0; i < Constants.ObjectLimit; i++) {
                    var obj = GameObjectManager.Instance()->Objects.IndexSorted[i].Value;
                    if (obj == null || obj->DrawObject == null) continue;
                    if (obj->DrawObject->GetObjectType() != ObjectType.CharacterBase) continue;
                    var chrBase = (CharacterBase*)obj->DrawObject;
                    if (chrBase->Skeleton != attach->Skeleton) continue;
                    var tempOffset = TempOffsets[i];
                    if (tempOffset == null || TempOffsetEmote[i] == null) break;
                    if (TempOffsetEmote[i]?.EmoteModeId != EmoteIdentifier.MountedFakeEmoteId) break;
                    var baseRotation = chrBase->Skeleton->Transform.Rotation * FFXIVClientStructs.FFXIV.Common.Math.Quaternion.CreateFromYawPitchRoll(tempOffset.R, 0, 0).Normalized;
                    chrBase->Skeleton->Transform.Rotation *= FFXIVClientStructs.FFXIV.Common.Math.Quaternion.CreateFromYawPitchRoll(tempOffset.R, tempOffset.Pitch, tempOffset.Roll);
                    var baseOffset = new Vector3(tempOffset.X, tempOffset.Y, tempOffset.Z);
                    var offset = baseRotation * baseOffset;
                    chrBase->Skeleton->Transform.Position += offset;
                }
            }
        }
    }
    
    public Plugin(IDalamudPluginInterface pluginInterface) {
        CancellationTokenSource = new CancellationTokenSource();
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
        updateMountedPositionsHook?.Enable();
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

    public static Dictionary<uint, Dictionary<string, string>> Tags { get; } = new();

    public void Dispose() {
        CancellationTokenSource.Cancel();
        isDisposing = true;

        if (_isMinionAdjusted) {
            var pObj = GameObjectManager.Instance()->Objects.IndexSorted[0].Value;
            if (pObj != null && pObj->IsCharacter() ) {
                var pChr = (Character*)pObj;
                if (pChr->CompanionObject != null) {
                    var go =  pChr->CompanionObject;
                    if (go->DrawObject != null) {
                        go->DrawObject->Rotation = FFXIVClientStructs.FFXIV.Common.Math.Quaternion.CreateFromYawPitchRoll(go->Rotation, 0, 0);
                    }
                    
                    go->Effects.TiltParam1Value = 0;
                    go->Effects.TiltParam2Value = 0;
                }
            }
        }
        
        PluginService.Log.Verbose("Dispose");
        PluginService.Framework.Update -= OnFrameworkUpdate;

        for (var i = 0U; i < Constants.ObjectLimit; i++)
            if (i == 0 || ManagedIndex[i])
                UpdateObjectIndex(i);

        ApiProvider.DeInit();
        PluginService.Commands.RemoveHandler("/heels");
        windowSystem.RemoveAllWindows();

        SaveConfig();

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
        
        updateMountedPositionsHook?.Disable();
        updateMountedPositionsHook?.Dispose();
        updateMountedPositionsHook = null;
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
                if (character->GameObject.ObjectIndex == 0 && (m is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop or CharacterModes.Mounted or CharacterModes.RidingPillion|| previousMode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop or CharacterModes.Mounted or CharacterModes.RidingPillion)) {
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

        if (character->GameObject.ObjectIndex == 0 && Config.IdentifyAs.TryGetValue(PluginService.ClientState.LocalContentId, out var identity)) {
            name = identity.Item1;
            homeWorld = (ushort) identity.Item2;
        } else  if (ActorMapping.TryGetValue(character->GameObject.ObjectIndex, out var mappedActor)) {
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
        if (!AllowAdvancedPositioning()) {
            return setDrawRotationHook!.Original(gameObject, rotation);;
        }
        
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

    private bool AllowAdvancedPositioning() {
        if (PluginService.Condition.Any(ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78, ConditionFlag.OccupiedInCutSceneEvent)) return false;

        return true;
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
            var companion = character->CompanionData.CompanionObject;
            if (companion->DrawObject != null) {
                if (updateIndex != 0 && Utils.StaticMinions.Value.Contains(companion->BaseId) && IpcAssignedData.TryGetValue(obj->EntityId, out var ipc) && ipc.MinionPosition != null) {
                    companion->DrawObject->Object.Position.X = ipc.MinionPosition.X;
                    companion->DrawObject->Object.Position.Y = ipc.MinionPosition.Y;
                    companion->DrawObject->Object.Position.Z = ipc.MinionPosition.Z;
                    companion->DrawObject->Object.Rotation = Quaternion.CreateFromYawPitchRoll(ipc.MinionPosition.R, ipc.MinionPosition.Pitch, ipc.MinionPosition.Roll);
                }
            
                if (Config.ApplyStaticMinionPositions && updateIndex == 0 && Utils.StaticMinions.Value.Contains(companion->BaseId)) {
                    UpdateCompanionRotation(companion);
                    if (_isMinionAdjusted) {
                        ApiProvider.UpdateMinion(companion->DrawObject->Object.Position, companion->DrawObject->Object.Rotation.EulerAngles.Y * MathF.PI / 180f, companion->Effects.TiltParam1Value, companion->Effects.TiltParam2Value);
                    } else {
                        ApiProvider.UpdateMinion(companion->DrawObject->Object.Position, companion->DrawObject->Object.Rotation.EulerAngles.Y * MathF.PI / 180f, 0, 0);
                    }
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
        if (!AllowAdvancedPositioning()) appliedOffset = appliedOffset with { X = 0, Z = 0 };
        offset += appliedOffset;
        if (updateIndex != 0 && Config.UsePrecisePositioning && character->Mode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop && IpcAssignedData.TryGetValue(obj->EntityId, out var ipcCharacter) && ipcCharacter.EmotePosition != null && AllowAdvancedPositioning()) {
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

    private void DoEmoteSync() {
        foreach (var c in PluginService.Objects.Where(o => o is IPlayerCharacter)) {
            var character = (Character*)c.Address;
            if (character->DrawObject == null) continue;
            if (character->DrawObject->GetObjectType() != ObjectType.CharacterBase) continue;
            if (((CharacterBase*)character->DrawObject)->GetModelType() != CharacterBase.ModelType.Human) continue;
            var human = (Human*)character->DrawObject;
            var emoteIden = EmoteIdentifier.Get(character);
            if (emoteIden == null) continue;
            var skeleton = human->Skeleton;
            if (skeleton == null) continue;
            for (var i = 0; i < skeleton->PartialSkeletonCount && i < 1; ++i) {
                var partialSkeleton = &skeleton->PartialSkeletons[i];
                var animatedSkeleton = partialSkeleton->GetHavokAnimatedSkeleton(0);
                if (animatedSkeleton == null) continue;
                for (var animControl = 0; animControl < animatedSkeleton->AnimationControls.Length && animControl < 1; ++animControl) {
                    var control = animatedSkeleton->AnimationControls[animControl].Value;
                    if (control == null) continue;
                    control->hkaAnimationControl.LocalTime = 0;
                }
            }
        }
    }
    
    private void DoEmoteSync(List<string> splitArgs) {
        var delay = 0f;
        try {
            for (var i = 0; i < splitArgs.Count; i++) {
                switch (splitArgs[i].ToLowerInvariant()) {
                    case "delay": {
                        if (splitArgs.Count < i + 2) {
                            PluginService.ChatGui.PrintError("Invalid Argument Syntax: delay <seconds>", Name, 500);
                            return;
                        }

                        if (!float.TryParse(splitArgs[i + 1], CultureInfo.InvariantCulture, out delay)) {
                            PluginService.ChatGui.PrintError("Invalid Argument Syntax: delay <seconds>", Name, 500);
                            return;
                        }
                        
                        i++;
                        break;
                    }
                    default: {
                        PluginService.ChatGui.PrintError($"Invalid Argument: {splitArgs[i]}", Name, 500);
                        return;
                    }
                }
            }
        } catch (Exception ex) {
            PluginService.Log.Error(ex, "Error parsing EmoteSync command.");
        }
        
        if (delay <= 0) {
            PluginService.Framework.RunOnFrameworkThread(DoEmoteSync);
        } else {
            PluginService.Framework.RunOnTick(DoEmoteSync, TimeSpan.FromSeconds(delay), cancellationToken: CancellationTokenSource.Token);
        }
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
                    if (splitArgs.Count < 2) {
                        Config.TempOffsetWindowOpen = !Config.TempOffsetWindowOpen;
                        break;
                    }

                    SeString SetHelp(bool add) {
                        var builder = new SeStringBuilder();
                        builder.Add(NewLinePayload.Payload)
                            .AddUiForeground($"{command} temp {(add?"add":"set")} <options...> [silent]", 34)
                            .Add(NewLinePayload.Payload)
                            .AddText("- You can set multiple options in a single command.")
                            .Add(NewLinePayload.Payload)
                            .AddText("- Options are a type and a value.")
                            .Add(NewLinePayload.Payload)
                            .AddText(add ? "- Values are added to your existing offset." : "- Values will be set disregarding your existing offset.")
                            .Add(NewLinePayload.Payload)
                            .AddText("- Types: ")
                            .AddUiForeground("up", 24)
                            .AddText(", ")
                            .AddUiForeground("down", 24)
                            .AddText(", ")
                            .AddUiForeground("forward", 39)
                            .AddText(", ")
                            .AddUiForeground("backward", 39)
                            .AddText(", ")
                            .AddUiForeground("left", 43)
                            .AddText(", ")
                            .AddUiForeground("right", 43)
                            .AddText(", ")
                            .AddUiForeground("rotate", 56);
                        if (Config.TempOffsetPitchRoll) {
                            builder = builder.AddText(", ")
                            .AddUiForeground("pitch", 72)
                            .AddText(", ")
                            .AddUiForeground("roll", 11);
                        }

                        builder = builder.Add(NewLinePayload.Payload)
                            .AddText("- Example: ")
                            .AddUiForeground($"{command} temp {(add?"add":"set")} height 0.2 right 0.5 rotate 90", 34);
                        
                        return builder.Build();
                    }

                    switch (splitArgs[1].ToLowerInvariant()) {
                        case "a":
                        case "add":
                        case "s":
                        case "set": {
                            var add = splitArgs[1].ToLowerInvariant().StartsWith('a');
                            if (splitArgs.Count < 3) {
                                // Explain Set
                                var builder = SetHelp(add);
                                PluginService.ChatGui.Print(builder, Name, 500);
                                break;
                            }

                            var setting = string.Empty;
                            var newOffset = TempOffsets[0]?.Clone() ?? new TempOffset();
                            var anySet = false;
                            var heightSet = false;
                            var silent = false;

                            var emote = EmoteIdentifier.Get(PluginService.ClientState.LocalPlayer);
                            
                            foreach (var a in splitArgs[2..]) {
                                if (!string.IsNullOrWhiteSpace(setting)) {
                                    if (!float.TryParse(a, CultureInfo.InvariantCulture, out var val)) {
                                        PluginService.ChatGui.PrintError($"{a} is not a valid value.", Name, 500);
                                        anySet = false;
                                        goto Error;
                                    }

                                    switch (setting) {
                                        case "height" or "up":
                                            if (add)
                                                newOffset.Y += val;
                                            else
                                                newOffset.Y = val;
                                            anySet = true;
                                            heightSet = true;
                                            break;
                                        case "down":
                                            if (add)
                                                newOffset.Y -= val;
                                            else
                                                newOffset.Y = -val;
                                            anySet = true;
                                            heightSet = true;
                                            break;
                                        case "forward":
                                            if (add)
                                                newOffset.Z += val;
                                            else
                                                newOffset.Z = val;
                                            anySet = true;
                                            break;
                                        case "backward":
                                            if (add)
                                                newOffset.Z -= val;
                                            else
                                                newOffset.Z = -val;
                                            anySet = true;
                                            break;
                                        case "left":
                                            if (add)
                                                newOffset.X += val;
                                            else
                                                newOffset.X = val;
                                            anySet = true;
                                            break;
                                        case "right":
                                            if (add)
                                                newOffset.X -= val;
                                            else
                                                newOffset.X = -val;
                                            anySet = true;
                                            break;
                                        case "rotate" or "yaw":
                                            if (add)
                                                newOffset.R += val * MathF.PI / 180;
                                            else
                                                newOffset.R = val * MathF.PI / 180;
                                            anySet = true;
                                            break;
                                        case "pitch" when Config.TempOffsetPitchRoll:
                                            if (add)
                                                newOffset.Pitch += val * MathF.PI / 180;
                                            else
                                                newOffset.Pitch = val * MathF.PI / 180;
                                            anySet = true;
                                            break;
                                        case "roll" when Config.TempOffsetPitchRoll:
                                            if (add)
                                                newOffset.Roll += val * MathF.PI / 180;
                                            else
                                                newOffset.Roll = val * MathF.PI / 180;
                                            anySet = true;
                                            break;
                                    }
                                    setting = string.Empty;
                                } else {
                                    switch (a) {
                                        case "silent": {
                                            silent = true;
                                            break;
                                        }
                                        case "up" or "down" or "height" or "forward" or "backward" or "left" or "right" or "rotate":
                                        case "pitch" or "roll" when Config.TempOffsetPitchRoll:
                                            setting = a;
                                            break;
                                        default:
                                            PluginService.ChatGui.PrintError($"Invalid option: {a}", Name, 500);
                                            anySet = false;
                                            goto Error;
                                    }
                                }
                            }
                            
                            Error:
                            if (anySet) {
                                if (emote == null) {
                                    newOffset = new TempOffset(y: newOffset.Y);
                                    if (!heightSet) {
                                        if (!silent) {
                                            PluginService.ChatGui.Print("Unable to apply offset. Not performing a looping emote.", Name, 500);
                                        }
                                        break;
                                    }
                                }
                                
                                TempOffsetEmote[0] = emote;
                                TempOffsets[0] = newOffset;
                                ApiProvider.ForceUpdateLocal();
                                
                                if (!silent) {
                                    PluginService.ChatGui.Print("Offset applied.", Name, 500);
                                }
                            } else {
                                var helpMessage = SetHelp(add);
                                PluginService.ChatGui.Print(helpMessage, Name, 500);
                            }
                            
                            
                            break;
                        }

                        case "r":
                        case "reset":
                            var e = TempOffsetEmote[0];
                            var o = TempOffsets[0];
                            if (e != null && o != null) PreviousTempOffsets[e] = o;
                            TempOffsets[0] = null;
                            TempOffsetEmote[0] = null;
                            ApiProvider.ForceUpdateLocal();
                            break;
                        case "help": {
                            var builder = new SeStringBuilder()
                                .AddText("Temp offset commands:")
                                .Add(NewLinePayload.Payload)
                                .AddUiForeground($"{command} temp set <options...> [silent]", 34)
                                .Add(NewLinePayload.Payload)
                                .AddUiForeground($"{command} temp reset", 34);
                            PluginService.ChatGui.Print(builder.Build(), Name, 500);
                            
                            break;
                        }
                        
                        default:
                            PluginService.ChatGui.PrintError($"{command} {args} is not a valid command.", Name, 500);
                            PluginService.ChatGui.PrintError($"Try {command} temp help", Name, 500);
                            break;
                    }
                    
                    break;
                case "syncemote":
                case "emotesync":
                    DoEmoteSync(splitArgs[1..]);
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

                    var newAlreadyExists = Config.WorldCharacterDictionary.ContainsKey(targetworld.Value.RowId) && Config.WorldCharacterDictionary[targetworld.Value.RowId]
                        .ContainsKey(targetname);
                    var oldAlreadyExists = Config.WorldCharacterDictionary.ContainsKey(sourceworld.Value.RowId) && Config.WorldCharacterDictionary[sourceworld.Value.RowId]
                        .ContainsKey(sourcename);
                    if (newAlreadyExists || !oldAlreadyExists || !Config.TryAddCharacter(targetname, targetworld.Value.RowId)) {
                        break;
                    }

                    Config.WorldCharacterDictionary[targetworld.Value.RowId][targetname] = Config.WorldCharacterDictionary[sourceworld.Value.RowId][sourcename];
                    Config.WorldCharacterDictionary[sourceworld.Value.RowId]
                        .Remove(sourcename);
                    if (Config.WorldCharacterDictionary[sourceworld.Value.RowId].Count == 0) {
                        Config.WorldCharacterDictionary.Remove(sourceworld.Value.RowId);
                    }

                    break;
                case "identity":
                    void HelpIdentitySet() {
                        PluginService.ChatGui.Print(new SeStringBuilder().AddText("/heels identity set ").AddUiForeground("<name>", 35).AddText(" | ").AddUiForeground("[server]", 52).Build(), Name, 500);
                    }
                    void HelpIdentity() {
                        HelpIdentitySet();
                        PluginService.ChatGui.Print(new SeStringBuilder().AddText("/heels identity reset").Build(), Name, 500);
                    }
                    
                    if (PluginService.ClientState.LocalContentId == 0 || PluginService.ClientState.LocalPlayer == null) return;
                    if (splitArgs.Count < 2) {
                        HelpIdentity();
                        return;
                    }

                    switch (splitArgs[1]) {
                        case "reset":
                            Config.IdentifyAs.Remove(PluginService.ClientState.LocalContentId);
                            SaveConfig();
                            return;
                        case "set":

                            if (splitArgs.Count < 3) {
                                HelpIdentitySet();
                                return;
                            }

                            var nameServerSplit = string.Join(" ", splitArgs[2..]).Split('|', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);

                            var name = nameServerSplit[0];
                            var serverName = nameServerSplit.Length > 1 ? nameServerSplit[1] : string.Empty;
                            var serverId = 0U;
                            if (string.IsNullOrWhiteSpace(serverName)) {
                                serverId = PluginService.ClientState.LocalPlayer.HomeWorld.RowId;
                            } else {
                                if (!uint.TryParse(serverName, out serverId)) {

                                    var worldRow = PluginService.Data.GetExcelSheet<World>().FirstOrNull(w => w.Name.ExtractText().Equals(serverName, StringComparison.InvariantCultureIgnoreCase));
                                    if (worldRow is { } world) {
                                        serverId = world.RowId;
                                    } else {
                                        PluginService.ChatGui.PrintError($"World not found: '{serverName}'", Name, 500);
                                        return;
                                    }
                                }
                            }

                            if (PluginService.Data.GetExcelSheet<World>().GetRowOrDefault(serverId) == null) {
                                PluginService.ChatGui.PrintError($"World not found: 'World#{serverId}'", Name, 500);
                                return;
                            }
                            
                            Config.IdentifyAs[PluginService.ClientState.LocalContentId] = (name, serverId);
                            SaveConfig();
                            
                            return;
                        default:
                            HelpIdentity();
                            return;
                    }
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

    private delegate void* UpdateMountedPositions(Attach* a1);

    private static uint _companionBaseId = 0;
    private static bool _isMinionAdjusted = false;

    public static void SetMinionAdjusted(Companion* go) {
        if (go == null) return;
        if (go->GetObjectKind() != ObjectKind.Companion) return;
        if (go->DrawObject == null) return;
        _isMinionAdjusted = true;
        _companionBaseId = go->BaseId;
    }
    
    public void UpdateCompanionRotation(Companion* go) {
        if (go == null) return;
        if (go->GetObjectKind() != ObjectKind.Companion) return;
        if (go->DrawObject == null) return;
        if (!_isMinionAdjusted) return;

        if (_companionBaseId != go->BaseId && _isMinionAdjusted) {
            // Reset
            _companionBaseId = 0;
            PluginService.Log.Debug($"Change Companion: {go->BaseId}");
            go->DrawObject->Rotation = FFXIVClientStructs.FFXIV.Common.Math.Quaternion.CreateFromYawPitchRoll(go->Rotation, 0, 0);
            go->Effects.TiltParam1Value = 0;
            go->Effects.TiltParam2Value = 0;
            _isMinionAdjusted = false;
        } else if (_isMinionAdjusted) {
            var yaw = go->Rotation;
            var pitch = go->Effects.TiltParam1Value;
            var roll = go->Effects.TiltParam2Value;
            go->DrawObject->Rotation = FFXIVClientStructs.FFXIV.Common.Math.Quaternion.CreateFromYawPitchRoll(yaw, pitch, roll);
        }
    }

    public static void SaveConfig() {
        try {
            PluginService.Log.Information("Saving Plugin Config");
            PluginService.PluginInterface.SavePluginConfig(Config);
        } catch (Exception ex) {
            PluginService.ChatGui.PrintError($"Failed to save config: {ex.Message}.", "Simple Heels", 500);
            PluginService.Log.Error(ex, "Failed to save config.");
        }
    }
    
}
