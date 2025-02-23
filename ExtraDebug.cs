using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface;
using Dalamud.Interface.Textures;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
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
            DebugUtil.PrintOutObject(chr);
            if (chr->DrawObject != null) {
                if (chr->DrawObject->GetObjectType() == ObjectType.CharacterBase) {
                    var chrBase = (CharacterBase*)chr->DrawObject;
                    if (chrBase->GetModelType() == CharacterBase.ModelType.Human) {
                        DebugUtil.PrintOutObject((Human*) chrBase);
                    } else {
                        DebugUtil.PrintOutObject(chrBase);
                    }
                }
            }
        }
        
        ImGui.Unindent();
        
    }

    private void TabEmoteTiming() {
        var syncAll = ImGui.Button("Sync All");
        if (ImGui.BeginTable("Emote Timings", 5, ImGuiTableFlags.SizingFixedFit | ImGuiTableFlags.BordersH)) {
            ImGui.TableSetupColumn("Character");
            ImGui.TableSetupColumn("Emote");
            ImGui.TableSetupColumn("Position");
            ImGui.TableSetupColumn("Length");
            ImGui.TableSetupColumn("##fill", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            foreach (var c in PluginService.Objects.Where(o => o is IPlayerCharacter).OrderBy(c => c.Name.TextValue)) {
                var character = (Character*)c.Address;
                var emoteIden = EmoteIdentifier.Get(character);
                ImGui.TableNextRow();
                ImGui.TableNextColumn();
                ImGui.Text(character->NameString);
                
                ImGui.TableNextColumn();

                if (character->DrawObject == null) {
                    ImGui.TextDisabled("Not Visible");
                    continue;
                }

                if (character->DrawObject->GetObjectType() != ObjectType.CharacterBase) {
                    ImGui.TextDisabled("Model Invalid");
                    continue;
                }

                var charaBase = (CharacterBase*) character->DrawObject;
                if (charaBase->GetModelType() != CharacterBase.ModelType.Human) {
                    ImGui.TextDisabled("Non-Human");
                    continue;
                }
                
                if (emoteIden == null) {
                    ImGui.TextDisabled("No Emote");
                    continue;
                }

                var human = (Human*)charaBase;
                var skeleton = human->Skeleton;
                if (skeleton == null) {
                    ImGui.TextDisabled("No Skeleton");
                    continue;
                }

                var img = PluginService.TextureProvider.GetFromGameIcon(new GameIconLookup(emoteIden.Icon)).GetWrapOrEmpty();
                ImGui.Image(img.ImGuiHandle, new Vector2(ImGui.GetTextLineHeight()));
                ImGui.SameLine();
                ImGui.Text($"{emoteIden?.Name}");
                ImGui.TableNextColumn();
                var didFirstEntry = false;
                for (var i = 0; i < skeleton->PartialSkeletonCount && i < 1; ++i) {
                    var partialSkeleton = &skeleton->PartialSkeletons[i];
                    var animatedSkeleton = partialSkeleton->GetHavokAnimatedSkeleton(0);
                    if (animatedSkeleton == null) continue;
                    for (var animControl = 0; animControl < animatedSkeleton->AnimationControls.Length && animControl < 1; ++animControl) {
                        var control = animatedSkeleton->AnimationControls[animControl].Value;
                        if (control == null) continue;

                        var binding = control->hkaAnimationControl.Binding.ptr;
                        if (binding == null) continue;

                        var anim = binding->Animation.ptr;
                        if (anim == null) continue;
                
                        var duration = anim->Duration;
                        var position = control->hkaAnimationControl.LocalTime;

                        if (syncAll) {
                            control->hkaAnimationControl.LocalTime = 0;
                        }
                        
                        
                        if (didFirstEntry) {
                            ImGui.TableNextRow();
                            ImGui.TableNextColumn();
                            ImGui.TableNextColumn();
                            ImGui.TableNextColumn();
                        }

                        didFirstEntry = true;
                        using (ImRaii.PushFont(UiBuilder.MonoFont)) {
                            ImGui.Text($"{position:F3}");
                            ImGui.TableNextColumn();
                            ImGui.Text($"{duration:F3}");
                        }
                    }
                }
            }
            
            ImGui.EndTable();
        }
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
        if (ImGui.BeginTable("Static Minions", 9, ImGuiTableFlags.NoClip | ImGuiTableFlags.SizingFixedFit)) {
            ImGui.TableSetupColumn("Player", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Minion", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("X", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Y", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Z", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Yaw", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Pitch", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Roll", ImGuiTableColumnFlags.WidthFixed);
            ImGui.TableSetupColumn("Data", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableHeadersRow();
            foreach (var c in PluginService.Objects.Where(o => o is IPlayerCharacter).Cast<IPlayerCharacter>()) {
                if (c == null) continue;
                var chr = (Character*)c.Address;
                if (chr->CompanionData.CompanionObject == null) continue;
                var companionId = chr->CompanionData.CompanionObject->Character.GameObject.BaseId;
                var companion = PluginService.Data.GetExcelSheet<Companion>()?.GetRow(companionId);
                if (companion == null) continue;
                if (companion.Value.Behavior.RowId != 3) continue;
                using (ImRaii.PushId($"chr_{c.EntityId:X}")) {

                    var go = (FFXIVClientStructs.FFXIV.Client.Game.Character.Companion*) &chr->CompanionData.CompanionObject->Character.GameObject;
                    ImGui.TableNextRow();
                    ImGui.TableNextColumn();
                    ImGui.Text($"{chr->NameString}");
                    // DebugUtil.PrintOutObject(go);
                    ImGui.TableNextColumn();
                    ImGui.Text(companion.Value.Singular.ExtractText());
                    ImGui.TableNextColumn();
                    
                    ImGui.SetNextItemWidth(80);
                    var pos = new Vector3(go->Position.X, go->Position.Y, go->Position.Z);
                    var r = go->Rotation;
                    var pitch = go->Effects.TiltParam1Value;
                    var roll = go->Effects.TiltParam2Value;
                    var changePos = ImGui.DragFloat("##x", ref pos.X, 0.01f);
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(80);
                    changePos |= ImGui.DragFloat("##y", ref pos.Y, 0.01f);
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(80);
                    changePos |= ImGui.DragFloat("##z", ref pos.Z, 0.01f);


                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(80);
                    
                    if (ImGui.DragFloat("##yaw", ref r, 0.01f)) {
                        if (r < -MathF.PI) r += MathF.Tau;
                        if (r >= MathF.PI) r -= MathF.Tau;
                        go->SetRotation(r);
                        plugin.UpdateCompanionRotation(go);
                    }
                    
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(80);
                    
                    if (ImGui.DragFloat("##pitch", ref pitch, 0.01f)) {
                        if (pitch < -MathF.PI) pitch += MathF.Tau;
                        if (pitch >= MathF.PI) pitch -= MathF.Tau;
                        go->Effects.TiltParam1Value = pitch;
                        plugin.UpdateCompanionRotation(go);
                    }
                    
                    ImGui.TableNextColumn();
                    ImGui.SetNextItemWidth(80);
                    
                    if (ImGui.DragFloat("##roll", ref roll, 0.01f)) {
                        if (roll < -MathF.PI) roll += MathF.Tau;
                        if (roll >= MathF.PI) roll -= MathF.Tau;
                        go->Effects.TiltParam2Value = roll;
                        plugin.UpdateCompanionRotation(go);
                    }

                    if (changePos) {
                        go->SetPosition(pos.X, pos.Y, pos.Z);
                    }

                    ImGui.TableNextColumn();
                    DebugUtil.PrintOutObject(go);
                }
            }
            
            ImGui.EndTable();
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
