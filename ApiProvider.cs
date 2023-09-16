using System;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using Dalamud.Game;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Logging;
using Dalamud.Plugin.Ipc;
using Newtonsoft.Json;

namespace SimpleHeels;

public class AssignedData {
    public const float FloatDifferenceDelta = 0.0001f;
    
    public float Offset;
    public float SittingHeight;
    public float SittingPosition;

    public float GroundSitHeight;
    public float SleepHeight;
    
    public AssignedData() {}

    public unsafe AssignedData(Plugin plugin, GameObject gameObject) {
        var obj = (FFXIVClientStructs.FFXIV.Client.Game.Object.GameObject*)gameObject.Address;
        Offset = plugin.GetOffset(obj, true) ?? 0;
        plugin.TryGetSittingOffset(obj, out SittingHeight, out SittingPosition, true);
        plugin.TryGetGroundSitOffset(obj, out GroundSitHeight, true);
        plugin.TryGetSleepOffset(obj, out SleepHeight, true);
    }

    public static AssignedData? FromString(string json) {
        try {
            if (string.IsNullOrWhiteSpace(json)) return new AssignedData();
            return JsonConvert.DeserializeObject<AssignedData>(json);
        } catch (Exception ex) {
            PluginLog.LogError(ex, "Error decoding AssignedData");
        }
        return null;
    }

    public override string ToString() {
        if (this is { Offset: 0, SittingHeight: 0, SittingPosition: 0, GroundSitHeight: 0, SleepHeight: 0 }) return string.Empty;
        return JsonConvert.SerializeObject(this, Formatting.None, new JsonSerializerSettings { TypeNameHandling = TypeNameHandling.None });
    }

    public override bool Equals(object? obj) {
        if (obj is not AssignedData ad) return false;
        return Equals(ad);
    }

    protected bool Equals(AssignedData ad) {
        return 
            Math.Abs(ad.SittingHeight - SittingHeight) < FloatDifferenceDelta && 
            Math.Abs(ad.SittingPosition - SittingPosition) < FloatDifferenceDelta && 
            Math.Abs(ad.GroundSitHeight - GroundSitHeight) < FloatDifferenceDelta && 
            Math.Abs(ad.SleepHeight - SleepHeight) < FloatDifferenceDelta && 
            Math.Abs(ad.Offset - Offset) < FloatDifferenceDelta;
    }

    public override int GetHashCode() {
        return HashCode.Combine(Offset, SittingHeight, SittingPosition, GroundSitHeight, SleepHeight);
    }
}

public static class ApiProvider {
    private const int ApiVersionMajor = 1;
    private const int ApiVersionMinor = 0;

    public const string ApiVersionIdentifier = "SimpleHeels.ApiVersion";
    public const string GetLocalPlayerIdentifier = "SimpleHeels.GetLocalPlayer";
    public const string LocalChangedIdentifier = "SimpleHeels.LocalChanged";
    public const string RegisterPlayerIdentifier = "SimpleHeels.RegisterPlayer";
    public const string UnregisterPlayerIdentifier = "SimpleHeels.UnregisterPlayer";

    private static ICallGateProvider<(int, int)>? _apiVersion;
    private static ICallGateProvider<string>? _getLocalPlayer;
    private static ICallGateProvider<string, object?>? _localChanged;
    private static ICallGateProvider<GameObject, string, object?>? _registerPlayer;
    private static ICallGateProvider<GameObject, object?>? _unregisterPlayer;

    private static AssignedData? _lastReported = new();
    private static Plugin? _plugin;


    public static string LastReportedData => _lastReported?.ToString() ?? string.Empty;
    public static readonly Stopwatch TimeSinceLastReport = Stopwatch.StartNew();
    
    public static void Init(Plugin plugin) {
        _plugin = plugin;
        var pluginInterface = PluginService.PluginInterface;
        
        _apiVersion = pluginInterface.GetIpcProvider<(int, int)>(ApiVersionIdentifier);
        _getLocalPlayer = pluginInterface.GetIpcProvider<string>(GetLocalPlayerIdentifier);
        _localChanged = pluginInterface.GetIpcProvider<string, object?>(LocalChangedIdentifier);
        _registerPlayer = pluginInterface.GetIpcProvider<GameObject, string, object?>(RegisterPlayerIdentifier);
        _unregisterPlayer = pluginInterface.GetIpcProvider<GameObject, object?>(UnregisterPlayerIdentifier);
        
        
        _apiVersion.RegisterFunc(() => (ApiVersionMajor, ApiVersionMinor));
        
        _registerPlayer.RegisterAction((gameObject, data) =>
        {
            if (gameObject is not PlayerCharacter playerCharacter) return;
            if (string.IsNullOrWhiteSpace(data)) {
                Plugin.IpcAssignedData.Remove((playerCharacter.Name.TextValue, playerCharacter.HomeWorld.Id));
                return;
            }
            var assigned = AssignedData.FromString(data);
            if (assigned == null) return;
            Plugin.IpcAssignedData.Remove((playerCharacter.Name.TextValue, playerCharacter.HomeWorld.Id));
            Plugin.IpcAssignedData.Add((playerCharacter.Name.TextValue, playerCharacter.HomeWorld.Id), assigned);
            Plugin.RequestUpdateAll();
        });

        _unregisterPlayer.RegisterAction((gameObject) =>
        {
            if (gameObject is not PlayerCharacter playerCharacter) return;
            Plugin.IpcAssignedData.Remove((playerCharacter.Name.TextValue, playerCharacter.HomeWorld.Id));
            Plugin.RequestUpdateAll();
        });

        _getLocalPlayer.RegisterFunc(() => {
            var player = PluginService.ClientState.LocalPlayer;
            if (player == null) return string.Empty;
            return new AssignedData(_plugin, player).ToString();
        });
    }
    
    public static void SittingPositionChanged(float y, float z) {
        if (_lastReported == null || Math.Abs(_lastReported.SittingHeight - y) > AssignedData.FloatDifferenceDelta || Math.Abs(_lastReported.SittingPosition - z) > AssignedData.FloatDifferenceDelta) {
            OnChanged();
        }
    }

    public static void StandingOffsetChanged(float y) {
        if (_lastReported == null || Math.Abs(_lastReported.Offset - y) > AssignedData.FloatDifferenceDelta) {
            OnChanged();
        }
    }

    public static void GroundSitOffsetChanged(float y) {
        if (_lastReported == null || Math.Abs(_lastReported.GroundSitHeight - y) > AssignedData.FloatDifferenceDelta) {
            OnChanged();
        }
    }
    
    public static void SleepOffsetChanged(float y) {
        if (_lastReported == null || Math.Abs(_lastReported.SleepHeight - y) > AssignedData.FloatDifferenceDelta) {
            OnChanged();
        }
    }


    private static CancellationTokenSource? _tokenSource;
    private static void OnChanged() {
        TimeSinceLastReport.Restart();
        var gameObject = PluginService.ClientState.LocalPlayer;
        if (gameObject == null) return;
        if (gameObject.ObjectIndex != 0) return;
        if (_plugin == null) return;
        var data = new AssignedData(_plugin, gameObject);
        if (data.Equals(_lastReported)) return;
        var json = data.ToString();
        _lastReported = data;

        var tokenSource = _tokenSource;
        tokenSource?.Cancel();
        
        _tokenSource = new CancellationTokenSource();
        
        PluginService.Framework.RunOnTick(() => {
            _localChanged?.SendMessage(json);
        }, cancellationToken: _tokenSource.Token, delay: TimeSpan.FromMilliseconds(250));
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

}
