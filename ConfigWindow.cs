using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Newtonsoft.Json;
using SimpleHeels.Files;
using World = Lumina.Excel.GeneratedSheets.World;

namespace SimpleHeels; 

public class ConfigWindow : Window {
    private readonly PluginConfig config;
    private readonly Plugin plugin;

    private DalamudLinkPayload clickAllowInGposePayload;
    private DalamudLinkPayload clickAllowInCutscenePayload;
    
    private CancellationTokenSource? notVisibleWarningCancellationTokenSource;
    private readonly Stopwatch hiddenStopwatch = Stopwatch.StartNew();
    private string? PenumbraModFolder;
    
    public ConfigWindow(string name, Plugin plugin, PluginConfig config) : base(name, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse) {
        this.config = config;
        this.plugin = plugin;
        
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = ImGuiHelpers.ScaledVector2(800, 400),
            MaximumSize = new Vector2(float.MaxValue)
        };

        Size = ImGuiHelpers.ScaledVector2(1000, 500);
        SizeCondition = ImGuiCond.FirstUseEver;

        clickAllowInGposePayload = PluginService.PluginInterface.AddChatLinkHandler(1000, (_, _) => {
            config.ConfigInGpose = true;
            PluginService.PluginInterface.UiBuilder.DisableGposeUiHide = true;
            IsOpen = true;
        });
        
