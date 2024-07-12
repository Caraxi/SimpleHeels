﻿using System;
using System.Diagnostics;
using System.Numerics;
using System.Threading;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Plugin.Ipc;

namespace SimpleHeels;

public static class ApiProvider {
    private const int ApiVersionMajor = 1;
    private const int ApiVersionMinor = 1;

    public const string ApiVersionIdentifier = "SimpleHeels.ApiVersion";
    public const string GetLocalPlayerIdentifier = "SimpleHeels.GetLocalPlayer";
    public const string LocalChangedIdentifier = "SimpleHeels.LocalChanged";
    public const string RegisterPlayerIdentifier = "SimpleHeels.RegisterPlayer";
    public const string UnregisterPlayerIdentifier = "SimpleHeels.UnregisterPlayer";

    public static bool IsSerializing = false;

    private static ICallGateProvider<(int, int)>? _apiVersion;
    private static ICallGateProvider<string>? _getLocalPlayer;
    private static ICallGateProvider<string, object?>? _localChanged;
    private static ICallGateProvider<IGameObject, string, object?>? _registerPlayer;
    private static ICallGateProvider<IGameObject, object?>? _unregisterPlayer;

    private static IpcCharacterConfig? _lastReported;
    private static Vector3? _lastReportedOffset;
    private static float? _lastReportedRotation;
    private static Plugin? _plugin;
    public static readonly Stopwatch TimeSinceLastReport = Stopwatch.StartNew();

    private static CancellationTokenSource? _tokenSource;

    public static string LastReportedData { get; private set; } = string.Empty;

    public static void Init(Plugin plugin) {
        _plugin = plugin;
        var pluginInterface = PluginService.PluginInterface;

        _apiVersion = pluginInterface.GetIpcProvider<(int, int)>(ApiVersionIdentifier);
        _apiVersion.RegisterFunc(() => (ApiVersionMajor, ApiVersionMinor));
        _getLocalPlayer = pluginInterface.GetIpcProvider<string>(GetLocalPlayerIdentifier);
        _localChanged = pluginInterface.GetIpcProvider<string, object?>(LocalChangedIdentifier);
        _registerPlayer = pluginInterface.GetIpcProvider<IGameObject, string, object?>(RegisterPlayerIdentifier);
        _unregisterPlayer = pluginInterface.GetIpcProvider<IGameObject, object?>(UnregisterPlayerIdentifier);

        _apiVersion.RegisterFunc(() => (ApiVersionMajor, ApiVersionMinor));

        _registerPlayer.RegisterAction((gameObject, data) => {
            if (gameObject is not IPlayerCharacter playerCharacter) return;
            if (string.IsNullOrWhiteSpace(data)) {
                Plugin.IpcAssignedData.Remove(playerCharacter.GameObjectId);
                return;
            }

            var assigned = IpcCharacterConfig.FromString(data);
            if (assigned == null) return;
            Plugin.IpcAssignedData.Remove(playerCharacter.GameObjectId);
            Plugin.IpcAssignedData.Add(playerCharacter.GameObjectId, assigned);
            Plugin.RequestUpdateAll();
        });

        _unregisterPlayer.RegisterAction(gameObject => {
            if (gameObject is not IPlayerCharacter playerCharacter) return;
            Plugin.IpcAssignedData.Remove(playerCharacter.GameObjectId);
            Plugin.RequestUpdateAll();
        });

        _getLocalPlayer.RegisterFunc(() => {
            var player = PluginService.ClientState.LocalPlayer;
            if (player == null) return string.Empty;
            return new IpcCharacterConfig(_plugin, player).ToString();
        });
    }

    private static void OnChanged() {
        using (PerformanceMonitors.Run("Generate IPC Message")) {
            PluginService.Log.Debug("Reporting to IPC");
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

        _localChanged = null;
        _apiVersion = null;
        _getLocalPlayer = null;
        _registerPlayer = null;
        _unregisterPlayer = null;
    }

    internal static void UpdateLocal(Vector3 offset, float rotation) {
        if (_lastReportedOffset == null || Vector3.Distance(_lastReportedOffset.Value, offset) > Constants.FloatDelta || _lastReportedRotation == null || MathF.Abs(_lastReportedRotation.Value - rotation) > Constants.FloatDelta) {
            _lastReportedOffset = offset;
            _lastReportedRotation = rotation;
            OnChanged();
        }
    }

    internal static void ForceUpdateLocal() {
        _lastReportedOffset = null;
        _lastReportedRotation = null;
        _lastReported = null;
        OnChanged();
    }

    internal static void UpdateMinion(Vector3 pos, float rotation) {
        if (_lastReported?.MinionPosition == null || Vector3.Distance(new Vector3(_lastReported.MinionPosition.X, _lastReported.MinionPosition.Y, _lastReported.MinionPosition.Z), pos) > Constants.FloatDelta || MathF.Abs(_lastReported.MinionPosition.R - rotation) > Constants.FloatDelta) {
            OnChanged();
        }
    }
}
