using System;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Logging;
using Dalamud.Plugin.Ipc;

namespace SimpleHeels; 

public static class LegacyApiProvider {
    private const string API_VERSION = "1.0.1";

    public static readonly string ApiVersionIdentifier = "HeelsPlugin.ApiVersion";
    public static readonly string GetOffsetIdentifier = "HeelsPlugin.GetOffset";
    public static readonly string OffsetChangedIdentifier = "HeelsPlugin.OffsetChanged";
    public static readonly string RegisterPlayerIdentifier = "HeelsPlugin.RegisterPlayer";
    public static readonly string UnregisterPlayerIdentifier = "HeelsPlugin.UnregisterPlayer";

    private static ICallGateProvider<string>? ApiVersion;
    private static ICallGateProvider<float>? GetOffset;
    private static ICallGateProvider<float, object?>? OffsetUpdate;
    private static ICallGateProvider<GameObject, float, object?>? RegisterPlayer;
    private static ICallGateProvider<GameObject, object?>? UnregisterPlayer;


    public static void Init(Plugin plugin) {
        var pluginInterface = PluginService.PluginInterface;
        
        ApiVersion = pluginInterface.GetIpcProvider<string>(ApiVersionIdentifier);
        GetOffset = pluginInterface.GetIpcProvider<float>(GetOffsetIdentifier);
        OffsetUpdate = pluginInterface.GetIpcProvider<float, object?>(OffsetChangedIdentifier);
        RegisterPlayer = pluginInterface.GetIpcProvider<GameObject, float, object?>(RegisterPlayerIdentifier);
        UnregisterPlayer = pluginInterface.GetIpcProvider<GameObject, object?>(UnregisterPlayerIdentifier);
        
        RegisterPlayer.RegisterAction((gameObject, offset) =>
        {
            if (gameObject is not PlayerCharacter playerCharacter) return;
            Plugin.IpcAssignedOffset.Remove((playerCharacter.Name.TextValue, playerCharacter.HomeWorld.Id));
            Plugin.IpcAssignedOffset.Add((playerCharacter.Name.TextValue, playerCharacter.HomeWorld.Id), offset);
            Plugin.RequestUpdateAll();
        });

        UnregisterPlayer.RegisterAction((gameObject) =>
        {
            if (gameObject is not PlayerCharacter playerCharacter) return;
            Plugin.IpcAssignedOffset.Remove((playerCharacter.Name.TextValue, playerCharacter.HomeWorld.Id));
            Plugin.RequestUpdateAll();
        });

        ApiVersion.RegisterFunc(() => API_VERSION);
        GetOffset.RegisterFunc(() => {
            var player = PluginService.ClientState.LocalPlayer;
            if (player == null) return 0;
            unsafe {
                return plugin.GetOffset((GameObjectExt*) player.Address) ?? 0;
            }
        });
    }


    private static float? _lastReportedOffset = null;
    
    public static void OnOffsetChange(float offset)
    {
        if (_lastReportedOffset == null || MathF.Abs(_lastReportedOffset.Value - offset) > 0.0001f) {
            PluginLog.Debug($"Announcing Local Offset Change: {offset}");
            OffsetUpdate?.SendMessage(offset);
            _lastReportedOffset = offset;
        }
    }
    
    internal static void DeInit() {
        ApiVersion?.UnregisterFunc();
        GetOffset?.UnregisterFunc();
        RegisterPlayer?.UnregisterAction();
        UnregisterPlayer?.UnregisterAction();

        OffsetUpdate = null;
        ApiVersion = null;
        GetOffset = null;
        UnregisterPlayer = null;
    }

}
