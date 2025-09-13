using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Plugin.Ipc;

namespace SimpleHeels;

public static class ApiProvider {
    private const int ApiVersionMajor = 2;
    private const int ApiVersionMinor = 4;

    public const string ApiVersionIdentifier = "SimpleHeels.ApiVersion";
    public const string GetLocalPlayerIdentifier = "SimpleHeels.GetLocalPlayer";
    public const string LocalChangedIdentifier = "SimpleHeels.LocalChanged";
    public const string RegisterPlayerIdentifier = "SimpleHeels.RegisterPlayer";
    public const string UnregisterPlayerIdentifier = "SimpleHeels.UnregisterPlayer";
    public const string CreateTagIdentifier = "SimpleHeels.SetTag";
    public const string GetTagIdentifier = "SimpleHeels.GetTag";
    public const string RemoveTagIdentifier = "SimpleHeels.RemoveTag";
    public const string TagChangedIdentifier = "SimpleHeels.TagChanged";
    public const string SetLocalPlayerIdentity = "SimpleHeels.SetLocalPlayerIdentity";
    public const string ReadyIdentifier = "SimpleHeels.Ready";

    public static bool IsSerializing = false;

    private static ICallGateProvider<(int, int)>? _apiVersion;
    private static ICallGateProvider<string>? _getLocalPlayer;
    private static ICallGateProvider<string, object?>? _localChanged;
    private static ICallGateProvider<int, string, object?>? _registerPlayer;
    private static ICallGateProvider<int, object?>? _unregisterPlayer;
    private static ICallGateProvider<int, string, string, object?>? _setTag;
    private static ICallGateProvider<int, string, string?>? _getTag;
    private static ICallGateProvider<int, string, object?>? _removeTag;
    private static ICallGateProvider<int, string, string?, object?>? _tagChanged;
    private static ICallGateProvider<string, uint, object?>? _setLocalPlayerIdentity;
    private static ICallGateProvider<object?>? _ready;

    private static IpcCharacterConfig? _lastReported;
    private static Vector3? _lastReportedOffset;
    private static float? _lastReportedRotation;
    private static PitchRoll? _lastReportedPitchRoll;
    private static Plugin? _plugin;
    public static readonly Stopwatch TimeSinceLastReport = Stopwatch.StartNew();

    private static CancellationTokenSource? _tokenSource;

    private static bool localTagsChanged = false;

    public static string LastReportedData { get; private set; } = string.Empty;

