using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Runtime.InteropServices;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Animation;
using FFXIVClientStructs.FFXIV.Client.Graphics.Render;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using ImGuiNET;
using Lumina.Excel.Sheets;
using Companion = Lumina.Excel.Sheets.Companion;

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
        ImGui.Spacing();
        ImGui.Spacing();
        ImGui.TextDisabled($"Time Since Report: {ApiProvider.TimeSinceLastReport.Elapsed:hh\\:mm\\:ss}");
        
        ImGui.Separator();
        var localPlayer = PluginService.ClientState.LocalPlayer;
        if (localPlayer != null) {
            var chr = (Character*)localPlayer.Address;
            Util.ShowStruct(chr);
        }
        
        ImGui.Unindent();
        
    }
    
    private void TabPerformance() {
        ImGui.Checkbox("Detailed", ref Plugin.Config.DetailedPerformanceLogging);
        PerformanceMonitors.DrawTable(ImGui.GetContentRegionAvail().Y - ImGui.GetTextLineHeight() * 2);
    }

    private void TabBaseOffsets() {
        foreach (var (index, offset) in plugin.BaseOffsets) ImGui.Text($"Object#{index} => {offset}");
    }

    private Vector4[] minionPositions = new Vector4[Constants.ObjectLimit];
    private void TabStaticMinions() {
        foreach (var c in PluginService.Objects.Where(o => o is IPlayerCharacter).Cast<IPlayerCharacter>()) {
            if (c == null) continue;
            var chr = (Character*)c.Address;
            if (chr->CompanionData.CompanionObject == null) continue;
            var companionId = chr->CompanionData.CompanionObject->Character.GameObject.BaseId;
            var companion = PluginService.Data.GetExcelSheet<Companion>()?.GetRow(companionId);
            if (companion == null) continue;
            if (companion.Value.Behavior.RowId != 3) continue;
            using (ImRaii.PushId($"chr_{c.EntityId:X}")) {
                ImGui.Separator();
                var go = &chr->CompanionData.CompanionObject->Character.GameObject;
                if (minionPositions[go->ObjectIndex] == default) minionPositions[go->ObjectIndex] = new Vector4(go->Position, go->Rotation);
                if (ImGui.DragFloat4($"{c.Name.TextValue}'s {companion.Value.Singular.ToDalamudString().TextValue}", ref minionPositions[go->ObjectIndex], 0.01f)) {
                    go->DrawObject->Object.Position.X = minionPositions[go->ObjectIndex].X;
                    go->DrawObject->Object.Position.Y = minionPositions[go->ObjectIndex].Y;
                    go->DrawObject->Object.Position.Z = minionPositions[go->ObjectIndex].Z;
                    go->DrawObject->Object.Rotation = Quaternion.CreateFromYawPitchRoll(minionPositions[go->ObjectIndex].W, 0, 0);
                }
                
                ImGui.SameLine();
                ImGui.Text($"{go->EntityId:X}");
            }
        }
    }

    private void TabObjects() {
        foreach (var actor in PluginService.Objects) {
            if (actor is not IPlayerCharacter pc) continue;

            if (ImGui.TreeNode($"[{actor.ObjectIndex} ({actor.GetType().Name})] {actor.ObjectKind}.{actor.SubKind} - {actor.Name}")) {
                var obj = (Character*)pc.Address;
                
                ImGui.Text($"Address: {actor.Address:X}");
                if (ImGui.IsItemClicked()) ImGui.SetClipboardText($"{actor.Address:X}");

                ImGui.Text($"Name: '{actor.Name}'");
                ImGui.Text($"HomeWorld: '{pc.HomeWorld.RowId}'");

                ImGui.Text($"Mode: {obj->Mode}");
                ImGui.Text($"ModeParam: {obj->ModeParam}");

                ImGui.Text($"Feet Model: {obj->DrawData.Equipment(DrawDataContainer.EquipmentSlot.Feet).Id}");

                ImGui.Text($"Draw Offset: {obj->GameObject.DrawOffset}");
                ImGui.Text($"Height: {obj->GameObject.Height}");
                
                ImGui.Text("Character Data:");
                ImGui.SameLine();
                Util.ShowStruct(&obj->CharacterData);

                ImGui.Text($"Reaper Shroud:");
                ImGui.SameLine();
                Util.ShowStruct(&obj->ReaperShroud);

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
    
    
    [StructLayout(LayoutKind.Explicit, Size = 128)]
    private struct AnimationResourceHandle {
        [FieldOffset(0x00)] public ResourceHandle ResourceHandle;
        [FieldOffset(0x00)] public void* Unk00;
    }

    [StructLayout(LayoutKind.Explicit, Size = 8)]
    private struct SkeletonExt {
        [FieldOffset(0x00)] public Skeleton Skeleton;
        
        [FieldOffset(0x58)] public SkeletonResourceHandle** SkeletonResourceHandles;
        [FieldOffset(0x70)] public AnimationResourceHandle** AnimationResourceHandles;
        
        
        
    }

    private void TabHeight() {
        foreach (var actor in PluginService.Objects) {
            
            
            
            
        }
    }

    private void TabEmotes() {
        foreach (var actor in PluginService.Objects) {
            using (ImRaii.PushId($"emoteCharacter_{actor.EntityId}")) {
                if (actor is not IPlayerCharacter pc) continue;
                var character = (Character*)pc.Address;
                using var tree = ImRaii.TreeNode(character->NameString);
                if (!tree) continue;
                var drawObject = character->DrawObject;
                if (drawObject->GetObjectType() != ObjectType.CharacterBase) continue;
                var charaBase = (CharacterBase*)drawObject;
                
                if (charaBase->GetModelType() != CharacterBase.ModelType.Human) continue;
                var human = (Human*)charaBase;

                var skeleton = (SkeletonExt*)human->Skeleton;
                if (skeleton == null) continue;
                
                DebugUtil.PrintOutObject(human);
                
                DebugUtil.PrintOutObject(skeleton);
                
                
                
                DebugUtil.PrintOutObject(drawObject);
                var emoteController = &character->EmoteController;

                var timelineContainer = &character->Timeline;
                
                DebugUtil.PrintOutObject(emoteController);
                
                DebugUtil.PrintOutObject(timelineContainer);
                
                if (PluginService.Data.GetExcelSheet<Emote>().TryGetRow(emoteController->EmoteId, out var emote)) {
                    ImGui.Text("Emote:");
                    using (ImRaii.PushIndent()) {
                        ImGui.Text($"Name: {emote.Name.ExtractText().OrIfWhitespace("No Name")}");
                    }
                } else {
                    ImGui.Text("No Emote Data");
                }

                foreach (var timeline in timelineContainer->TimelineSequencer.SchedulerTimelines) {
                    if (timeline.Value == null) continue;
                    if (timeline.Value->Value == null) continue;
                    var tl = timeline.Value->Value;

                    if (tl->SchedulerResource == null) continue;
                    ImGui.Text(tl->SchedulerResource->Name.BufferString);
                    ImGui.SameLine();

                    if (tl->SchedulerResource->Resource == null) {
                        ImGui.Text("No Resource");
                        if (ImGui.IsItemClicked()) {
                            var r = tl->LoadTimelineResources();
                            PluginService.Log.Debug($"{r:X}");
                            ImGui.SetClipboardText($"{r:X}");
                        }
                    } else {
                        ImGui.Text(tl->SchedulerResource->Resource->FileName.ToString());
                    }
                    
                    DebugUtil.PrintOutObject(tl);
                }
                
                
                
                
                






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
