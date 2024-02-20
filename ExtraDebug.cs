using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ImGuiNET;

namespace SimpleHeels;

public unsafe class ExtraDebug : Window {
    private readonly Plugin plugin;

    private readonly List<MethodInfo> tabs = new();

    public ExtraDebug(Plugin plugin, PluginConfig config) : base("SimpleHeels Extended Debugging") {
        this.plugin = plugin;
        foreach (var m in GetType().GetMethods(BindingFlags.NonPublic | BindingFlags.Instance).Where(m => m.GetParameters().Length == 0 && m.Name.StartsWith("Tab"))) tabs.Add(m);
    }

    public override void OnOpen() {
        Plugin.Config.ExtendedDebugOpen = true;
    }

    public override void OnClose() {
        Plugin.Config.ExtendedDebugOpen = false;
    }

    private void TabDebug() {
        ImGui.Text("Last Reported IPC:");
        ImGui.Indent();

        ImGui.TextWrapped(ApiProvider.LastReportedData);
        
        
        
        ImGui.Unindent();
        
    }
    
    private void TabPerformance() {
        ImGui.Checkbox("Detailed", ref Plugin.Config.DetailedPerformanceLogging);
        PerformanceMonitors.DrawTable(ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeight() * 2);
    }

    private void TabBaseOffsets() {
        foreach (var (index, offset) in plugin.BaseOffsets) ImGui.Text($"Object#{index} => {offset}");
    }

    private void TabObjects() {
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

                if (Plugin.ActorMapping.TryGetValue(actor.ObjectIndex, out var map)) ImGui.Text($"Clone of {map.name} @ {map.homeWorld}");

                var o = (Vector3)obj->GameObject.DrawOffset;
                if (ImGui.DragFloat3($"Draw Offset##{obj->GameObject.ObjectIndex}", ref o, 0.001f)) obj->GameObject.SetDrawOffset(o.X, o.Y, o.Z);

                if (plugin.TryGetCharacterConfig(obj, out var characterConfig))
                    if (characterConfig.TryGetFirstMatch(obj, out var offsetProvider)) {
                        var expectedOffset = offsetProvider.GetOffset();

                        ImGui.Text("Offset Provider: ");
                        Util.ShowObject(offsetProvider);
                        ImGui.Text($"Expected Offset: {expectedOffset}");
                    }

                ImGui.Text($"IsManaged: {plugin.ManagedIndex[obj->GameObject.ObjectIndex]}");
                ImGui.Text($"IsCharacter: {obj->GameObject.IsCharacter()}");
                ImGui.Text($"PoseIdentifier: {EmoteIdentifier.Get(obj)}");
                
                ImGui.TreePop();
            }
        }
    }

    public override void Draw() {
        if (ImGui.BeginTabBar("tabs")) {
            foreach (var t in tabs)
                if (ImGui.BeginTabItem($"{t.Name[3..]}")) {
                    t.Invoke(this, null);
                    ImGui.EndTabItem();
                }

            ImGui.EndTabBar();
        }
    }
}