    public static void Init(Plugin plugin) {
        _plugin = plugin;
        var pluginInterface = PluginService.PluginInterface;

        _apiVersion = pluginInterface.GetIpcProvider<(int, int)>(ApiVersionIdentifier);
        _apiVersion.RegisterFunc(() => (ApiVersionMajor, ApiVersionMinor));
        _getLocalPlayer = pluginInterface.GetIpcProvider<string>(GetLocalPlayerIdentifier);
        _localChanged = pluginInterface.GetIpcProvider<string, object?>(LocalChangedIdentifier);
        _registerPlayer = pluginInterface.GetIpcProvider<int, string, object?>(RegisterPlayerIdentifier);
        _unregisterPlayer = pluginInterface.GetIpcProvider<int, object?>(UnregisterPlayerIdentifier);
        _setTag = pluginInterface.GetIpcProvider<int, string, string, object?>(CreateTagIdentifier);
        _getTag = pluginInterface.GetIpcProvider<int, string, string?>(GetTagIdentifier);
        _removeTag = pluginInterface.GetIpcProvider<int, string, object?>(RemoveTagIdentifier);
        _setLocalPlayerIdentity = pluginInterface.GetIpcProvider<string, uint, object?>(SetLocalPlayerIdentity);
        _tagChanged = pluginInterface.GetIpcProvider<int, string, string?, object?>(TagChangedIdentifier);
        _ready = pluginInterface.GetIpcProvider<object?>(ReadyIdentifier);

        _apiVersion.RegisterFunc(() => (ApiVersionMajor, ApiVersionMinor));

        _registerPlayer.RegisterAction((gameObjectIndex, data) => {
            var gameObject = gameObjectIndex >= 0 && gameObjectIndex < PluginService.Objects.Length ? PluginService.Objects[gameObjectIndex] : null;
            if (gameObject is not IPlayerCharacter playerCharacter) return;
            if (string.IsNullOrWhiteSpace(data)) {
                Plugin.IpcAssignedData.Remove(playerCharacter.EntityId);
                return;
            }

            Dictionary<string, string> tags = [];
            
            if (Plugin.IpcAssignedData.TryGetValue(playerCharacter.EntityId, out var ipcCharacterConfig)) {
                tags = ipcCharacterConfig.Tags;
            }
            
            Plugin.IpcAssignedData.Remove(playerCharacter.EntityId);
            
            var assigned = IpcCharacterConfig.FromString(data);
            if (assigned == null) return;

            Plugin.IpcAssignedData.Add(playerCharacter.EntityId, assigned);

            foreach (var (tag, value) in assigned.Tags) {
                if (tags.Remove(tag, out var oldValue)) {
                    if (oldValue.Equals(value)) continue;
                }
                _tagChanged.SendMessage(gameObjectIndex, tag, value);
            }

            foreach (var tag in tags.Keys) {
                _tagChanged.SendMessage(gameObjectIndex, tag, null);
            }
            
            Plugin.RequestUpdateAll();
        });

        _unregisterPlayer.RegisterAction(gameObjectIndex => {
            var gameObject = gameObjectIndex >= 0 && gameObjectIndex < PluginService.Objects.Length ? PluginService.Objects[gameObjectIndex] : null;
            if (gameObject is not IPlayerCharacter playerCharacter) return;

            if (Plugin.IpcAssignedData.TryGetValue(playerCharacter.EntityId, out var ipcCharacterConfig)) {
                foreach (var t in ipcCharacterConfig.Tags) {
                    _tagChanged.SendMessage(gameObjectIndex, t.Key, null);
                }
            }
            
            Plugin.IpcAssignedData.Remove(playerCharacter.EntityId);
            Plugin.RequestUpdateAll();
        });

        _getLocalPlayer.RegisterFunc(() => {
            var player = PluginService.ClientState.LocalPlayer;
            if (player == null) return string.Empty;
            return new IpcCharacterConfig(_plugin, player).ToString();
        });
        
        _setTag.RegisterAction((gameObjectIndex, tag, value) => {
            var gameObject = gameObjectIndex >= 0 && gameObjectIndex < PluginService.Objects.Length ? PluginService.Objects[gameObjectIndex] : null;
            if (gameObject is not IPlayerCharacter playerCharacter) return;

            if (!Plugin.Tags.TryGetValue(gameObject.EntityId, out var tagDict)) {
                tagDict = new Dictionary<string, string>();
                Plugin.Tags.Add(playerCharacter.EntityId, tagDict);
            }

            if (tagDict.TryGetValue(tag, out var oldValue)) {
                if (value.Equals(oldValue)) return;
                tagDict[tag] = value;
                _tagChanged.SendMessage(gameObjectIndex, tag, value);
                if (gameObject.ObjectIndex != 0) return;
                localTagsChanged = true;
                OnChanged();
                return;
            }

            if (!tagDict.TryAdd(tag, value)) return;
            
            _tagChanged.SendMessage(gameObjectIndex, tag, value);
            if (gameObject.ObjectIndex != 0) return;
            
            localTagsChanged = true;
            OnChanged();
        });
        
        _getTag.RegisterFunc((gameObjectIndex, tag) => {
            var gameObject = gameObjectIndex >= 0 && gameObjectIndex < PluginService.Objects.Length ? PluginService.Objects[gameObjectIndex] : null;
            if (gameObject is not IPlayerCharacter playerCharacter) return null;

            if (Plugin.IpcAssignedData.TryGetValue(playerCharacter.EntityId, out var ipcCharacterConfig)) {
                return ipcCharacterConfig.Tags.GetValueOrDefault(tag);
            }
            
            return !Plugin.Tags.TryGetValue(playerCharacter.EntityId, out var tagDict) ? null : tagDict.GetValueOrDefault(tag);
        });
        
        _removeTag.RegisterAction((gameObjectIndex, tag) => {
            var gameObject = gameObjectIndex >= 0 && gameObjectIndex < PluginService.Objects.Length ? PluginService.Objects[gameObjectIndex] : null;
            if (gameObject is not IPlayerCharacter playerCharacter) return;

            if (!Plugin.Tags.TryGetValue(gameObject.EntityId, out var tagDict)) {
                return;
            }
            if (tagDict.Remove(tag)) {
                _tagChanged.SendMessage(gameObjectIndex, tag, null);
            }
            if (tagDict.Count == 0) Plugin.Tags.Remove(playerCharacter.EntityId);
            if (gameObject.ObjectIndex == 0) {
                localTagsChanged = true;
                OnChanged();
            }
        });
        
        _setLocalPlayerIdentity.RegisterAction(((name, world) => {
            if (PluginService.ClientState.LocalContentId == 0) return;
            if (string.IsNullOrWhiteSpace(name)) {
                Plugin.Config.IdentifyAs.Remove(PluginService.ClientState.LocalContentId);
            } else {
                Plugin.Config.IdentifyAs[PluginService.ClientState.LocalContentId] = (name, world);
            }
        }));
        
        _ready.SendMessage();
    }

