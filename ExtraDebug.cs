#if DEBUG
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ImGuiNET;

namespace SimpleHeels; 

public unsafe class ExtraDebug : Window {
    private readonly Plugin plugin;

    public ExtraDebug(Plugin plugin, PluginConfig config) : base("SimpleHeels Extended Debugging") {
        this.plugin = plugin;
    }

    public override void Draw() {

        
        
        
        foreach (var actor in PluginService.Objects) {
            if (actor is not PlayerCharacter pc) continue;

            
            
            if (ImGui.TreeNode($"[{actor.ObjectIndex} ({actor.GetType().Name})] {actor.ObjectKind}.{actor.SubKind} - {actor.Name}")) {

                var obj = (Character*)pc.Address;
                
                ImGui.Text($"Address: {actor.Address:X}");
                if (ImGui.IsItemClicked()) ImGui.SetClipboardText($"{actor.Address:X}");
                
                ImGui.Text($"Name: '{actor.Name}'");
                ImGui.Text($"HomeWorld: '{pc.HomeWorld.Id}'");
                
                ImGui.Text($"Mode: {obj->Mode}");
                ImGui.Text($"ModeParam: {obj->ModeParam}");
                
                ImGui.Text($"Feet Model: {obj->DrawData.Feet.Id}");
                
                ImGui.Text($"Draw Offset: {obj->GameObject.DrawOffset}");
                ImGui.Text($"Height: {obj->GameObject.Height}");

                if (Plugin.ActorMapping.TryGetValue(actor.ObjectIndex, out var map)) {
                    ImGui.Text($"Clone of {map.name} @ {map.homeWorld}");
                }

                var o = (Vector3) obj->GameObject.DrawOffset;
                if (ImGui.DragFloat3($"Draw Offset##{obj->GameObject.ObjectIndex}", ref o, 0.001f)) {
                    obj->GameObject.SetDrawOffset(o.X, o.Y, o.Z);
                }

                var e = plugin.GetOffset(&obj->GameObject);
                ImGui.Text($"Expected Offset: {e}");
                
                ImGui.Text($"IsManaged: {plugin.ManagedIndex[obj->GameObject.ObjectIndex]}");
                ImGui.Text($"AppliedSittingOffset: {plugin.AppliedSittingOffset[obj->GameObject.ObjectIndex]}");
                ImGui.Text($"IsCharacter: {obj->GameObject.IsCharacter()}");
                
                
                ImGui.TreePop();
                
                
                
            };
            
            
        }
        
        
        
    }
}

#endif