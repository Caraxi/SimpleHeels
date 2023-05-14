using Dalamud.Data;
using Dalamud.Game;
using Dalamud.Game.ClientState;
using Dalamud.Game.ClientState.Objects;
using Dalamud.Game.Command;
using Dalamud.Game.Gui;
using Dalamud.IoC;
using Dalamud.Plugin;

namespace SimpleHeels; 
// ReSharper disable AutoPropertyCanBeMadeGetOnly.Local
public class PluginService {
    [PluginService] public static DalamudPluginInterface PluginInterface { get; private set; } = null!;
    [PluginService] public static ClientState ClientState { get; private set; } = null!;
    [PluginService] public static CommandManager Commands { get; private set; } = null!;
    [PluginService] public static DataManager Data { get; private set; } = null!;
    [PluginService] public static ObjectTable Objects { get; private set; } = null!;
    [PluginService] public static TargetManager Targets { get; private set; } = null!;
    [PluginService] public static Framework Framework { get; private set; } = null!;
    [PluginService] public static ChatGui ChatGui { get; private set; } = null!;
}