    private static void OnChanged() {
        using (PerformanceMonitors.Run("Generate IPC Message")) {
            PluginService.Log.Debug("Reporting to IPC");
            localTagsChanged = false;
            TimeSinceLastReport.Restart();
            var gameObject = PluginService.ClientState.LocalPlayer;
            if (gameObject == null) return;
            if (gameObject.ObjectIndex != 0) return;
            if (_plugin == null) return;
            var data = new IpcCharacterConfig(_plugin, gameObject);
            if (data.Equals(_lastReported)) return;

            var json = data.ToString();
            _lastReported = data;

            var tokenSource = _tokenSource;
            tokenSource?.Cancel();

            _tokenSource = new CancellationTokenSource();

            PluginService.Framework.RunOnTick(() => {
                using (PerformanceMonitors.Run("Send IPC Message")) {
                    LastReportedData = json;
                    _localChanged?.SendMessage(json);
                }
            }, cancellationToken: _tokenSource.Token, delay: TimeSpan.FromMilliseconds(250));
        }
    }

    internal static void DeInit() {
        _tokenSource?.Cancel();
        
        _apiVersion?.UnregisterFunc();
        _getLocalPlayer?.UnregisterFunc();
        _registerPlayer?.UnregisterAction();
        _unregisterPlayer?.UnregisterAction();
        _setTag?.UnregisterAction();
        _getTag?.UnregisterFunc();
        _removeTag?.UnregisterAction();
        _setLocalPlayerIdentity?.UnregisterAction();

        _localChanged = null;
        _apiVersion = null;
        _getLocalPlayer = null;
        _registerPlayer = null;
        _unregisterPlayer = null;
        _getTag = null;
        _removeTag = null;
        _setTag = null;
        _setLocalPlayerIdentity = null;
    }

    internal static void UpdateLocal(Vector3 offset, float rotation, PitchRoll pitchRoll) {
        if (localTagsChanged || _lastReportedOffset == null || Vector3.Distance(_lastReportedOffset.Value, offset) > Constants.FloatDelta ||
            _lastReportedRotation == null || MathF.Abs(_lastReportedRotation.Value - rotation) > Constants.FloatDelta ||
            _lastReportedPitchRoll == null || !PitchRoll.Equal(_lastReportedPitchRoll, pitchRoll)) {
            _lastReportedOffset = offset;
            _lastReportedRotation = rotation;
            _lastReportedPitchRoll = pitchRoll;
            OnChanged();
        }
    }

    internal static void ForceUpdateLocal() {
        _lastReportedOffset = null;
        _lastReportedRotation = null;
        _lastReportedPitchRoll = null;
        _lastReported = null;
        localTagsChanged = true;
        OnChanged();
    }

    internal static void UpdateMinion(Vector3 pos, float rotation, float pitch, float roll) {
        if (_lastReported?.MinionPosition == null || 
            Vector3.Distance(new Vector3(_lastReported.MinionPosition.X, _lastReported.MinionPosition.Y, _lastReported.MinionPosition.Z), pos) > Constants.FloatDelta || 
            MathF.Abs(_lastReported.MinionPosition.R - rotation) > Constants.FloatDelta || 
            MathF.Abs(_lastReported.MinionPosition.Pitch - pitch) > Constants.FloatDelta || 
            MathF.Abs(_lastReported.MinionPosition.Roll - roll) > Constants.FloatDelta) {
            OnChanged();
        }
    }
}