        clickAllowInCutscenePayload = PluginService.PluginInterface.AddChatLinkHandler(1001, (_, _) => {
            config.ConfigInCutscene = true;
            PluginService.PluginInterface.UiBuilder.DisableCutsceneUiHide = true;
            IsOpen = true;
        });
    }

    public override void OnOpen() {
        UpdatePenumbraModFolder();
    }

    private void UpdatePenumbraModFolder() {
        try {
            var getModDir = PluginService.PluginInterface.GetIpcSubscriber<string>("Penumbra.GetModDirectory");
            PenumbraModFolder = getModDir.InvokeFunc();
            PenumbraModFolder = PenumbraModFolder.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar).Trim(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            PluginService.Log.Debug($"Penumbra Folder: {PenumbraModFolder}");
        } catch {
            PenumbraModFolder = null;
        }
    }
    
    private Vector2 iconButtonSize = new(16);
    private float checkboxSize = 36;

    private readonly Stopwatch holdingClick = Stopwatch.StartNew();
    private readonly Stopwatch clickHoldThrottle = Stopwatch.StartNew();

    private string groupNameMatchingNewInput = string.Empty;
    private string groupNameMatchingWorldSearch = string.Empty;

    public unsafe void DrawCharacterList() {

        foreach (var (worldId, characters) in config.WorldCharacterDictionary.ToArray()) {
            var world = PluginService.Data.GetExcelSheet<World>()?.GetRow(worldId);
            if (world == null) continue;
            
            ImGui.TextDisabled($"{world.Name.RawString}");
            ImGuiExt.Separator();

            foreach (var (name, characterConfig) in characters.ToArray()) {
                if (ImGui.Selectable($"{name}##{world.Name.RawString}", selectedCharacter == characterConfig)) {
                    selectedCharacter = characterConfig;
                    selectedName = name;
                    selectedWorld = world.RowId;
                    newName = name;
                    newWorld = world.RowId;
                    selectedGroup = null;
                }
                
                if (ImGui.BeginPopupContextItem()) {
                    if (ImGui.Selectable($"Remove '{name} @ {world.Name.RawString}' from Config")) {
                        characters.Remove(name);
                        if (selectedCharacter == characterConfig) selectedCharacter = null;
                        if (characters.Count == 0) {
                            config.WorldCharacterDictionary.Remove(worldId);
                        }
                    }
                    ImGui.EndPopup();
                }
            }

            ImGuiHelpers.ScaledDummy(10);
        }

        if (Plugin.IsDebug && Plugin.IpcAssignedData.Count > 0) {
            ImGui.TextDisabled("[DEBUG] IPC Assignments");
            ImGuiExt.Separator();

            foreach (var (name, worldId) in Plugin.IpcAssignedData.Keys) {
                var world = PluginService.Data.GetExcelSheet<World>()?.GetRow(worldId);
                if (world == null) continue;
                if (ImGui.Selectable($"{name}##{world.Name.RawString}##ipc", selectedName == name && selectedWorld == worldId)) {
                    config.TryGetCharacterConfig(name, worldId, null, out selectedCharacter);
                    selectedCharacter ??= new CharacterConfig();
                    selectedName = name;
                    selectedWorld = world.RowId;
                    newName = string.Empty;
                    newWorld = 0;
                    selectedGroup = null;
                }
                ImGui.SameLine();
                ImGui.TextDisabled(world.Name.ToDalamudString().TextValue);
            }
            
            ImGuiHelpers.ScaledDummy(10);
        }


        if (config.Groups.Count > 0) {
            ImGui.TextDisabled($"Group Assignments");
            ImGuiExt.Separator();
            var arr = config.Groups.ToArray();
            
            for(var i = 0; i < arr.Length; i++) {
                var filterConfig = arr[i];
                if (ImGui.Selectable($"{filterConfig.Label}##filterConfig_{i}", selectedGroup == filterConfig)) {
                    selectedCharacter = null;
                    selectedName = string.Empty;
                    selectedWorld = 0;
                    newName = string.Empty;
                    newWorld = 0;
                    selectedGroup = filterConfig;
                    selectedGroup.Characters.RemoveAll(c => string.IsNullOrWhiteSpace(c.Name));
                }
                
                if (ImGui.BeginPopupContextItem()) {
                    if (config.Groups.Count > 1) {

                        if (i > 0) {
                            if (ImGui.Selectable($"Move Up")) {
                                config.Groups.Remove(filterConfig);
                                config.Groups.Insert(i - 1, filterConfig);
                            }
                        }

                        if (i < config.Groups.Count - 1) {
                            if (ImGui.Selectable($"Move Down")) {
                                config.Groups.Remove(filterConfig);
                                config.Groups.Insert(i + 1, filterConfig);
                            }
                        }
                        
                        
                        

                        ImGuiExt.Separator();
                    }

                    ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGui.GetIO().KeyShift ? ImGuiCol.Text : ImGuiCol.TextDisabled));
                    if (ImGui.Selectable($"Delete group '{filterConfig.Label}'") && ImGui.GetIO().KeyShift) {
                        config.Groups.Remove(filterConfig);
                    }
                    ImGui.PopStyleColor();
                    if (!ImGui.GetIO().KeyShift && ImGui.IsItemHovered()) {
                        ImGui.SetTooltip("Hold SHIFT to delete.");
                    }
                    ImGui.EndPopup();
                }
                
            }
            ImGuiHelpers.ScaledDummy(10);
        }
    }

    private CharacterConfig? selectedCharacter;
    private string selectedName = string.Empty;
    private uint selectedWorld;

    private string newName = string.Empty;
    private uint newWorld = 0;

    private GroupConfig? selectedGroup;
    
    private void ShowDebugInfo() {
        if (Plugin.IsDebug && ImGui.TreeNode("DEBUG INFO")) {
            try {
                var activePlayer = PluginService.Objects.FirstOrDefault(t => t is PlayerCharacter playerCharacter && playerCharacter.Name.TextValue == selectedName && playerCharacter.HomeWorld.Id == selectedWorld);

                if (activePlayer is not PlayerCharacter pc) {
                    ImGui.TextDisabled("Character is not currently in world.");
                    return;
                }
                
                ImGui.TextDisabled($"Character: {pc:X8}");
                if (ImGui.IsItemClicked()) ImGui.SetClipboardText($"{pc.Address:X}");

                unsafe {
                    var obj = (GameObject*)activePlayer.Address;
                    var character = (Character*)obj;
                    Util.ShowStruct(character);
                    var realPosition = obj->Position;
                    if (obj->DrawObject == null) {
                        ImGui.TextDisabled("Character is not currently being drawn.");
                        return;
                    }

                    var drawPosition = obj->DrawObject->Object.Position;

                    ImGui.Text($"Actual Y Position: {realPosition.Y}");
                    ImGui.Text($"Drawn Y Position: {drawPosition.Y}");
                    if (ImGui.IsItemClicked()) {
                        ImGui.SetClipboardText($"{(ulong)(&obj->DrawObject->Object.Position.Y):X}");
                    }

                    ImGui.Text($"Active Offset: {drawPosition.Y - realPosition.Y}");
                    ImGui.Text($"Expected Offset: {plugin.GetOffset(obj)}");

                    ImGui.Text($"Height: {obj->GetHeight()}");
                    ImGui.Text($"Mode: {character->Mode}, {character->ModeParam}");

                    ImGui.Text($"Object Type: {obj->DrawObject->Object.GetObjectType()}");
                    if (obj->DrawObject->Object.GetObjectType() == ObjectType.CharacterBase) {
                        var characterBase = (CharacterBase*)obj->DrawObject;
                        ImGui.Text($"Model Type: {characterBase->GetModelType()}");
                        if (characterBase->GetModelType() == CharacterBase.ModelType.Human) {
                            var human = (Human*)obj->DrawObject;
                            ImGui.Text("Active Models:");
                            ImGui.Indent();
                            ImGui.Text("Top:");
                            ImGui.Indent();
                            ImGui.Text($"ID: {human->Top.Id}, {human->Top.Variant}");
                            ImGui.Text($"Name: {GetModelName(human->Top.Id, ModelSlot.Top, true) ?? "Does not replace feet"}");
                            ImGui.Text($"Path: {Plugin.GetModelPath(human, ModelSlot.Top)}");
                            ImGui.Unindent();
                            ImGui.Text("Legs:");
                            ImGui.Indent();
                            ImGui.Text($"ID: {human->Legs.Id}, {human->Legs.Variant}");
                            ImGui.Text($"Name: {GetModelName(human->Legs.Id, ModelSlot.Legs, true) ?? "Does not replace feet"}");
                            ImGui.Text($"Path: {Plugin.GetModelPath(human, ModelSlot.Legs)}");
                            ImGui.Unindent();
                            ImGui.Text("Feet:");
                            ImGui.Indent();
                            ImGui.Text($"ID: {human->Feet.Id}, {human->Feet.Variant}");
                            ImGui.Text($"Name: {GetModelName(human->Feet.Id, ModelSlot.Feet)}");
                            ImGui.Text($"Path: {Plugin.GetModelPath(human, ModelSlot.Feet)}");
                            ImGui.Unindent();
                            
                            ImGui.Unindent();
                        } else {
                            ImGui.TextDisabled("Player is not a 'Human'");
                        }
                    } else {
                        ImGui.TextDisabled("Player is not a 'Character'");
                    }
                }
                
            } finally {
                ImGui.TreePop();
            }
        }
                
    }

    private float kofiButtonOffset = 0f;
    
    public override void Draw() {
        hiddenStopwatch.Restart();
        if (notVisibleWarningCancellationTokenSource != null) {
            notVisibleWarningCancellationTokenSource.Cancel();
            notVisibleWarningCancellationTokenSource = null;
        }
        
        if (holdingClick.IsRunning && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) holdingClick.Restart();
        if (!Plugin.IsEnabled) {
            ImGui.TextColored(ImGuiColors.DalamudRed, $"{plugin.Name} is currently disabled due to Heels Plugin being installed.\nPlease uninstall Heels Plugin to allow {plugin.Name} to run.");
            return;
        }

        if (!config.Enabled) {
            ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudRed * new Vector4(1, 1, 1, 0.3f));
            ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.DalamudRed * new Vector4(1, 1, 1, 0.3f));
            ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.DalamudRed * new Vector4(1, 1, 1, 0.3f));
            ImGui.Button($"{plugin.Name} is currently disabled. No offsets will be applied.", new Vector2(ImGui.GetContentRegionAvail().X, 32 * ImGuiHelpers.GlobalScale));
            ImGui.PopStyleColor(3);
        }
        

        ImGui.BeginGroup();
        {
            if (ImGui.BeginChild("character_select", ImGuiHelpers.ScaledVector2(240, 0) - iconButtonSize with { X = 0 }, true)) {
                DrawCharacterList();
            }
            ImGui.EndChild();

            var charaListPos = ImGui.GetItemRectSize().X;

            if (PluginService.ClientState.LocalPlayer != null) {
                if (ImGuiComponents.IconButton(FontAwesomeIcon.User)) {
                    if (PluginService.ClientState.LocalPlayer != null) {
                        config.TryAddCharacter(PluginService.ClientState.LocalPlayer.Name.TextValue, PluginService.ClientState.LocalPlayer.HomeWorld.Id);
                    }
                }
                
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add current character");
                
                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.DotCircle)) {
                    if (PluginService.Targets.Target is PlayerCharacter pc) {
                        config.TryAddCharacter(pc.Name.TextValue, pc.HomeWorld.Id);
                    }
                }
                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add targeted character");
                ImGui.SameLine();
            }

            if (ImGuiComponents.IconButton(FontAwesomeIcon.PeopleGroup)) {
                var newGroup = new GroupConfig();
                selectedCharacter = null;
                selectedName = string.Empty;
                selectedWorld = 0;
                newName = string.Empty;
                newWorld = 0;
                selectedGroup = newGroup;
                config.Groups.Add(newGroup);
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Create new group assignment");
            ImGui.SameLine();
            
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog)) {
                selectedCharacter = null;
                selectedName = string.Empty;
                selectedWorld = 0;
                newName = string.Empty;
                newWorld = 0;
                selectedGroup = null;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Plugin Options");
            iconButtonSize = ImGui.GetItemRectSize() + ImGui.GetStyle().ItemSpacing;

            if (!config.HideKofi) {
                ImGui.SameLine();
                if (kofiButtonOffset > 0) ImGui.SetCursorPosX(MathF.Max(ImGui.GetCursorPosX(), charaListPos - kofiButtonOffset + ImGui.GetStyle().WindowPadding.X));
                if (ImGuiComponents.IconButtonWithText(FontAwesomeIcon.Coffee, "Support", new Vector4(1, 0.35f, 0.35f, 1f), new Vector4(1, 0.35f, 0.35f, 0.9f), new Vector4(1, 0.35f, 0.35f, 75f))) {
                    Util.OpenLink("https://ko-fi.com/Caraxi");
                }
                if (ImGui.IsItemHovered()) {
                    ImGui.SetTooltip("Support on Ko-fi");
                }
                kofiButtonOffset = ImGui.GetItemRectSize().X;
            }
        }
        ImGui.EndGroup();
        
        ImGui.SameLine();
        if (ImGui.BeginChild("character_view", ImGuiHelpers.ScaledVector2(0), true)) {
            if (selectedCharacter != null) {
                ShowDebugInfo();

                if (newWorld != 0) {
                    ImGui.InputText("Character Name", ref newName, 64);
                    var worldName = PluginService.Data.GetExcelSheet<World>()!.GetRow(newWorld)!.Name.ToDalamudString().TextValue;
                    if (ImGui.BeginCombo("World", worldName)) {

                        foreach (var w in PluginService.Data.GetExcelSheet<World>()!.Where(w => w.IsPublic).OrderBy(w => w.Name.ToDalamudString().TextValue, StringComparer.OrdinalIgnoreCase)) {
                            if (ImGui.Selectable($"{w.Name.ToDalamudString().TextValue}", w.RowId == newWorld)) {
                                newWorld = w.RowId;
                            }
                        }
                        ImGui.EndCombo();
                    }

                    if (ImGui.Button("Create Group")) {
                        var group = new GroupConfig() {
                            Label = $"Group from {selectedName}@{worldName}", 
                            SittingOffsetY = selectedCharacter.SittingOffsetY, 
                            SittingOffsetZ = selectedCharacter.SittingOffsetZ, 
                            HeelsConfig = selectedCharacter.HeelsConfig
                        };
                        var copy = JsonConvert.DeserializeObject<GroupConfig>(JsonConvert.SerializeObject(group));
                        if (copy != null) {
                            config.Groups.Add(copy);
                            selectedCharacter = null;
                            selectedName = string.Empty;
                            selectedWorld = 0;
                            selectedGroup = copy;
                        }
                    }
                    ImGui.SameLine();
                    var isModified = newName != selectedName || newWorld != selectedWorld; 
                    {
                        var newAlreadyExists = config.WorldCharacterDictionary.ContainsKey(newWorld) && config.WorldCharacterDictionary[newWorld].ContainsKey(newName);
                
                        ImGui.BeginDisabled(isModified == false || newAlreadyExists);
                        if (ImGui.Button("Move Character Config")) {
                            if (selectedCharacter != null && config.TryAddCharacter(newName, newWorld)) {
                                config.WorldCharacterDictionary[newWorld][newName] = selectedCharacter;
                                config.WorldCharacterDictionary[selectedWorld].Remove(selectedName);
                                if (config.WorldCharacterDictionary[selectedWorld].Count == 0) {
                                    config.WorldCharacterDictionary.Remove(selectedWorld);
                                }
                                selectedName = newName;
                                selectedWorld = newWorld;
                            }
                        }
                        ImGui.SameLine();
                        if (ImGui.Button("Copy Character Config")) {
                            if (config.TryAddCharacter(newName, newWorld)) {
                                var j = JsonConvert.SerializeObject(selectedCharacter);
                                config.WorldCharacterDictionary[newWorld][newName] = JsonConvert.DeserializeObject<CharacterConfig>(j) ?? new CharacterConfig();
                            }
                        }
                        ImGui.EndDisabled();

                        if (isModified && newAlreadyExists) {
                            ImGui.SameLine();
                            ImGui.TextDisabled("Character already exists in config.");
                        }
                    }
                    
                    ImGuiExt.Separator();
                }
                
                if (Plugin.IpcAssignedData.TryGetValue((selectedName, selectedWorld), out var data)) {
                    ImGui.Text("This character's offset is currently assigned by another plugin.");
                    if (Plugin.IsDebug && ImGui.Button("Clear IPC Assignment")) {
                        Plugin.IpcAssignedData.Remove((selectedName, selectedWorld));
                        Plugin.RequestUpdateAll();
                    }
                    
                    ImGui.Text($"Assigned Offset: {data.Offset}");
                    ImGui.Text($"Sitting Height: {data.SittingHeight}");
                    ImGui.Text($"Sitting Position: {data.SittingPosition}");
                    ImGui.Text($"GroundSit Height: {data.GroundSitHeight}");
                    ImGui.Text($"Sleep Height: {data.SleepHeight}");
                    
                } else {
                    DrawCharacterView(selectedCharacter);
                }
            }
            else if (selectedGroup != null) {

                ImGui.InputText("Group Label", ref selectedGroup.Label, 50);
                
                ImGuiExt.Separator();
                
                ImGui.Text("Apply group to characters using:");
                ImGui.Indent();

                if (ImGui.Checkbox("Masculine Model", ref selectedGroup.MatchMasculine)) {
                    if (selectedGroup is { MatchFeminine: false, MatchMasculine: false}) {
                        selectedGroup.MatchFeminine = true;
                    }
                }

                if (ImGui.Checkbox("Feminine Model", ref selectedGroup.MatchFeminine)) {
                    if (selectedGroup is { MatchFeminine: false, MatchMasculine: false}) {
                        selectedGroup.MatchMasculine = true;
                    }
                }

                ImGui.Unindent();
                
                ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(selectedGroup.Clans.Count == 0 ? ImGui.GetColorU32(ImGuiCol.TextDisabled) : ImGui.GetColorU32(ImGuiCol.Text)),"Apply group to characters of the clans:");

                if (selectedGroup.Clans.Count == 0) {
                    ImGui.SameLine();
                    ImGuiComponents.HelpMarker($"This group will apply to all characters{(selectedGroup.MatchFeminine && selectedGroup.MatchMasculine ? "" : selectedGroup.MatchFeminine ? " using a feminine model" : " using a masculine model") } as no clan is selected.");
                }
                
                ImGui.Indent();
                if (ImGui.BeginTable("clanTable", 4)) {
                    foreach (var clan in PluginService.Data.GetExcelSheet<Tribe>()!) {
                        if (clan.RowId == 0) continue;
                        
                        var isEnabled = selectedGroup.Clans.Count == 0 || selectedGroup.Clans.Contains(clan.RowId);

                        ImGui.TableNextColumn();
                        
                        ImGui.PushStyleColor(ImGuiCol.CheckMark, ImGui.GetColorU32(selectedGroup.Clans.Count == 0 ? ImGuiCol.TextDisabled : ImGuiCol.Text));
                        if (ImGui.Checkbox($"{clan.Masculine.ToDalamudString().TextValue}", ref isEnabled)) {
                            if (selectedGroup.Clans.Contains(clan.RowId)) {
                                selectedGroup.Clans.Remove(clan.RowId);
                            } else {
                                selectedGroup.Clans.Add(clan.RowId);
                            }
                        }
                        ImGui.PopStyleColor();

                    }
                    ImGui.EndTable();
                }
                
                ImGui.Unindent();

                if (ImGui.CollapsingHeader("Name Matching")) {

                    var nameMatchCharacter = -1;
                    foreach (var c in selectedGroup.Characters.ToArray()) {
                        ImGui.PushID($"group_character_{++nameMatchCharacter}");

                        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 140);
                        ImGui.InputText("##name", ref c.Name, 32);
                        
                        ImGui.SameLine();
                        ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 140);
                        if (ImGui.BeginCombo("##world", c.World == ushort.MaxValue ? "Non Player" : PluginService.Data.GetExcelSheet<World>()?.GetRow(c.World)?.Name.RawString ?? $"World#{c.World}", ImGuiComboFlags.HeightLargest)) {

                            var appearing = ImGui.IsWindowAppearing();
                            
                            if (appearing) {
                                groupNameMatchingWorldSearch = string.Empty;
                                ImGui.SetKeyboardFocusHere();
                            }
                            ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 140);
                            ImGui.InputTextWithHint("##search", "Search...", ref groupNameMatchingWorldSearch, 25);
                            var s = ImGui.GetItemRectSize();
                            ImGuiExt.Separator();
                            
                            if (ImGui.BeginChild("worldScroll", new Vector2(s.X, ImGuiHelpers.GlobalScale * 250))) {

                                var lastDc = uint.MaxValue;
                                void World(string name, uint worldId, WorldDCGroupType? dc = null) {


                                    if (!string.IsNullOrWhiteSpace(groupNameMatchingWorldSearch)) {
                                        if (!name.Contains(groupNameMatchingWorldSearch, StringComparison.InvariantCultureIgnoreCase)) return;
                                    }
                                    
                                    if (dc != null) {
                                        if (lastDc != dc.RowId) {
                                            lastDc = dc.RowId;
                                            ImGui.TextDisabled($"{dc.Name.RawString}");
                                        }
                                    }
                                    
                                    if (ImGui.Selectable($"    {name}", c.World == worldId)) {
                                        c.World = worldId;
                                        ImGui.CloseCurrentPopup();
                                    }
                                    
                                    if (appearing && c.World == worldId) {
                                        ImGui.SetScrollHereY();
                                    }
                                    
                                }

                                World("Non Player", ushort.MaxValue);
                                foreach (var w in PluginService.Data.GetExcelSheet<World>()!.Where(w => w.IsPublic).OrderBy(w => w.DataCenter.Value?.Name.RawString).ThenBy(w => w.Name.RawString)) {
                                    World(w.Name.RawString, w.RowId, w.DataCenter.Value);
                                }
                            }
                            ImGui.EndChild();
                            ImGui.EndCombo();
                        }
                        
                        ImGui.SameLine();
                        if (ImGuiComponents.IconButton(FontAwesomeIcon.Trash)) {
                            selectedGroup.Characters.RemoveAt(nameMatchCharacter);
                        }
                        
                        ImGui.PopID();
                    }
                    
                    ImGui.PushID($"group_character_{++nameMatchCharacter}");
                    ImGui.SetNextItemWidth(ImGuiHelpers.GlobalScale * 140);
                    if (ImGui.InputText("##name", ref groupNameMatchingNewInput, 32)) {
                        var c = new GroupCharacter() {
                            Name = groupNameMatchingNewInput,
                        };
                        selectedGroup.Characters.Add(c);
                        groupNameMatchingNewInput = string.Empty;
                    }
                    
                    ImGui.PopID();
                }

                ImGuiExt.Separator();
                DrawCharacterView(selectedGroup);
            } else {

                var changelogVisible = Changelog.Show(config);
                
                ImGui.Text("SimpleHeels Options");

                if (!changelogVisible) {
                    ImGui.SameLine();
                    ImGui.SetCursorPosX(ImGui.GetContentRegionMax().X - ImGui.CalcTextSize("changelogs").X - ImGui.GetStyle().FramePadding.X * 2);
                    if (ImGui.SmallButton("changelogs")) {
                        config.DismissedChangelog = 0f;
                    }
                }
                
                ImGuiExt.Separator();

                if (ImGui.Checkbox("Enabled", ref config.Enabled)) {
                    Plugin.RequestUpdateAll();
                }
                ImGui.SameLine();
                ImGuiComponents.HelpMarker("Can be toggled using commands:\n\t/heels toggle\n\t/heels enable\n\t/heels disable");
                ImGui.Checkbox("Hide Ko-fi Support button", ref config.HideKofi);
                if (ImGui.Checkbox("Use model assigned offsets", ref config.UseModelOffsets)) {
                    Plugin.RequestUpdateAll();
                }
                ImGui.SameLine();
                ImGuiComponents.HelpMarker("Allows mod developers to assign an offset to a modded item.\nClick this for more information.");
                if (ImGui.IsItemClicked()) {
                    Util.OpenLink("https://github.com/Caraxi/SimpleHeels/blob/master/modguide.md");
                }

                ImGui.Checkbox("Show Plus/Minus buttons for offset adjustments", ref config.ShowPlusMinusButtons);
                if (config.ShowPlusMinusButtons) {
                    ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
                    ImGui.SliderFloat("Plus/Minus Button Delta", ref config.PlusMinusDelta, 0.0001f, 0.01f, "%.4f", ImGuiSliderFlags.AlwaysClamp);
                }
                
                ImGuiExt.Separator();
                ImGui.Text("Bypass Dalamud's plugin UI hiding:");
                ImGui.Indent();
                if (ImGui.Checkbox("In GPose", ref config.ConfigInGpose)) {
                    PluginService.PluginInterface.UiBuilder.DisableGposeUiHide = config.ConfigInGpose;
                }

                if (ImGui.Checkbox("In Cutscene", ref config.ConfigInCutscene)) {
                    PluginService.PluginInterface.UiBuilder.DisableCutsceneUiHide = config.ConfigInCutscene;
                }
                ImGui.Unindent();
                ImGuiExt.Separator();

                #if DEBUG
                ImGui.Checkbox("[DEBUG] Open config window on startup", ref config.DebugOpenOnStartup);
                #endif

                if (Plugin.IsDebug) {
                    if (ImGui.TreeNode("DEBUG")) {
                        
                        ImGui.Text("Last Reported Data:");
                        ImGui.Indent();
                        ImGui.Text(ApiProvider.LastReportedData);
                        ImGui.Unindent();

                        ImGui.TreePop();
                    }

                    if (ImGui.TreeNode("PERFORMANCE")) {
                        PerformanceMonitors.DrawTable();
                        ImGui.TreePop();
                    }
                    
                    
                }
                

                if (config.UseModelOffsets && ImGui.CollapsingHeader("Model Offset Editor")) {
                    ShowModelEditor();
                }
            }
            
        }
        ImGui.EndChild();
    }

    private static FileDialogManager? _fileDialogManager;
    private float mdlEditorOffset = 0f;
    private Exception? mdlEditorException;
    private MdlFile? loadedFile;
    private string loadedFilePath = string.Empty;
    
    private void ShowModelEditor() {
        if (mdlEditorException != null) {
            ImGui.TextColored(ImGuiColors.DalamudRed, $"{mdlEditorException}");
        } else {
            ImGui.TextWrapped("This is a very simple editor that allows setting the offset for a mdl file.");
            ImGui.Spacing();
            ImGui.Text("- Select a file");
            ImGui.Text("- Set Offset");
            ImGui.Text("- Save the modified file");
            ImGui.Spacing();
            
            if (loadedFile != null) {
                var attributes = loadedFile.Attributes.ToList();
                
                
                ImGui.InputText("##loadedFile", ref loadedFilePath, 2048, ImGuiInputTextFlags.ReadOnly);
                var s = ImGui.GetItemRectSize();
                ImGui.SameLine();
                ImGui.Text("Loaded File");

                
                FloatEditor("Heels Offset", ref mdlEditorOffset, 0.001f, -1, 1, "%.5f", ImGuiSliderFlags.AlwaysClamp);
                var offset = attributes.FirstOrDefault(a => a.StartsWith("heels_offset="));
                if (offset == null) {
                    ImGui.Text("Model has no offset assigned.");
                } else {
                    ImGui.Text($"Current Offset: {offset[13..]}");
                }
                
                if (ImGui.Button("Save MDL File")) {
                    if (_fileDialogManager == null) {
                        _fileDialogManager = new FileDialogManager();
                        PluginService.PluginInterface.UiBuilder.Draw += _fileDialogManager.Draw;
                    }

                    try {
                        _fileDialogManager.SaveFileDialog("Save MDL File...", "MDL File{.mdl}", "output.mdl", ".mdl", (b, files) => {
                            attributes.RemoveAll(a => a.StartsWith("heels_offset="));
                            attributes.Add($"heels_offset={mdlEditorOffset.ToString(CultureInfo.InvariantCulture)}");
                            loadedFile.Attributes = attributes.ToArray();
                            var outputBytes = loadedFile.Write();
                            File.WriteAllBytes(files, outputBytes);
                            loadedFile = null;
                        }, Path.GetDirectoryName(loadedFilePath), true);
                    } catch (Exception ex) {
                        mdlEditorException = ex;
                    }
                }

                if (ImGui.Button("Cancel")) {
                    loadedFile = null;
                }
                
            } else {
                if (ImGui.Button("Select MDL File")) {
                    if (_fileDialogManager == null) {
                        _fileDialogManager = new FileDialogManager();
                        PluginService.PluginInterface.UiBuilder.Draw += _fileDialogManager.Draw;
                    }

                    try {
                        _fileDialogManager.OpenFileDialog("Select MDL File...", "MDL File{.mdl}", (b, files) => {
                            if (files.Count != 1) return;
                            loadedFilePath = files[0];
                            PluginService.Log.Info($"Loading MDL: {loadedFilePath}");
                           
                            config.ModelEditorLastFolder = Path.GetDirectoryName(loadedFilePath) ?? string.Empty;
                            var bytes = File.ReadAllBytes(loadedFilePath);
                            loadedFile = new MdlFile(bytes);
                            var attributes = loadedFile.Attributes.ToList();
                            var offset = attributes.FirstOrDefault(a => a.StartsWith("heels_offset="));
                        
                            if (offset != null) {
                                if (!float.TryParse(offset[13..], CultureInfo.InvariantCulture, out mdlEditorOffset)) {
                                    mdlEditorOffset = 0;
                                }
                            }

                        }, 1, config.ModelEditorLastFolder, true);
                    } catch (Exception ex) {
                        mdlEditorException = ex;
                    }
                }
            }
        }
    }

    private string footwearSearch = string.Empty;

    private int beginDrag = -1;
    private int endDrag = -1;
    private Vector2 endDragPosition = new();
    
    private Vector2 firstCheckboxScreenPosition = new(0);
    private unsafe void DrawCharacterView(CharacterConfig? characterConfig) {
        if (characterConfig == null) return;

        var wearingMatchCount = 0;
        var usingDefault = true;
        
        if (characterConfig.HeelsConfig.Count > 0 && !characterConfig.HeelsConfig.Any(hc => hc.Enabled)) {
            ImGui.BeginGroup();
            ImGui.PushStyleColor(ImGuiCol.Text, ImGuiColors.DalamudOrange);
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text(FontAwesomeIcon.ExclamationTriangle.ToIconString());
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.TextWrapped("All heel config options are currently disabled on this character. Click the check box under the 'Enable' heading to enable an entry to begin applying heels offsets.");
            ImGui.PopStyleColor();
            ImGui.EndGroup();

            if (ImGui.IsItemHovered() && firstCheckboxScreenPosition is not { X : 0, Y : 0 }) {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                var dl = ImGui.GetForegroundDrawList();
                dl.AddLine(ImGui.GetMousePos(), firstCheckboxScreenPosition, ImGui.GetColorU32(ImGuiColors.DalamudOrange), 2);
            }
        }

        GameObject* activeCharacter = null;
        Character* activeCharacterAsCharacter = null;
        HeelConfig? activeHeelConfig = null;
        
        
        if (characterConfig is GroupConfig) {
            var target = PluginService.Targets.SoftTarget ?? PluginService.Targets.Target;
            if (target is Dalamud.Game.ClientState.Objects.Types.Character) {
                activeCharacter = (GameObject*)target.Address;
                activeCharacterAsCharacter = (Character*)activeCharacter;
            }
        } else {
            var player = PluginService.Objects.FirstOrDefault(t => t is PlayerCharacter playerCharacter && playerCharacter.Name.TextValue == selectedName && playerCharacter.HomeWorld.Id == selectedWorld);
            if (player is PlayerCharacter) {
                activeCharacter = (GameObject*)player.Address;
                activeCharacterAsCharacter = (Character*)activeCharacter;

                if (activeCharacter->DrawObject != null && activeCharacter->DrawObject->Object.GetObjectType() == ObjectType.CharacterBase) {
                    var cb = (CharacterBase*)activeCharacter->DrawObject;
                    if (cb->GetModelType() == CharacterBase.ModelType.Human) {
                        activeHeelConfig = characterConfig.GetFirstMatch((Human*)cb);
                    }
                }
            }
        }
        
        var activeFootwear = GetModelIdForPlayer(activeCharacter, ModelSlot.Feet);
        var activeFootwearPath = GetModelPathForPlayer(activeCharacter, ModelSlot.Feet);
    
        var activeTop = GetModelIdForPlayer(activeCharacter, ModelSlot.Top);
        var activeTopPath = GetModelPathForPlayer(activeCharacter, ModelSlot.Top);
    
        var activeLegs = GetModelIdForPlayer(activeCharacter, ModelSlot.Legs);
        var activeLegsPath = GetModelPathForPlayer(activeCharacter, ModelSlot.Legs);
        
        var windowMax = ImGui.GetWindowPos() + ImGui.GetWindowSize();
        if (ImGui.BeginTable("OffsetsTable", 5)) {
            ImGui.TableSetupColumn("Enable", ImGuiTableColumnFlags.WidthFixed, checkboxSize * 4 + 3 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Offset", ImGuiTableColumnFlags.WidthFixed, (90 + (config.ShowPlusMinusButtons ? 50 : 0)) * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Clothing", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, checkboxSize);

            TableHeaderRow(TableHeaderAlign.Right, TableHeaderAlign.Center, TableHeaderAlign.Center, TableHeaderAlign.Left);
            var deleteIndex = -1;
            for (var i = 0; i < characterConfig.HeelsConfig.Count; i++) {
                ImGui.BeginDisabled(beginDrag == i);
                ImGui.PushID($"heels_{i}");
                var heelConfig = characterConfig.HeelsConfig[i];
                heelConfig.Label ??= string.Empty;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.One * ImGuiHelpers.GlobalScale);
                ImGui.PushFont(UiBuilder.IconFont);

                if (ImGui.Button($"{(char)(heelConfig.Locked ? FontAwesomeIcon.Lock : FontAwesomeIcon.LockOpen)}", new Vector2(checkboxSize))) {
                    if (heelConfig.Locked == false || ImGui.GetIO().KeyShift)
                        heelConfig.Locked = !heelConfig.Locked;
                }
                
                if (ImGui.IsItemHovered()) {
                    ImGui.PopFont();
                    if (heelConfig.Locked && ImGui.GetIO().KeyShift) {
                        ImGui.SetTooltip("Unlock Entry");
                    } else if (heelConfig.Locked) {
                        ImGui.SetTooltip("Hold SHIFT to unlock");
                    } else {
                        ImGui.SetTooltip("Lock Entry");
                    }
                    ImGui.PushFont(UiBuilder.IconFont);
                }
                
                if (beginDrag >= 0 && MouseWithin(ImGui.GetItemRectMin(), new Vector2(windowMax.X, ImGui.GetItemRectMax().Y))) {
                    endDrag = i;
                    endDragPosition = ImGui.GetItemRectMin();
                }
                
                ImGui.SameLine();

                if (beginDrag != i && heelConfig.Locked) ImGui.BeginDisabled(heelConfig.Locked);
                
                if (ImGui.Button($"{(char)FontAwesomeIcon.Trash}##delete", new Vector2(checkboxSize)) && ImGui.GetIO().KeyShift) {
                    deleteIndex = i;
                }

                if (ImGui.IsItemHovered() && !ImGui.GetIO().KeyShift) {
                    ImGui.PopFont();
                    ImGui.SetTooltip("Hold SHIFT to delete.");
                    ImGui.PushFont(UiBuilder.IconFont);
                }

                ImGui.SameLine();
                if (beginDrag != i && heelConfig.Locked) ImGui.EndDisabled();
                ImGui.Button($"{(char)FontAwesomeIcon.ArrowsUpDown}", new Vector2(checkboxSize));
                if (beginDrag == -1 && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                    beginDrag = i;
                    endDrag = i;
                    endDragPosition = ImGui.GetItemRectMin();
                }


                ImGui.SameLine();
                ImGui.PopFont();
                ImGui.PopStyleVar();
                if (ImGui.Checkbox("##enable", ref heelConfig.Enabled)) {
                    if (heelConfig.Enabled) {
                        foreach (var heel in characterConfig.GetDuplicates(heelConfig, true)) {
                            heel.Enabled = false;
                        }
                        heelConfig.Enabled = true;
                    }
                }

                if (i == 0) {
                    firstCheckboxScreenPosition = ImGui.GetItemRectMin() + ImGui.GetItemRectSize() / 2;
                }

                if (ImGui.IsItemHovered()) {
                    ImGui.BeginTooltip();

                    if (heelConfig.Enabled) {
                        ImGui.Text("Click to disable heel config entry.");
                    } else {
                        ImGui.Text("Click to enable heel config entry.");
                        var match = characterConfig.GetDuplicates(heelConfig, true).FirstOrDefault();
                        if (match != null) {
                            if (!string.IsNullOrWhiteSpace(match.Label)) {
                                ImGui.TextDisabled($"'{match.Label}' will be disabled as it affects the same items.");
                            } else {
                                ImGui.TextDisabled($"An entry affecting the same items will be disabled.");
                            }
                        }
                    }
                    
                    ImGui.EndTooltip();
                }
                

                if (i == 0) checkboxSize = ImGui.GetItemRectSize().X;

                if (beginDrag != i && heelConfig.Locked) ImGui.BeginDisabled(heelConfig.Locked);
                
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.InputText("##label", ref heelConfig.Label, 100);
                
                ImGui.TableNextColumn();

                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (FloatEditor("##offset", ref heelConfig.Offset, 0.001f, float.MinValue, float.MaxValue, "%.5f")) {
                    if (heelConfig.Enabled) Plugin.RequestUpdateAll();
                }
                
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                
                var pathMode = heelConfig.PathMode;
                var pathDisplay = heelConfig.Path ?? string.Empty;
                if (pathMode) {
                    pathDisplay = pathDisplay.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                    if (!string.IsNullOrWhiteSpace(PenumbraModFolder) && !string.IsNullOrWhiteSpace(pathDisplay) && pathDisplay.StartsWith(PenumbraModFolder, StringComparison.InvariantCultureIgnoreCase)) {
                        pathDisplay = "[Penumbra] " + (heelConfig.Slot != ModelSlot.Feet ? $"[{heelConfig.Slot}] " : "") + pathDisplay.Remove(0, PenumbraModFolder.Length);
                    } else {
                        pathDisplay = (heelConfig.Slot != ModelSlot.Feet ? $"[{heelConfig.Slot}] " : "") + pathDisplay;
                    }
                }
                
                if (ImGui.BeginCombo("##footwear", pathMode ? pathDisplay : GetModelName(heelConfig.ModelId, heelConfig.Slot), ImGuiComboFlags.HeightLargest)) {
                    if (ImGui.BeginTabBar("##footwear_tabs")) {
                        if (pathMode) {
                            if (ImGui.TabItemButton("Model ID")) {
                                heelConfig.PathMode = false;
                                (heelConfig.Slot, heelConfig.RevertSlot) = (heelConfig.RevertSlot, heelConfig.Slot);
                            }
                        }
                        
                        if (ImGui.BeginTabItem((pathMode ? "Model Path" : "Model ID") + "###currentConfigType")) {
                            if (pathMode) {
                                heelConfig.Path ??= string.Empty;
                                ImGui.TextWrapped("Assign offset based on the file path of the model, this can be a game path or a penumbra mod path.");
                                ImGui.TextDisabled("File Path:");
                                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                                ImGui.InputText("##pathInput", ref heelConfig.Path, 1024);

                                ImGui.TextDisabled("Equip Slot:");
                                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                                if (ImGui.BeginCombo("##slotInput", $"{heelConfig.Slot}")) {
                                    if (ImGui.Selectable($"{ModelSlot.Top}", heelConfig.Slot == ModelSlot.Top)) heelConfig.Slot = ModelSlot.Top;
                                    if (ImGui.Selectable($"{ModelSlot.Legs}", heelConfig.Slot == ModelSlot.Legs)) heelConfig.Slot = ModelSlot.Legs;
                                    if (ImGui.Selectable($"{ModelSlot.Feet}", heelConfig.Slot == ModelSlot.Feet)) heelConfig.Slot = ModelSlot.Feet;
                                    ImGui.EndCombo();
                                }
                                
                                var activeSlotPath = heelConfig.Slot switch {
                                    ModelSlot.Top => activeTopPath,
                                    ModelSlot.Legs => activeLegsPath,
                                    _ => activeFootwearPath,
                                };
                                
                                if (activeSlotPath != null) {
                                    if (ImGui.Button("Current Model Path")) {
                                        heelConfig.Path = activeSlotPath;
                                    }

                                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                                        ImGui.SetTooltip(activeSlotPath);
                                    }
                                }
                                
                                
                                
                            } else {
                                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                                if (ImGui.IsWindowAppearing()) {
                                    footwearSearch = string.Empty;
                                    ImGui.SetKeyboardFocusHere();
                                }
                                ImGui.InputTextWithHint("##footwearSearch", "Search...", ref footwearSearch, 100);
                    
                                if (ImGui.BeginChild("##footwearSelectScroll", new Vector2(-1, 200))) {
                                    foreach (var shoeModel in shoeModelList.Value.Values) {
                                        if (!string.IsNullOrWhiteSpace(footwearSearch)) {
                                            if (!((ushort.TryParse(footwearSearch, out var searchId) && searchId == shoeModel.Id) || (shoeModel.Name ?? $"Unknown#{shoeModel.Id}").Contains(footwearSearch, StringComparison.InvariantCultureIgnoreCase) || shoeModel.Items.Any(shoeItem => shoeItem.Name.ToDalamudString().TextValue.Contains(footwearSearch, StringComparison.InvariantCultureIgnoreCase)))) {
                                                continue;
                                            }
                                        }
                            
                                        if (ImGui.Selectable($"{shoeModel.Name}##shoeModel_{shoeModel.Id}")) {
                                            heelConfig.ModelId = shoeModel.Id;
                                            heelConfig.Slot = shoeModel.Slot;
                                            ImGui.CloseCurrentPopup();
                                        }

                                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && shoeModel.Items.Count > 3) {
                                            ShowModelTooltip(shoeModel.Id, shoeModel.Slot);
                                        }
                                    }
                                }
                    
                                ImGui.EndChild();
                            }
                            
                            ImGui.EndTabItem();
                        }
                            
                        if (!pathMode) {
                            if (ImGui.TabItemButton("Model Path")) {
                                heelConfig.PathMode = true;
                                (heelConfig.Slot, heelConfig.RevertSlot) = (heelConfig.RevertSlot, heelConfig.Slot);
                            }
                        }
                        
                        ImGui.EndTabBar();
                    }
                    
                    ImGui.EndCombo();
                }
                
                ImGui.TableNextColumn();
                ImGui.EndDisabled();
                if (characterConfig is not GroupConfig) {
                    if ((heelConfig.Slot == ModelSlot.Feet && ((heelConfig.PathMode == false && activeFootwear == heelConfig.ModelId) || (heelConfig.PathMode && activeFootwearPath != null && activeFootwearPath.Equals(heelConfig.Path, StringComparison.OrdinalIgnoreCase)))) 
                        || (heelConfig.Slot == ModelSlot.Legs && ((heelConfig.PathMode == false && activeLegs == heelConfig.ModelId) || (heelConfig.PathMode && activeLegsPath != null && activeLegsPath.Equals(heelConfig.Path, StringComparison.OrdinalIgnoreCase)))) 
                        || (heelConfig.Slot == ModelSlot.Top && ((heelConfig.PathMode == false && activeTop == heelConfig.ModelId) || (heelConfig.PathMode && activeTopPath != null && activeTopPath.Equals(heelConfig.Path, StringComparison.OrdinalIgnoreCase))))) {
                    
                        ImGui.PushFont(UiBuilder.IconFont);
                        if (heelConfig.Enabled) {
                            if (activeCharacter->IsCharacter() && ((Character*)activeCharacter)->Mode == Character.CharacterModes.InPositionLoop) {
                                ImGui.TextColored(ImGuiColors.DalamudViolet, $"{(char)FontAwesomeIcon.ArrowLeft}");
                            } else {
                                ImGui.TextColored(activeHeelConfig == heelConfig ? ImGuiColors.HealerGreen : ImGuiColors.DalamudYellow,$"{(char)FontAwesomeIcon.ArrowLeft}");
                            }
                        } else {
                            ImGui.TextDisabled($"{(char)FontAwesomeIcon.ArrowLeft}");
                        }
                        
                        ImGui.PopFont();
                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                            ImGui.BeginTooltip();
                            ImGui.Text("Currently Wearing");
                            if (heelConfig.Enabled) {
                                if (activeCharacter->IsCharacter() && ((Character*)activeCharacter)->Mode == Character.CharacterModes.InPositionLoop) {
                                    ImGui.TextColored(ImGuiColors.DalamudViolet, $"This entry is INACTIVE because the character is {(((Character*)activeCharacter)->ModeParam is 1 or 2 ? "sitting" : "sleeping")}.");
                                } else if (activeHeelConfig == heelConfig) {
                                    ImGui.TextColored(ImGuiColors.HealerGreen, "This entry is ACTIVE");
                                } else {
                                    ImGui.TextColored(ImGuiColors.DalamudYellow, "This entry is INACTIVE because another entry is applied first.");
                                }
                            } else {
                                ImGui.TextDisabled("This entry is INACTIVE because it is disabled.");
                            }
                            ImGui.EndTooltip();
                        }

                        if (heelConfig.Enabled) {
                            wearingMatchCount++;
                            usingDefault = false;
                        }
                    }
                }

                ImGui.PopID();
                
            }

            if (deleteIndex >= 0) {
                characterConfig.HeelsConfig.RemoveAt(deleteIndex);
            }

            ImGui.EndTable();

            if (wearingMatchCount > 1) {
                ImGui.BeginGroup();
                ImGui.PushStyleColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled));
                ImGui.PushFont(UiBuilder.IconFont);
                ImGui.Text(FontAwesomeIcon.InfoCircle.ToIconString());
                ImGui.PopFont();
                ImGui.SameLine();
                ImGui.TextWrapped("You are wearing items that match multiple enabled config entries.");
                ImGui.PopStyleColor();
                ImGui.EndGroup();
                if (ImGui.IsItemHovered()) {
                    ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                    ImGui.BeginTooltip();
                    ImGui.Text("Offsets will be applied to: ");
                    ImGui.Text(" - The first enabled '[Top]' option you are wearing.");
                    ImGui.Text(" - Then the first enabled '[Legs]' option you are wearing.");
                    ImGui.Text(" - Finally the first enabled feet option you are wearing.");
                    ImGui.EndTooltip();
                }
            }
            

            if (beginDrag >= 0) {
                if (!ImGui.IsMouseDown(ImGuiMouseButton.Left)) {
                    if (endDrag != beginDrag) {
                        var move = characterConfig.HeelsConfig[beginDrag];
                        characterConfig.HeelsConfig.RemoveAt(beginDrag);
                        characterConfig.HeelsConfig.Insert(endDrag, move);
                    }

                    beginDrag = -1;
                    endDrag = -1;
                } else {
                    var dl = ImGui.GetWindowDrawList();
                    dl.AddLine(endDragPosition, endDragPosition + new Vector2(ImGui.GetWindowContentRegionMax().X, 0), ImGui.GetColorU32(ImGuiCol.DragDropTarget), 2 * ImGuiHelpers.GlobalScale);
                }
            }
            
            bool ShowAddButton(ushort id, ModelSlot slot) {
                if (shoeModelList.Value.ContainsKey((id, slot))) {
                    if (ImGui.Button($"Add an Entry for {GetModelName(id, slot)}")) {
                        characterConfig.HeelsConfig.Add(new HeelConfig() {
                            ModelId = id,
                            Slot = slot,
                            Enabled = !characterConfig.HeelsConfig.Any(h => h is { PathMode: false } && h.ModelId == id)
                        });
                    }
                    return true;
                }
                return false;
            }

            
            void ShowAddPathButton(string? path, ModelSlot slot) {
                if (ImGui.Button($"Add Path: {path}")) {
                    characterConfig.HeelsConfig.Add(new HeelConfig() {
                        PathMode = true,
                        Path = path,
                        Slot = slot,
                        Enabled = !characterConfig.HeelsConfig.Any(h => h is {PathMode: true } && h.Path == path)
                    });
                }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                    ImGui.SetTooltip($"{path}");
                }
                
            }

            if (ImGui.GetIO().KeyShift && (ImGui.GetIO().KeyCtrl ? activeLegsPath : ImGui.GetIO().KeyAlt ? activeTopPath : activeFootwearPath) != null) {
                ShowAddPathButton(ImGui.GetIO().KeyCtrl ? activeLegsPath : ImGui.GetIO().KeyAlt ? activeTopPath : activeFootwearPath, ImGui.GetIO().KeyCtrl ? ModelSlot.Legs : ImGui.GetIO().KeyAlt ? ModelSlot.Top : ModelSlot.Feet);
            } else {
                if (!(ShowAddButton(activeTop, ModelSlot.Top) || ShowAddButton(activeLegs, ModelSlot.Legs) || ShowAddButton(activeFootwear, ModelSlot.Feet))) {
                    if (ImGui.Button($"Add New Entry")) {
                        characterConfig.HeelsConfig.Add(new HeelConfig() {
                            ModelId = 0,
                            Slot = ModelSlot.Feet,
                            Enabled = !characterConfig.HeelsConfig.Any(h => h is { PathMode: false, ModelId: 0 })
                        });
                    }
                }
            }
            
            if (characterConfig.HeelsConfig.Count == 0) {
                heelsConfigPath ??= Path.Join(PluginService.PluginInterface.ConfigFile.DirectoryName, "HeelsPlugin.json");
                heelsConfigExists ??= File.Exists(heelsConfigPath);
                if (heelsConfigExists is true && ImGui.Button("Import from Heels Plugin")) {
                    var heelsJson = File.ReadAllText(heelsConfigPath);
                    var heelsConfig = JsonConvert.DeserializeObject<HeelsPlugin.Configuration>(heelsJson);
                    if (heelsConfig == null) {
                        heelsConfigExists = false;
                    } else {
                        foreach (var e in heelsConfig.Configs) {
                            characterConfig.HeelsConfig.Add(new HeelConfig() {
                                Enabled = e.Enabled,
                                Label = e.Name ?? string.Empty,
                                ModelId = (ushort)e.ModelMain,
                                Offset = e.Offset,
                            });
                        }
                    }
                }
            }
        }
        
        ImGuiExt.Separator();

        ShowSittingOffsetEditor(characterConfig);
        
        if (activeCharacterAsCharacter != null && activeCharacterAsCharacter->Mode == Character.CharacterModes.InPositionLoop && activeCharacterAsCharacter->ModeParam == 2) {
            usingDefault = false;
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(ImGuiColors.HealerGreen,$"{(char)FontAwesomeIcon.ArrowLeft}");
            ImGui.PopFont();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.BeginTooltip();
                ImGui.Text("Currently Active");
                ImGui.EndTooltip();
            }
        }

        FloatEditor("Ground Sitting Offset", ref characterConfig.GroundSitOffset, 0.001f);
        if (activeCharacterAsCharacter != null && activeCharacterAsCharacter->Mode == Character.CharacterModes.InPositionLoop && activeCharacterAsCharacter->ModeParam == 1) {
            usingDefault = false;
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(ImGuiColors.HealerGreen,$"{(char)FontAwesomeIcon.ArrowLeft}");
            ImGui.PopFont();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.BeginTooltip();
                ImGui.Text("Currently Active");
                ImGui.EndTooltip();
            }
        }

        FloatEditor("Sleeping Offset", ref characterConfig.SleepOffset, 0.001f);
        if (activeCharacterAsCharacter != null && activeCharacterAsCharacter->Mode == Character.CharacterModes.InPositionLoop && activeCharacterAsCharacter->ModeParam == 3) {
            usingDefault = false;
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(ImGuiColors.HealerGreen,$"{(char)FontAwesomeIcon.ArrowLeft}");
            ImGui.PopFont();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.BeginTooltip();
                ImGui.Text("Currently Active");
                ImGui.EndTooltip();
            }
        }
        
        
        ImGuiExt.Separator();

        FloatEditor("Default Offset", ref characterConfig.DefaultOffset, 0.001f);
        ImGui.SameLine();
        ImGuiComponents.HelpMarker("The default offset will be used for all footwear that has not been configured.");
        
        if (activeCharacterAsCharacter != null && usingDefault) {
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.TextColored(ImGuiColors.HealerGreen,$"{(char)FontAwesomeIcon.ArrowLeft}");
            ImGui.PopFont();
            if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
                ImGui.BeginTooltip();
                ImGui.Text("Currently Active");
                ImGui.EndTooltip();
            }
        }
        
        
        
    }

    private void ShowSittingOffsetEditor(CharacterConfig characterConfig) {
        ImGui.BeginGroup();
        var sittingPositionChanged = FloatEditor("Sitting Height Offset", ref characterConfig.SittingOffsetY, 0.001f, -3f, 3f);
        sittingPositionChanged |= FloatEditor("Sitting Position Offset", ref characterConfig.SittingOffsetZ, 0.001f, -1f, 1f);

        if (sittingPositionChanged) {
            if (characterConfig is GroupConfig) {
                plugin.TryUpdateSittingPositions();
            } else {
                plugin.TryUpdateSittingPosition(selectedName, selectedWorld);
            }
            
        }
        
        ImGui.EndGroup();
    }

    private string? heelsConfigPath;
    private bool? heelsConfigExists;
    
    

    private readonly Lazy<Dictionary<(ushort, ModelSlot), ShoeModel>> shoeModelList = new(() => {
        var dict = new Dictionary<(ushort, ModelSlot), ShoeModel> {
            [(0, ModelSlot.Feet)] = new() { Id = 0, Name = "Smallclothes (Barefoot)"},
        };

        foreach (var item in PluginService.Data.GetExcelSheet<Item>()!.Where(i => i.EquipSlotCategory?.Value?.Feet != 0)) {
            if (item.ItemUICategory.Row is not (35 or 36 or 38)) continue;
            
            var modelBytes = BitConverter.GetBytes(item.ModelMain);
            var modelId = BitConverter.ToUInt16(modelBytes, 0);

            var slot = item.ItemUICategory.Row switch {
                35 => ModelSlot.Top,
                36 => ModelSlot.Legs,
                _ => ModelSlot.Feet
            };
            
            if (!dict.ContainsKey((modelId, slot))) dict.Add((modelId, slot), new ShoeModel {
                Id = modelId, Slot = slot
            });
            
            dict[(modelId, slot)].Items.Add(item);
            dict[(modelId, slot)].Name = null;
        }

        return dict;
    });

    private class ShoeModel {
        public ushort Id;
        public ModelSlot Slot;
        public readonly List<Item> Items = new();

        private string? nameCache;
        public string? Name {
            get {
                if (nameCache != null) return nameCache;
                nameCache = Items.Count switch {
                    0 => $"Unknwon#{Id}",
                    1 => Items[0].Name.ToDalamudString().TextValue,
                    2 => string.Join(" and ", Items.Select(i => i.Name.ToDalamudString().TextValue)),
                    > 3 => $"{Items[0].Name.ToDalamudString().TextValue} & {Items.Count - 1} others.",
                    _ => string.Join(", ", Items.Select(i => i.Name.ToDalamudString().TextValue))
                };

                switch (Slot) {
                    case ModelSlot.Legs: {
                        nameCache = $"[Legs] {nameCache}";
                        break;
                    }
                    case ModelSlot.Top: {
                        nameCache = $"[Top] {nameCache}";
                        break;
                    }
                }
                

                return nameCache;
            }
            set => nameCache = value;
        }
    }

    private static unsafe ushort GetModelIdForPlayer(GameObject* obj, ModelSlot slot) {
        if (obj == null) return ushort.MaxValue;
        if (obj->DrawObject == null) return ushort.MaxValue;
        if (obj->DrawObject->Object.GetObjectType() != ObjectType.CharacterBase) return ushort.MaxValue;
        var characterBase = (CharacterBase*)obj->DrawObject;
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human) return ushort.MaxValue;
        var human = (Human*)obj->DrawObject;
        if (human == null) return ushort.MaxValue;
        return slot switch {
            ModelSlot.Feet => human->Feet.Id,
            ModelSlot.Top => human->Top.Id,
            ModelSlot.Legs => human->Legs.Id,
            _ => ushort.MaxValue,
        };
    }
    
    private static unsafe string? GetModelPathForPlayer(GameObject* obj, ModelSlot slot) {
        if (obj == null) return null;
        if (obj->DrawObject == null) return null;
        if (obj->DrawObject->Object.GetObjectType() != ObjectType.CharacterBase) return null;
        var characterBase = (CharacterBase*)obj->DrawObject;
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human) return null;
        var human = (Human*)obj->DrawObject;
        if (human == null) return null;
        if ((byte)slot > human->CharacterBase.SlotCount) return null;
        var modelArray = human->CharacterBase.Models;
        if (modelArray == null) return null;
        var feetModel = modelArray[(byte) slot];
        if (feetModel == null) return null;
        var modelResource = feetModel->ModelResourceHandle;
        if (modelResource == null) return null;

        return modelResource->ResourceHandle.FileName.ToString();
    }
    
    private string? GetModelName(ushort modelId, ModelSlot slot, bool nullOnNoMatch = false) {
        if (modelId == 0) return "Smallclothes" + (slot == ModelSlot.Feet ? " (Barefoot)" : "");

        if (shoeModelList.Value.TryGetValue((modelId, slot), out var shoeModel)) {
            return shoeModel.Name ?? $"Unknown#{modelId}";
        }
        
        return nullOnNoMatch ? null : $"Unknown#{modelId}";
    }

    private void ShowModelTooltip(ushort modelId, ModelSlot slot) {
        
        ImGui.BeginTooltip();

        try {
            if (modelId == 0) {
                ImGui.Text("Smallclothes (Barefoot)");
                return;
            }

            if (shoeModelList.Value.TryGetValue((modelId, slot), out var shoeModel)) {

                foreach (var i in shoeModel.Items) {
                    ImGui.Text($"{i.Name.ToDalamudString().TextValue}");
                }
                
            } else {
                ImGui.Text($"Unknown Item (Model#{modelId})");
            }
        } finally {
            ImGui.EndTooltip();
        }
    }

    public override void OnClose() {
        PluginService.PluginInterface.SavePluginConfig(config);
        base.OnClose();
    }
    
    private bool FloatEditor(string label, ref float value, float speed = 1, float min = float.MinValue, float max = float.MaxValue, string format = "%.5f", ImGuiSliderFlags flags = ImGuiSliderFlags.None) {
        var c = false;
        var w = ImGui.CalcItemWidth();

        if (config.ShowPlusMinusButtons) {
            if (ImGuiComponents.IconButton($"##{label}_minus", FontAwesomeIcon.Minus) || (holdingClick.ElapsedMilliseconds > 500 && clickHoldThrottle.ElapsedMilliseconds > 50 && ImGui.IsItemHovered() && ImGui.IsMouseDown(ImGuiMouseButton.Left))) {
                clickHoldThrottle.Restart();
                value -= config.PlusMinusDelta;
                c = true;
            }

            w -= ImGui.GetItemRectSize().X * 2 + ImGui.GetStyle().ItemSpacing.X * 2;
            ImGui.SameLine();
                    
        }
        ImGui.SetNextItemWidth(w);
        
        c |= ImGui.DragFloat($"##{label}_slider", ref value, speed, min,  max, format, flags);
        if (config.ShowPlusMinusButtons) {
            ImGui.SameLine();
            if (ImGuiComponents.IconButton($"##{label}_plus", FontAwesomeIcon.Plus) || (holdingClick.ElapsedMilliseconds > 500 && clickHoldThrottle.ElapsedMilliseconds > 50 && ImGui.IsItemHovered() && ImGui.IsMouseDown(ImGuiMouseButton.Left))) {
                clickHoldThrottle.Restart();
                value += MathF.Round(config.PlusMinusDelta, 5, MidpointRounding.AwayFromZero);
                c = true;
            }
        }
        ImGui.SameLine();
        ImGui.Text(label.Split("##")[0]);
        
        
        return c;
    }

    private bool MouseWithin(Vector2 min, Vector2 max) {
        var mousePos = ImGui.GetMousePos();
        return mousePos.X >= min.X && mousePos.Y <= max.X && mousePos.Y >= min.Y && mousePos.Y <= max.Y;
    }


    private enum TableHeaderAlign {
        Left,
        Center,
        Right
    }
    
    private void TableHeaderRow(params TableHeaderAlign[] aligns) {
        ImGui.TableNextRow();
        for (var i = 0; i < ImGui.TableGetColumnCount(); i++) {
            ImGui.TableNextColumn();
            var label = ImGui.TableGetColumnName(i);
            ImGui.PushID($"TableHeader_{i}");
            var align = aligns.Length <= i ? TableHeaderAlign.Left : aligns[i];

            switch (align) {
                case TableHeaderAlign.Center: {

                    var textSize = ImGui.CalcTextSize(label);
                    var space = ImGui.GetContentRegionAvail().X;
                    ImGui.TableHeader("");
                    ImGui.SameLine(space / 2f - textSize.X / 2f);
                    ImGui.Text(label);

                    break;
                }
                case TableHeaderAlign.Right: {
                    ImGui.TableHeader("");
                    var textSize = ImGui.CalcTextSize(label);
                    var space = ImGui.GetContentRegionAvail().X;
                    ImGui.SameLine(space - textSize.X);
                    ImGui.Text(label);
                    break;
                }
                default:
                    ImGui.TableHeader(label);
                    break;
            }
            ImGui.PopID();
        }
    }
    
    public void ToggleWithWarning() {
        if (IsOpen && hiddenStopwatch.ElapsedMilliseconds < 1000) {
            IsOpen = false;
        } else {
            IsOpen = true;
            notVisibleWarningCancellationTokenSource?.Cancel();
            notVisibleWarningCancellationTokenSource = new CancellationTokenSource();
            PluginService.Framework.RunOnTick(() => {
                if (notVisibleWarningCancellationTokenSource == null || notVisibleWarningCancellationTokenSource.IsCancellationRequested) return;
                
                
                // UI Should be visible but was never drawn
                var message = new SeStringBuilder();
                message.AddText("[");
                message.AddUiForeground($"{plugin.Name}", 48);
                message.AddText("] The config window is currently hidden");
                
                if (PluginService.ClientState.IsGPosing) {
                    message.AddText(" in GPose. ");
                    message.AddUiForeground(37);
                    message.Add(clickAllowInGposePayload);
                    message.AddText("Click Here");
                    message.Add(RawPayload.LinkTerminator);
                    message.AddUiForegroundOff();
                    message.AddText(" to allow the config window to be shown in GPose.");
                } else if (PluginService.PluginInterface.UiBuilder.CutsceneActive) {
                    message.AddText(" in cutscenes. ");
                    message.AddUiForeground(37);
                    message.Add(clickAllowInCutscenePayload);
                    message.AddText("Click Here");
                    message.Add(RawPayload.LinkTerminator);
                    message.AddUiForegroundOff();
                    message.AddText(" to allow the config window to be shown in cutscenes.");
                } else {
                    // Unknown reason, don't mention it at all
                    return;
                }
                
                PluginService.ChatGui.PrintError(message.Build());

            }, TimeSpan.FromMilliseconds(250), cancellationToken: notVisibleWarningCancellationTokenSource.Token);
        }
    }
}
