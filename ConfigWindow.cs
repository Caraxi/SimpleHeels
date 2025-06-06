﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Game.ClientState.Objects.Types;
using Dalamud.Game.Text.SeStringHandling;
using Dalamud.Game.Text.SeStringHandling.Payloads;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.ImGuiFileDialog;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Lumina.Excel.Sheets;
using Newtonsoft.Json;
using SimpleHeels.Files;
using World = Lumina.Excel.Sheets.World;
using WorldDCGroupType = Lumina.Excel.Sheets.WorldDCGroupType;

namespace SimpleHeels;

public class ConfigWindow : Window {
    private static FileDialogManager? _fileDialogManager;
    private readonly PluginConfig config;
    private readonly Stopwatch hiddenStopwatch = Stopwatch.StartNew();
    
    private readonly Plugin plugin;

    private readonly Lazy<Dictionary<(ushort, ModelSlot), ShoeModel>> shoeModelList = new(() => {
        var dict = new Dictionary<(ushort, ModelSlot), ShoeModel> { [(0, ModelSlot.Feet)] = new() { Id = 0, Name = "Smallclothes (Barefoot)" } };

        foreach (var item in PluginService.Data.GetExcelSheet<Item>().Where(i => i.EquipSlotCategory.Value.Feet != 0)) {
            if (item.ItemUICategory.RowId is not (35 or 36 or 38)) continue;

            var modelBytes = BitConverter.GetBytes(item.ModelMain);
            var modelId = BitConverter.ToUInt16(modelBytes, 0);

            var slot = item.ItemUICategory.RowId switch {
                35 => ModelSlot.Top,
                36 => ModelSlot.Legs,
                _ => ModelSlot.Feet
            };

            if (!dict.ContainsKey((modelId, slot))) dict.Add((modelId, slot), new ShoeModel { Id = modelId, Slot = slot });

            dict[(modelId, slot)].Items.Add(item);
            dict[(modelId, slot)].Name = null;
        }

        return dict;
    });

    private int beginDrag = -1;
    private float checkboxSize = 36;
    private DalamudLinkPayload clickAllowInCutscenePayload;

    private DalamudLinkPayload clickAllowInGposePayload;
    private int endDrag = -1;
    private Vector2 endDragPosition = new();

    private Vector2 firstCheckboxScreenPosition = new(0);

    private string footwearSearch = string.Empty;

    private string groupNameMatchingNewInput = string.Empty;
    private string groupNameMatchingWorldSearch = string.Empty;

    private Vector2 iconButtonSize = new(16);

    private float kofiButtonOffset = 0f;
    private MdlFile? loadedFile;
    private string loadedFilePath = string.Empty;
    private Exception? mdlEditorException;
    private float mdlEditorOffset = 0f;

    private string newName = string.Empty;
    private uint newWorld = 0;

    private CancellationTokenSource? notVisibleWarningCancellationTokenSource;
    private string? PenumbraModFolder;

    private string searchInput = string.Empty;

    private CharacterConfig? selectedCharacter;

    private GroupConfig? selectedGroup;
    private string selectedName = string.Empty;
    private uint selectedWorld;
    private bool useTextoolSafeAttribute = true;

    public ConfigWindow(string name, Plugin plugin, PluginConfig config) : base(name, ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse) {
        this.config = config;
        this.plugin = plugin;

        SizeConstraints = new WindowSizeConstraints { MinimumSize = new Vector2(800, 400), MaximumSize = new Vector2(float.MaxValue) };

        Size = new Vector2(1000, 500);
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

    public unsafe void DrawCharacterList() {
        
        var highlightName = PluginService.ClientState.LocalPlayer?.Name.TextValue ?? string.Empty;
        var highlightWorld = PluginService.ClientState.LocalPlayer?.HomeWorld.RowId ?? 0;

        if (config.IdentifyAs.TryGetValue(PluginService.ClientState.LocalContentId, out var identity)) {
            highlightName = identity.Item1;
            highlightWorld = identity.Item2;
            var worldName = PluginService.Data.GetExcelSheet<World>().GetRowOrDefault(identity.Item2)?.Name.ExtractText() ?? $"UnknownWorld#{identity.Item2}";
            ImGui.Text("Current Identity:");
            ImGui.SameLine();
            if (ImGui.SmallButton("Reset")) {
                config.IdentifyAs.Remove(PluginService.ClientState.LocalContentId);
            } else {
                ImGui.Text($"\t{identity.Item1}\n\t\t@ {worldName}");
                        
            }
            ImGui.Separator();
            
        }
        
        foreach (var (worldId, characters) in config.WorldCharacterDictionary.ToArray()) {
            var world = PluginService.Data.GetExcelSheet<World>()?.GetRowOrDefault(worldId);
            if (world == null) continue;

            ImGui.TextDisabled($"{world.Value.Name.ExtractText()}");
            ImGuiExt.Separator();
            
            foreach (var (name, characterConfig) in characters.ToArray()) {
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGrey, characterConfig.Enabled == false)) {
                    if (ImGui.Selectable($"{name}##{world.Value.Name.ExtractText()}", selectedCharacter == characterConfig)) {
                        selectedCharacter = characterConfig;
                        selectedName = name;
                        selectedWorld = world.Value.RowId;
                        newName = name;
                        newWorld = world.Value.RowId;
                        selectedGroup = null;
                    }
                }

                if (highlightName.Equals(name) && highlightWorld == worldId) {
                    using (PluginService.PluginInterface.UiBuilder.IconFontHandle.Push())
                    using (ImRaii.PushColor(ImGuiCol.Text, 0xFFAA55FF)) {
                        ImGui.SameLine();
                        ImGui.Text(FontAwesomeIcon.ArrowLeft.ToIconString());
                    }

                    if (ImGui.IsItemHovered()) {
                        ImGui.SetTooltip("Active Character");
                    }
                }
                
                if (ImGui.BeginPopupContextItem()) {
                    if (ImGui.Selectable($"Remove '{name} @ {world.Value.Name.ExtractText()}' from Config")) {
                        characters.Remove(name);
                        if (selectedCharacter == characterConfig) selectedCharacter = null;
                        if (characters.Count == 0) {
                            config.WorldCharacterDictionary.Remove(worldId);
                        }
                    }
                    
                    if (PluginService.ClientState.LocalContentId != 0 && ImGui.Selectable($"Identify as '{name} @ {world.Value.Name.ExtractText()}'")) {
                        config.IdentifyAs[PluginService.ClientState.LocalContentId] = (name, world.Value.RowId);
                    }

                    ImGui.EndPopup();
                }
            }

            ImGuiHelpers.ScaledDummy(10);
        }

        if (Plugin.IsDebug && Plugin.IpcAssignedData.Count > 0) {
            ImGui.TextDisabled("[DEBUG] IPC Assignments");
            ImGuiExt.Separator();

            foreach (var (objectId, ipcCharacterConfig) in Plugin.IpcAssignedData) {
                var ipcAssignedObject = Utils.GetGameObjectById(objectId);
                if (ipcAssignedObject == null) continue;
                if (!ipcAssignedObject->IsCharacter()) continue;
                var ipcAssignedCharacter = (Character*)ipcAssignedObject;
                if (ipcAssignedCharacter->HomeWorld == ushort.MaxValue) continue;

                var name = SeString.Parse(ipcAssignedObject->Name).TextValue;
                var worldId = ipcAssignedCharacter->HomeWorld;

                var world = PluginService.Data.GetExcelSheet<World>()?.GetRow(worldId);
                if (world == null) continue;
                if (ImGui.Selectable($"{name}##{world.Value.Name.ExtractText()}##ipc", selectedName == name && selectedWorld == worldId)) {
                    selectedCharacter = ipcCharacterConfig;
                    selectedName = name;
                    selectedWorld = world.Value.RowId;
                    newName = string.Empty;
                    newWorld = 0;
                    selectedGroup = null;
                }

                ImGui.SameLine();
                ImGui.TextDisabled(world.Value.Name.ToDalamudString().TextValue);
            }

            ImGuiHelpers.ScaledDummy(10);
        }

        if (config.Groups.Count > 0) {
            ImGui.TextDisabled($"Group Assignments");
            ImGuiExt.Separator();
            var arr = config.Groups.ToArray();

            for (var i = 0; i < arr.Length; i++) {
                var filterConfig = arr[i];
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.ParsedGrey, filterConfig.Enabled == false)) {
                    if (ImGui.Selectable($"{filterConfig.Label}##filterConfig_{i}", selectedGroup == filterConfig)) {
                        selectedCharacter = null;
                        selectedName = string.Empty;
                        selectedWorld = 0;
                        newName = string.Empty;
                        newWorld = 0;
                        selectedGroup = filterConfig;
                        selectedGroup.Characters.RemoveAll(c => string.IsNullOrWhiteSpace(c.Name));
                    }
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
                        if (selectedGroup == filterConfig) selectedGroup = null;
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

    private void ShowDebugInfo() {
        if (Plugin.IsDebug && ImGui.TreeNode("DEBUG INFO")) {
            try {
                var activePlayer = PluginService.Objects.FirstOrDefault(t => t is IPlayerCharacter playerCharacter && playerCharacter.Name.TextValue == selectedName && playerCharacter.HomeWorld.RowId == selectedWorld);

                if (activePlayer is not IPlayerCharacter pc) {
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
                    // ImGui.Text($"Expected Offset: {plugin.GetOffset(obj)}");

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

    public override void Draw() {
        checkboxSize = ImGui.GetTextLineHeight() + ImGui.GetStyle().FramePadding.Y * 2;

        hiddenStopwatch.Restart();
        if (notVisibleWarningCancellationTokenSource != null) {
            notVisibleWarningCancellationTokenSource.Cancel();
            notVisibleWarningCancellationTokenSource = null;
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

                        var name = PluginService.ClientState.LocalPlayer.Name.TextValue;
                        var world = PluginService.ClientState.LocalPlayer.HomeWorld.RowId;

                        if (config.IdentifyAs.TryGetValue(PluginService.ClientState.LocalContentId, out var identity)) {
                            name = identity.Item1;
                            world = identity.Item2;
                        }
                        
                        config.TryAddCharacter(name, world);
                    }
                }

                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add current character");

                ImGui.SameLine();
                if (ImGuiComponents.IconButton(FontAwesomeIcon.DotCircle)) {
                    if (PluginService.Targets.Target is IPlayerCharacter pc) {
                        config.TryAddCharacter(pc.Name.TextValue, pc.HomeWorld.RowId);
                    }
                }

                if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add targeted character");
                ImGui.SameLine();
            }

            if (ImGuiComponents.IconButton(FontAwesomeIcon.PeopleGroup)) {
                if (new GroupConfig().Initialize() is GroupConfig newGroup) {
                    selectedCharacter = null;
                    selectedName = string.Empty;
                    selectedWorld = 0;
                    newName = string.Empty;
                    newWorld = 0;
                    selectedGroup = newGroup;
                    config.Groups.Add(newGroup);
                }
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
            ImGui.SameLine();
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Save)) { 
                Plugin.SaveConfig();
            }

            if (ImGui.IsItemHovered()) {
                using (ImRaii.Tooltip()) {
                    ImGui.Text("Manually Save Plugin Config");
                    ImGui.Separator();
                    ImGui.Text("This should not be necessary, but it might\nhelp in certain circumstances.");
                }
            }

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
                DrawCharacterView(selectedCharacter);
            } else if (selectedGroup != null) {
                if (ImGui.Checkbox($"Enable Offsets for Group", ref selectedGroup.Enabled)) {
                    Plugin.RequestUpdateAll();
                }

                if (selectedGroup is { Enabled: false }) {
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudRed, "This config is disabled.");
                }

                ImGui.InputText("Group Label", ref selectedGroup.Label, 50);
                ImGuiExt.Separator();
                ImGui.Text("Apply group to characters using:");
                ImGui.Indent();

                if (ImGui.Checkbox("Masculine Model", ref selectedGroup.MatchMasculine)) {
                    if (selectedGroup is { MatchFeminine: false, MatchMasculine: false }) {
                        selectedGroup.MatchFeminine = true;
                    }
                }

                if (ImGui.Checkbox("Feminine Model", ref selectedGroup.MatchFeminine)) {
                    if (selectedGroup is { MatchFeminine: false, MatchMasculine: false }) {
                        selectedGroup.MatchMasculine = true;
                    }
                }

                ImGui.Unindent();

                ImGui.TextColored(ImGui.ColorConvertU32ToFloat4(selectedGroup.Clans.Count == 0 ? ImGui.GetColorU32(ImGuiCol.TextDisabled) : ImGui.GetColorU32(ImGuiCol.Text)), "Apply group to characters of the clans:");

                if (selectedGroup.Clans.Count == 0) {
                    ImGui.SameLine();
                    ImGuiComponents.HelpMarker($"This group will apply to all characters{(selectedGroup.MatchFeminine && selectedGroup.MatchMasculine ? "" : selectedGroup.MatchFeminine ? " using a feminine model" : " using a masculine model")} as no clan is selected.");
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
                        if (ImGui.BeginCombo("##world", c.World == ushort.MaxValue ? "Non Player" : PluginService.Data.GetExcelSheet<World>()?.GetRowOrDefault(c.World)?.Name.ExtractText() ?? $"World#{c.World}", ImGuiComboFlags.HeightLargest)) {
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
                                        if (lastDc != dc.Value.RowId) {
                                            lastDc = dc.Value.RowId;
                                            ImGui.TextDisabled($"{dc.Value.Name.ExtractText()}");
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
                                foreach (var w in PluginService.Data.GetExcelSheet<World>()!.Where(w => w.IsPlayerWorld()).OrderBy(w => w.DataCenter.Value.Name.ExtractText()).ThenBy(w => w.Name.ExtractText())) {
                                    World(w.Name.ExtractText(), w.RowId, w.DataCenter.Value);
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
                        var c = new GroupCharacter() { Name = groupNameMatchingNewInput };
                        selectedGroup.Characters.Add(c);
                        groupNameMatchingNewInput = string.Empty;
                    }
                    
                    if (PluginService.ClientState.LocalPlayer != null) {
                        ImGui.SameLine();
                        if (ImGuiComponents.IconButton(FontAwesomeIcon.User)) {
                            if (PluginService.ClientState.LocalPlayer != null) {
                                var c = new GroupCharacter { Name = PluginService.ClientState.LocalPlayer.Name.TextValue, World = PluginService.ClientState.LocalPlayer.HomeWorld.RowId };
                                if (!selectedGroup.Characters.Any(ec => ec.Name == c.Name && ec.World == c.World)) {
                                    selectedGroup.Characters.Add(c);
                                }
                            }
                        }

                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add current character");

                        ImGui.SameLine();
                        if (ImGuiComponents.IconButton(FontAwesomeIcon.DotCircle)) {
                            var target = PluginService.Targets.SoftTarget ?? PluginService.Targets.Target;
                            if (target != null) {
                                var c = new GroupCharacter { Name = target.Name.TextValue, World = target is IPlayerCharacter pc ? pc.HomeWorld.RowId : ushort.MaxValue };
                                if (!selectedGroup.Characters.Any(ec => ec.Name == c.Name && ec.World == c.World)) {
                                    selectedGroup.Characters.Add(c);
                                }
                            }
                        }

                        if (ImGui.IsItemHovered()) ImGui.SetTooltip("Add targeted character");
                        ImGui.SameLine();
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
                
                if (config.UseModelOffsets) {
                    ImGui.SameLine();
                    ImGui.Dummy(new Vector2(15, 1) * ImGuiHelpers.GlobalScale);
                    ImGui.SameLine();
                    ImGui.Checkbox("Enable 'Copy Attribute' button for model offsets.", ref config.CopyAttributeButton);
                }

                ImGui.Checkbox("Apply offsets to minions", ref config.ApplyToMinions);
                ImGui.SameLine();
                ImGuiComponents.HelpMarker("Allows group offsets to be applied to minions.\nThis only functions if the minion has been converted to a human using another plugin such as Glamourer.\nEmote offsets and syncing will not function on minions.");

                ImGui.Checkbox("Share static minion positions", ref config.ApplyStaticMinionPositions);
                ImGui.SameLine();
                ImGuiComponents.HelpMarker("Allows the sending and recieving of static minion positions when syncing your offset with Mare.\nThis option must be enabled on both sides to have an effect on position.\nOnly works on minions that do not move, such as the Plush Cushion and Wanderers Campfire");

                ImGui.Checkbox("Enable Gizmo for Minion Positions", ref config.MinionGizmo);
                ImGui.SameLine();

                ImGui.TextDisabled("Gizmo will be displayed while the temp offset window is open, and the");
                ImGui.SameLine();
                if (HotkeyHelper.DrawHotkeyConfigEditor("##MinionGizmoDisplayHotkey", config.MinionGizmoHotkey, out var newMinionGizmoHotkey)) {
                    config.MinionGizmoHotkey = newMinionGizmoHotkey;
                }
                ImGui.SameLine();
                ImGui.TextDisabled("binding is held.");
                
                ImGui.Checkbox("Use precise positioning for emotes", ref config.UsePrecisePositioning);
                ImGui.SameLine();
                ImGuiComponents.HelpMarker("Adjusts offsets of other players to better match the position they see themself in.\n\nBy default, the game does not have a good deal of precision in showing where other players are, this option helps line up emotes that aren't bound to a chair or bed.");
                
                ImGui.Checkbox("Prefer model paths when creating new entries", ref config.PreferModelPath);

                ImGui.Checkbox("Show Plus/Minus buttons for offset adjustments", ref config.ShowPlusMinusButtons);
                using (ImRaii.Disabled(!config.ShowPlusMinusButtons))
                using (ImRaii.PushIndent()) {
                    ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
                    ImGui.SliderFloat("Plus/Minus Button Delta", ref config.PlusMinusDelta, 0.0001f, 0.01f, "%.4f", ImGuiSliderFlags.AlwaysClamp);
                }

                ImGui.Checkbox("SHIFT + Right click offset inputs to reset values", ref config.RightClickResetValue);
                ImGui.Checkbox("Show character Rename and Copy UI", ref config.ShowCopyUi);

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
                ImGui.Text("Temporary Offsets");
                ImGui.SameLine();
                using (ImRaii.PushFont(UiBuilder.IconFont)) {
                    ImGui.TextColored(ImGuiColors.DalamudWhite, FontAwesomeIcon.InfoCircle.ToIconString());
                }

                if (ImGui.IsItemHovered()) {
                    
                    ImGui.BeginTooltip();
                    
                    ImGui.TextWrapped("Temporary Offsets allow adjusting your current offset without permanently changing the config. Offsets will automatically be reset when you begin or end a looped emote, or if manually reset with the 'Reset Offset' button.");
                    ImGuiHelpers.ScaledDummy(350, 1);
                    ImGui.EndTooltip();
                }
                
                using (ImRaii.PushIndent())
                using (ImRaii.PushId("TempOffsets")) {
                    ImGui.Checkbox("Show Editing Window", ref config.TempOffsetWindowOpen);
                    ImGui.SameLine();
                    ImGui.TextDisabled("Toggle with command");
                    ImGui.SameLine();
                    ImGui.TextColored(ImGuiColors.DalamudViolet, "/heels temp");
                    ImGui.Checkbox("Show Tooltips", ref config.TempOffsetWindowTooltips);
                    ImGui.Checkbox("Lock Window", ref config.TempOffsetWindowLock);
                    ImGui.Checkbox("Restrict window to FFXIV Window", ref config.TempOffsetWindowLockInViewport);
                    using (ImRaii.Disabled(config.TempOffsetWindowLock == false)) {
                        ImGui.Checkbox("Transparent", ref config.TempOffsetWindowTransparent);
                    }

                    ImGui.Checkbox("Show Plus/Minus Buttons", ref config.TempOffsetWindowPlusMinus);
                    ImGui.Checkbox("Enable Pitch/Roll", ref config.TempOffsetPitchRoll);
                    
                    ImGui.Checkbox("Show Gizmo", ref config.TempOffsetShowGizmo);
                    ImGui.SameLine();

                    ImGui.TextDisabled("Gizmo will be displayed while the temp offset window is open, and the");
                    ImGui.SameLine();
                    if (HotkeyHelper.DrawHotkeyConfigEditor("##GizmoDisplayHotkey", config.TempOffsetGizmoHotkey, out var newHotkey)) {
                        config.TempOffsetGizmoHotkey = newHotkey;
                    }
                    ImGui.SameLine();
                    ImGui.TextDisabled("binding is held.");
                    
                }
                
                
                ImGuiExt.Separator();

#if DEBUG
                ImGui.Checkbox("[DEBUG] Open config window on startup", ref config.DebugOpenOnStartup);
#endif

                if (Plugin.IsDebug) {
                    if (ImGui.TreeNode("DEBUG")) {
                        
                        ImGui.Text($"Dalamud: {Util.GetGitHash()}- ClientStructs: {FFXIVClientStructs.ThisAssembly.Git.Commit}[{FFXIVClientStructs.ThisAssembly.Git.Commits}]");
                        
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
                /*
                if (config.UseModelOffsets && ImGui.CollapsingHeader("Model Offset Editor")) {
                    ShowModelEditor();
                }
                */
            }
        }

        ImGui.EndChild();
    }

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
                ImGui.Checkbox("Use TexTools safe attribute", ref useTextoolSafeAttribute);

                ImGuiExt.FloatEditor("Heels Offset", ref mdlEditorOffset, 0.001f, -1, 1, "%.5f", ImGuiSliderFlags.AlwaysClamp);
                var offset = attributes.FirstOrDefault(a => a.Length > 13 && a.StartsWith("heels_offset") && a[12] is '_' or '=');
                if (offset == null) {
                    ImGui.Text("Model has no offset assigned.");
                } else if (offset[12] == '_') {
                    var str = offset[13..].Replace("n_", "-").Replace('a', '0').Replace('b', '1').Replace('c', '2').Replace('d', '3').Replace('e', '4').Replace('f', '5').Replace('g', '6').Replace('h', '7').Replace('i', '8').Replace('j', '9').Replace('_', '.');
                    ImGui.Text($"Current Offset: {str}");
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
                            attributes.RemoveAll(a => a.StartsWith("heels_offset"));
                            if (useTextoolSafeAttribute) {
                                var valueStr = mdlEditorOffset.ToString(CultureInfo.InvariantCulture).Replace("-", "n_").Replace(".", "_").Replace("0", "a").Replace("1", "b").Replace("2", "c").Replace("3", "d").Replace("4", "e").Replace("5", "f").Replace("6", "g").Replace("7", "h").Replace("8", "i").Replace("9", "j");

                                attributes.Add($"heels_offset_{valueStr}");
                            } else {
                                attributes.Add($"heels_offset={mdlEditorOffset.ToString(CultureInfo.InvariantCulture)}");
                            }

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
                            var offset = attributes.FirstOrDefault(a => a.StartsWith("heels_offset") && a.Length > 13);

                            if (offset != null) {
                                if (offset[12] == '_') {
                                    // TexTools safe attribute
                                    useTextoolSafeAttribute = true;

                                    var str = offset[13..].Replace("n_", "-").Replace('a', '0').Replace('b', '1').Replace('c', '2').Replace('d', '3').Replace('e', '4').Replace('f', '5').Replace('g', '6').Replace('h', '7').Replace('i', '8').Replace('j', '9').Replace('_', '.');

                                    if (!float.TryParse(str, CultureInfo.InvariantCulture, out mdlEditorOffset)) {
                                        mdlEditorOffset = 0;
                                    }
                                } else if (offset[12] == '=') {
                                    useTextoolSafeAttribute = false;
                                    if (!float.TryParse(offset[13..], CultureInfo.InvariantCulture, out mdlEditorOffset)) {
                                        mdlEditorOffset = 0;
                                    }
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

    private unsafe void DrawCharacterView(CharacterConfig? characterConfig) {
        if (characterConfig == null) return;

        var wearingMatchCount = 0;
        var usingDefault = true;

        GameObject* activeCharacter = null;
        Character* activeCharacterAsCharacter = null;
        IOffsetProvider? activeHeelConfig = null;

        if (characterConfig is GroupConfig gc) {
            var target = new[] { PluginService.Targets.SoftTarget, PluginService.Targets.Target, PluginService.ClientState.LocalPlayer }.FirstOrDefault(t => t is ICharacter character && gc.Matches(((GameObject*)character.Address)->DrawObject, character.Name.TextValue, (character is IPlayerCharacter pc) ? pc.HomeWorld.RowId : ushort.MaxValue));
            if (target is ICharacter) {
                activeCharacter = (GameObject*)target.Address;
                activeCharacterAsCharacter = (Character*)activeCharacter;
                activeHeelConfig = characterConfig.GetFirstMatch(activeCharacterAsCharacter);
                if (target is IPlayerCharacter pc) {
                    ImGui.TextDisabled($"Preview displays based on {target.Name.TextValue} ({pc.HomeWorld.Value.Name.ExtractText()})");
                } else {
                    ImGui.TextDisabled($"Preview displays based on {target.Name.TextValue} (NPC)");
                }

                Plugin.RequestUpdateAll();
            }
        } else {
            var player =  PluginService.Objects.FirstOrDefault(t => t is IPlayerCharacter playerCharacter && ((playerCharacter.ObjectIndex == 0 && config.IdentifyAs.TryGetValue(PluginService.ClientState.LocalContentId, out var identity)) ? (identity.Item1 == selectedName && identity.Item2 == selectedWorld) : (playerCharacter.Name.TextValue == selectedName && playerCharacter.HomeWorld.RowId == selectedWorld)));
            if (player is IPlayerCharacter) {
                activeCharacter = (GameObject*)player.Address;
                activeCharacterAsCharacter = (Character*)activeCharacter;
                activeHeelConfig = characterConfig.GetFirstMatch(activeCharacterAsCharacter);
                Plugin.NeedsUpdate[activeCharacter->ObjectIndex] = true;
            }
        }

        if (activeCharacter != null && Plugin.IpcAssignedData.TryGetValue(activeCharacter->EntityId, out var ipcCharacterConfig)) {
            characterConfig = ipcCharacterConfig;
        }

        if (characterConfig is IpcCharacterConfig) {
            ImGui.TextColored(ImGuiColors.DalamudYellow, "This character's config has been assigned by another plugin.");
            if (Plugin.IsDebug && activeCharacter != null) {
                ImGui.SameLine();
                if (ImGui.SmallButton("Clear IPC")) {
                    Plugin.IpcAssignedData.Remove(activeCharacter->EntityId);
                    selectedCharacter = null;
                    selectedWorld = 0;
                    selectedName = string.Empty;
                    return;
                }
            }
        }

        if (characterConfig is not IpcCharacterConfig && config.ShowCopyUi && newWorld != 0) {
            ImGui.InputText("Character Name", ref newName, 64);
            var worldName = PluginService.Data.GetExcelSheet<World>()!.GetRow(newWorld)!.Name.ToDalamudString().TextValue;
            if (ImGui.BeginCombo("World", worldName)) {
                var lastDc = string.Empty;
                foreach (var world in PluginService.Data.GetExcelSheet<World>()!.Where(w => w.IsPlayerWorld()).OrderBy(w => w.DataCenter.Value.Name.ExtractText() ?? string.Empty).ThenBy(w => w.Name.ToDalamudString().TextValue, StringComparer.OrdinalIgnoreCase)) {
                    if (lastDc != world.DataCenter.Value.Name.ExtractText()) {
                        ImGui.TextDisabled(world.DataCenter.Value.Name.ExtractText() ?? string.Empty);
                        lastDc = world.DataCenter.Value.Name.ExtractText() ?? string.Empty;
                    }
                    
                    if (ImGui.Selectable($"  {world.Name.ToDalamudString().TextValue}", world.RowId == newWorld)) {
                        newWorld = world.RowId;
                    }
                }

                ImGui.EndCombo();
            }

            using (ImRaii.Disabled(!ImGui.GetIO().KeyShift)) {
                if (ImGui.Button("Create Group") && selectedCharacter != null) {
                    if (new GroupConfig { Label = $"Group from {selectedName}@{worldName}", HeelsConfig = selectedCharacter.HeelsConfig, EmoteConfigs = selectedCharacter.EmoteConfigs, Enabled = false }.Initialize() is GroupConfig group) {
                        var copy = JsonConvert.DeserializeObject<GroupConfig>(JsonConvert.SerializeObject(group));
                        if (copy != null) {
                            config.Groups.Add(copy);
                            selectedCharacter = null;
                            selectedName = string.Empty;
                            selectedWorld = 0;
                            selectedGroup = copy;
                        }
                    }
                }
            }

            if (!ImGui.GetIO().KeyShift && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                ImGui.SetTooltip("Hold SHIFT\n\nCreates a new Group Assignment from this character's config.");
            }

            ImGui.SameLine();
            var isModified = newName != selectedName || newWorld != selectedWorld;
            {
                var newAlreadyExists = config.WorldCharacterDictionary.ContainsKey(newWorld) && config.WorldCharacterDictionary[newWorld].ContainsKey(newName);

                using (ImRaii.Disabled(isModified == false || newAlreadyExists)) {
                    if (ImGui.Button("Rename Character Config")) {
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
                }

                var moveHovered = ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled);

                ImGui.SameLine();
                using (ImRaii.Disabled(isModified == false || newAlreadyExists)) {
                    if (ImGui.Button("Copy Character Config")) {
                        if (config.TryAddCharacter(newName, newWorld)) {
                            var j = JsonConvert.SerializeObject(selectedCharacter);
                            config.WorldCharacterDictionary[newWorld][newName] = (JsonConvert.DeserializeObject<CharacterConfig>(j) ?? new CharacterConfig()).Initialize();
                        }
                    }
                }

                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) || moveHovered) {
                    using (ImRaii.Tooltip()) {
                        ImGui.Text(moveHovered ? "Change which character this character configuration is assigned to." : "Copy this character config to another character.");
                        if (!isModified) {
                            ImGui.TextColored(ImGuiColors.DalamudYellow, "Change the name or world in the boxes above.");
                        } else if (newAlreadyExists) {
                            ImGui.TextColored(ImGuiColors.DalamudOrange, "The new character already has a configuration.");
                        }
                    }
                }

                if (isModified && newAlreadyExists) {
                    ImGui.SameLine();
                    ImGui.TextDisabled("Character already exists in config.");
                }
            }

            ImGuiExt.Separator();
        }

        if (characterConfig is not IpcCharacterConfig && characterConfig.HeelsConfig.Count > 0 && !characterConfig.HeelsConfig.Any(hc => hc.Enabled)) {
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

        if (characterConfig is not IpcCharacterConfig && activeCharacter != null) {
            var customizePlusProfile = IPC.CustomizePlus.GetProfileOnCharacter(activeCharacter->ObjectIndex);
            if (customizePlusProfile != null) {
                if (customizePlusProfile.Bones.TryGetValue("n_root", out _)) {
                    ImGui.PushStyleColor(ImGuiCol.Button, ImGuiColors.DalamudRed * new Vector4(1, 1, 1, 0.3f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonActive, ImGuiColors.DalamudRed * new Vector4(1, 1, 1, 0.3f));
                    ImGui.PushStyleColor(ImGuiCol.ButtonHovered, ImGuiColors.DalamudRed * new Vector4(1, 1, 1, 0.3f));
                    ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, new Vector2(0, 0));
                    ImGui.Button($"Customize Plus is modifying the root node of '{activeCharacter->NameString}'", new Vector2(ImGui.GetContentRegionAvail().X, 32 * ImGuiHelpers.GlobalScale));
                    ImGui.Button($"Simple Heels WILL NOT work for this character.", new Vector2(ImGui.GetContentRegionAvail().X, 32 * ImGuiHelpers.GlobalScale));
                    ImGui.PopStyleVar();
                    ImGui.PopStyleColor(3);
                }
            }
        }

        using (ImRaii.Disabled(characterConfig is IpcCharacterConfig)) {
            if (characterConfig is not (IpcCharacterConfig or GroupConfig) && ImGui.Checkbox($"Enable offsets for {selectedName}", ref characterConfig.Enabled)) {
                Plugin.RequestUpdateAll();
            }

            if (selectedCharacter is { Enabled: false }) {
                ImGui.SameLine();
                ImGui.TextColored(ImGuiColors.DalamudRed, "This config is disabled.");
            }

            ImGuiExt.Separator();

            if (characterConfig is not IpcCharacterConfig && ImGui.CollapsingHeader("Equipment Offsets", ImGuiTreeNodeFlags.DefaultOpen)) {
                var activeFootwear = GetModelIdForPlayer(activeCharacter, ModelSlot.Feet);
                var activeFootwearPath = GetModelPathForPlayer(activeCharacter, ModelSlot.Feet);

                var activeTop = GetModelIdForPlayer(activeCharacter, ModelSlot.Top);
                var activeTopPath = GetModelPathForPlayer(activeCharacter, ModelSlot.Top);

                var activeLegs = GetModelIdForPlayer(activeCharacter, ModelSlot.Legs);
                var activeLegsPath = GetModelPathForPlayer(activeCharacter, ModelSlot.Legs);

                var windowMax = ImGui.GetWindowPos() + ImGui.GetWindowSize();
                if (ImGui.BeginTable("OffsetsTable", 5, ImGuiTableFlags.Resizable)) {
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

                        if (beginDrag != i && heelConfig.Locked) ImGui.BeginDisabled(heelConfig.Locked);

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.InputText("##label", ref heelConfig.Label, 100);

                        ImGui.TableNextColumn();

                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        if (ImGuiExt.FloatEditor("##offset", ref heelConfig.Offset, 0.001f, float.MinValue, float.MaxValue, "%.5f", resetValue: 0f)) {
                            if (heelConfig.Enabled) Plugin.RequestUpdateAll();
                        }

                        ImGui.TableNextColumn();

                        if (config.CopyAttributeButton) {
                            if (ImGuiComponents.IconButton(FontAwesomeIcon.Copy)) {
                                var valueStr = heelConfig.Offset.ToString(CultureInfo.InvariantCulture).Replace("-", "n_").Replace(".", "_").Replace("0", "a").Replace("1", "b").Replace("2", "c").Replace("3", "d").Replace("4", "e").Replace("5", "f").Replace("6", "g").Replace("7", "h").Replace("8", "i").Replace("9", "j");
                                ImGui.SetClipboardText($"heels_offset_{valueStr}");
                            }

                            if (ImGui.IsItemHovered()) {
                                var valueStr = heelConfig.Offset.ToString(CultureInfo.InvariantCulture).Replace("-", "n_").Replace(".", "_").Replace("0", "a").Replace("1", "b").Replace("2", "c").Replace("3", "d").Replace("4", "e").Replace("5", "f").Replace("6", "g").Replace("7", "h").Replace("8", "i").Replace("9", "j");
                                ImGui.SetTooltip($"Copy Attribute Offset:\nheels_offset_{valueStr}");
                            }
                            
                            ImGui.SameLine();
                        }

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
                                            _ => activeFootwearPath
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

                        if ((heelConfig.Slot == ModelSlot.Feet && ((heelConfig.PathMode == false && activeFootwear == heelConfig.ModelId) || (heelConfig.PathMode && activeFootwearPath != null && activeFootwearPath.Equals(heelConfig.Path, StringComparison.OrdinalIgnoreCase)))) || (heelConfig.Slot == ModelSlot.Legs && ((heelConfig.PathMode == false && activeLegs == heelConfig.ModelId) || (heelConfig.PathMode && activeLegsPath != null && activeLegsPath.Equals(heelConfig.Path, StringComparison.OrdinalIgnoreCase)))) || (heelConfig.Slot == ModelSlot.Top && ((heelConfig.PathMode == false && activeTop == heelConfig.ModelId) || (heelConfig.PathMode && activeTopPath != null && activeTopPath.Equals(heelConfig.Path, StringComparison.OrdinalIgnoreCase))))) {
                            ShowActiveOffsetMarker(activeCharacterAsCharacter != null, heelConfig.Enabled, activeHeelConfig?.Is(heelConfig) ?? false, "Currently Wearing");
                            if (heelConfig.Enabled) {
                                wearingMatchCount++;
                                usingDefault = false;
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
                                characterConfig.HeelsConfig.Add(new HeelConfig() { ModelId = id, Slot = slot, Enabled = !characterConfig.HeelsConfig.Any(h => h is { PathMode: false } && h.ModelId == id) });
                            }

                            return true;
                        }

                        return false;
                    }

                    void ShowAddPathButton(string? path, ModelSlot slot) {
                        var pathDisplay = path ?? string.Empty;

                        pathDisplay = pathDisplay.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
                        if (!string.IsNullOrWhiteSpace(PenumbraModFolder) && !string.IsNullOrWhiteSpace(pathDisplay) && pathDisplay.StartsWith(PenumbraModFolder, StringComparison.InvariantCultureIgnoreCase)) {
                            pathDisplay = "[Penumbra] " + (slot != ModelSlot.Feet ? $"[{slot}] " : "") + pathDisplay.Remove(0, PenumbraModFolder.Length);
                        } else {
                            pathDisplay = (slot != ModelSlot.Feet ? $"[{slot}] " : "") + pathDisplay;
                        }

                        if (ImGui.Button($"Add Path: {pathDisplay}")) {
                            characterConfig.HeelsConfig.Add(new HeelConfig() { PathMode = true, Path = path, Slot = slot, Enabled = !characterConfig.HeelsConfig.Any(h => h is { PathMode: true } && string.Equals(h.Path, path, StringComparison.OrdinalIgnoreCase)) });
                        }

                        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                            ImGui.SetTooltip($"{path}");
                        }
                    }

                    if (characterConfig is not IpcCharacterConfig) {
                        if (ImGui.GetIO().KeyShift != config.PreferModelPath && (ImGui.GetIO().KeyCtrl ? activeLegsPath : ImGui.GetIO().KeyAlt ? activeTopPath : activeFootwearPath) != null) {
                            ShowAddPathButton(ImGui.GetIO().KeyCtrl ? activeLegsPath : ImGui.GetIO().KeyAlt ? activeTopPath : activeFootwearPath, ImGui.GetIO().KeyCtrl ? ModelSlot.Legs : ImGui.GetIO().KeyAlt ? ModelSlot.Top : ModelSlot.Feet);
                        } else {
                            if (!(ShowAddButton(activeTop, ModelSlot.Top) || ShowAddButton(activeLegs, ModelSlot.Legs) || ShowAddButton(activeFootwear, ModelSlot.Feet))) {
                                if (ImGui.Button($"Add New Entry")) {
                                    characterConfig.HeelsConfig.Add(new HeelConfig() { ModelId = 0, Slot = ModelSlot.Feet, Enabled = !characterConfig.HeelsConfig.Any(h => h is { PathMode: false, ModelId: 0 }) });
                                }
                            }
                        }
                    }
                }
            }

            ImGuiExt.Separator();
            var tableDl = ImGui.GetWindowDrawList();
            var w = ImGui.GetContentRegionAvail().X;

            var emoteOffsetsOpen = true;

            if (characterConfig is IpcCharacterConfig) {
                ImGui.CollapsingHeader("Emote Offsets##ipcCharacterConfig", ImGuiTreeNodeFlags.DefaultOpen | ImGuiTreeNodeFlags.Leaf);
            } else {
                emoteOffsetsOpen = ImGui.CollapsingHeader("Emote Offsets", ImGuiTreeNodeFlags.DefaultOpen);
            }

            if (emoteOffsetsOpen && characterConfig.EmoteConfigs != null) {
                var space = ImGui.GetContentRegionAvail().X;
                if (ImGui.BeginTable("emoteOffsets", (characterConfig is IpcCharacterConfig ? 7 : 9) - (space < 1000 ? 3 : 0), ImGuiTableFlags.NoClip | (space < 1000 ? ImGuiTableFlags.None : ImGuiTableFlags.Resizable))) {
                    
                    if (characterConfig is not IpcCharacterConfig) {
                        ImGui.TableSetupColumn("Enable", ImGuiTableColumnFlags.WidthFixed, checkboxSize * 4 + 3 * ImGuiHelpers.GlobalScale);
                        ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthStretch, 0.15f);
                    }
                    ImGui.TableSetupColumn("Emote", ImGuiTableColumnFlags.WidthStretch, 0.3f);
                    ImGui.TableSetupColumn("Rel.", ImGuiTableColumnFlags.WidthFixed, checkboxSize);
                    if (space >= 1000) {
                        ImGui.TableSetupColumn("Offset Height", ImGuiTableColumnFlags.WidthStretch, 0.1625f);
                        ImGui.TableSetupColumn("Offset Forward", ImGuiTableColumnFlags.WidthStretch, 0.1625f);
                        ImGui.TableSetupColumn("Offset Side", ImGuiTableColumnFlags.WidthStretch, 0.1625f);
                        ImGui.TableSetupColumn("Rotation", ImGuiTableColumnFlags.WidthStretch, 0.1625f);
                    } else {
                        ImGui.TableSetupColumn("Offset", ImGuiTableColumnFlags.WidthStretch, 0.65f);
                    }
                    
                    ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, checkboxSize);

                    if (characterConfig is IpcCharacterConfig) {
                        TableHeaderRow(TableHeaderAlign.Left, TableHeaderAlign.Center, TableHeaderAlign.Center, TableHeaderAlign.Center, TableHeaderAlign.Center, TableHeaderAlign.Center, TableHeaderAlign.Left);
                    } else {
                        TableHeaderRow(TableHeaderAlign.Right, TableHeaderAlign.Center, TableHeaderAlign.Center, TableHeaderAlign.Left, TableHeaderAlign.Center, TableHeaderAlign.Center, TableHeaderAlign.Center, TableHeaderAlign.Center, TableHeaderAlign.Left);
                    }

                    var i = 0;
                    EmoteConfig? deleteEmoteConfig = null;

                    for (var emoteIndex = 0; emoteIndex < characterConfig.EmoteConfigs.Count; emoteIndex++) {
                        var e = characterConfig.EmoteConfigs[emoteIndex];
                        using var _ = ImRaii.PushId($"emoteConfig_{i++}");
                        ImGui.TableNextRow();

                        if (characterConfig is not IpcCharacterConfig) {
                            ImGui.TableNextColumn();
                            if (i != 0) {
                                var s = ImGui.GetStyle().ItemSpacing.Y / 2;
                                tableDl.AddLine(ImGui.GetCursorScreenPos() - new Vector2(0, s), ImGui.GetCursorScreenPos() + new Vector2(w, -s), ImGui.GetColorU32(ImGuiCol.Separator) & 0x20FFFFFF);
                            }

                            using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(1))) {
                                using (ImRaii.Disabled(e.Locked || !ImGui.GetIO().KeyShift))
                                using (ImRaii.PushFont(UiBuilder.IconFont)) {
                                    if (ImGui.Button(FontAwesomeIcon.Eraser.ToIconString(), new Vector2(checkboxSize))) {
                                        e.Offset = new Vector3(0, 0, 0);
                                        e.Rotation = 0;
                                    }
                                }
                                
                                if (e.Locked == false && !ImGui.GetIO().KeyShift && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                                    ImGui.SetTooltip("Hold SHIFT to reset all values to zero");
                                }
                                
                                ImGui.SameLine();
                                using (ImRaii.Disabled(e.Locked || !ImGui.GetIO().KeyShift))
                                using (ImRaii.PushFont(UiBuilder.IconFont)) {
                                    if (ImGui.Button(FontAwesomeIcon.Trash.ToIconString(), new Vector2(checkboxSize))) {
                                        deleteEmoteConfig = e;
                                    }
                                }

                                if (e.Locked == false && !ImGui.GetIO().KeyShift && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
                                    ImGui.SetTooltip("Hold SHIFT to delete");
                                }

                                ImGui.SameLine();
                                using (ImRaii.PushColor(ImGuiCol.Button, ImGui.GetColorU32(ImGuiCol.ButtonActive), e.Editing))
                                using (ImRaii.PushFont(UiBuilder.IconFont)) {
                                    if (ImGui.Button(FontAwesomeIcon.Edit.ToIconString(), new Vector2(checkboxSize))) {
                                        e.Editing = !e.Editing;
                                    }
                                }

                                ImGui.SameLine();
                                ImGui.Checkbox("##enable", ref e.Enabled);
                            }

                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                            ImGui.InputText("##label", ref e.Label, 100);
                        }

                        ImGui.TableNextColumn();
                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);

                        var previewEmoteName = characterConfig is not IpcCharacterConfig && e is { Editing: false, LinkedEmotes.Count: > 0 } ? $"{e.Emote.Name} (+ {e.LinkedEmotes.Count} other{(e.LinkedEmotes.Count > 1 ? "s" : "")})" : e.Emote.Name;

                        ImGuiExt.IconTextFrame(e.Emote.Icon, previewEmoteName);
                        ImGui.TableNextColumn();
                        ImGui.Checkbox("##relativeOffset", ref e.RelativeOffset);
                        
                        if (space >= 1000) {
                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                            ImGuiExt.FloatEditor("##height", ref e.Offset.Y, 0.0001f, allowPlusMinus: characterConfig is not IpcCharacterConfig, resetValue: 0f);
                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                            ImGuiExt.FloatEditor("##forward", ref e.Offset.Z, 0.0001f, allowPlusMinus: characterConfig is not IpcCharacterConfig, resetValue: 0f);
                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                            ImGuiExt.FloatEditor("##side", ref e.Offset.X, 0.0001f, allowPlusMinus: characterConfig is not IpcCharacterConfig, resetValue: 0f);
                            ImGui.TableNextColumn();
                            ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                            var rot = e.Rotation * 180f / MathF.PI;

                            if (ImGuiExt.FloatEditor("##rotation", ref rot, format: "%.0f", allowPlusMinus: characterConfig is not IpcCharacterConfig, customPlusMinus: 1, resetValue: 0f)) {
                                if (rot < 0) rot += 360;
                                if (rot >= 360) rot -= 360;
                                e.Rotation = rot * MathF.PI / 180f;
                            }
                        } else {
                            ImGui.TableNextColumn();

                            using (ImRaii.ItemWidth(ImGui.GetContentRegionAvail().X - ImGui.CalcTextSize("  Rotation  ").X)) {
                                ImGuiExt.FloatEditor("Height", ref e.Offset.Y, 0.0001f, allowPlusMinus: characterConfig is not IpcCharacterConfig, resetValue: 0f);
                                ImGuiExt.FloatEditor("Forward", ref e.Offset.Z, 0.0001f, allowPlusMinus: characterConfig is not IpcCharacterConfig, resetValue: 0f);
                                ImGuiExt.FloatEditor("Side", ref e.Offset.X, 0.0001f, allowPlusMinus: characterConfig is not IpcCharacterConfig, resetValue: 0f);
                                var rot = e.Rotation * 180f / MathF.PI;
                                if (ImGuiExt.FloatEditor("Rotation", ref rot, format: "%.0f", allowPlusMinus: characterConfig is not IpcCharacterConfig, customPlusMinus: 1, resetValue: 0f)) {
                                    if (rot < 0) rot += 360;
                                    if (rot >= 360) rot -= 360;
                                    e.Rotation = rot * MathF.PI / 180f;
                                }
                            }
                        }

                        ImGui.TableNextColumn();

                        var activeEmote = EmoteIdentifier.Get(activeCharacterAsCharacter);
                        
                        ShowActiveOffsetMarker(activeCharacterAsCharacter != null && activeEmote != null && (activeEmote == e.Emote || (characterConfig is not IpcCharacterConfig && e.Editing == false && e.LinkedEmotes.Contains(activeEmote))), e.Enabled, activeHeelConfig?.Is(e) ?? false, "Emote is currently being performed");

                        if (characterConfig is IpcCharacterConfig || e.Editing) {
                            var fl = characterConfig is not IpcCharacterConfig;
                            foreach (var linked in e.LinkedEmotes.ToArray()) {
                                using var __ = ImRaii.PushId($"linkedEmote_{i++}");
                                ImGui.TableNextRow();

                                if (characterConfig is not IpcCharacterConfig) {
                                    ImGui.TableNextColumn();
                                    ImGui.TableNextColumn();
                                    using (ImRaii.PushStyle(ImGuiStyleVar.ItemSpacing, new Vector2(1))) {
                                        using (ImRaii.PushFont(UiBuilder.IconFont)) {
                                            ImGui.Text($"{(char)FontAwesomeIcon.ArrowUpLong}");
                                        }

                                        var paddingSize = (ImGui.GetContentRegionAvail().X - ImGui.GetItemRectSize().X * 2 - ImGui.GetFrameHeight() * 2 - 4) / 2f;
                                        ImGui.SameLine();
                                        ImGui.Dummy(new Vector2(paddingSize, 1));
                                        ImGui.SameLine();

                                        if (ImGuiExt.IconButton(FontAwesomeIcon.Unlink, null, ImGui.GetIO().KeyShift, "Unlink Emote", "Hold SHIFT")) {
                                            e.LinkedEmotes.Remove(linked);
                                            characterConfig.EmoteConfigs.Insert(emoteIndex + 1, new EmoteConfig() { Enabled = e.Enabled, Emote = linked, Offset = new Vector3(e.Offset.X, e.Offset.Y, e.Offset.Z), Rotation = e.Rotation });
                                        }

                                        ImGui.SameLine();

                                        if (ImGuiExt.IconButton(FontAwesomeIcon.Trash, null, ImGui.GetIO().KeyShift, "Remove Linked Emote", "Hold SHIFT")) {
                                            e.LinkedEmotes.Remove(linked);
                                        }

                                        ImGui.SameLine();
                                        ImGui.Dummy(new Vector2(paddingSize, 1));
                                        ImGui.SameLine();

                                        using (ImRaii.PushFont(UiBuilder.IconFont)) {
                                            ImGui.Text($"{(char)FontAwesomeIcon.ArrowUpLong}");
                                        }
                                    }
                                }

                                ImGui.TableNextColumn();

                                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                                ImGuiExt.IconTextFrame(linked.Icon, linked.Name);
                                ImGui.TableNextColumn();
                                using (ImRaii.PushFont(UiBuilder.IconFont)) {
                                    ImGui.Text($"{(char)FontAwesomeIcon.ArrowUpLong}");
                                }

                                if (fl) {
                                    fl = false;
                                    ImGui.GetWindowDrawList().AddText(ImGui.GetFont(), ImGui.GetFont().FontSize, ImGui.GetItemRectMin() + new Vector2(ImGui.GetContentRegionAvail().X + ImGui.GetItemRectSize().X, 0), ImGui.GetColorU32(ImGuiCol.TextDisabled), "Linked Emotes use the same offset as their base.");
                                }

                                if (space >= 1000) {
                                    ImGui.TableNextColumn();
                                    ImGui.TableNextColumn();
                                    ImGui.TableNextColumn();
                                }
                                
                                ImGui.TableNextColumn();

                                
                                
                                ShowActiveOffsetMarker(activeCharacterAsCharacter != null && activeEmote == linked, e.Enabled, activeHeelConfig?.Is(e) ?? false, "Emote is currently being performed");
                            }

                            if (characterConfig is not IpcCharacterConfig) {
                                ImGui.TableNextRow();
                                ImGui.TableNextColumn();
                                ImGui.TableNextColumn();
                                ImGui.TableNextColumn();

                                using (ImRaii.Disabled(activeCharacterAsCharacter == null || activeEmote == null || e.Emote == activeEmote || e.LinkedEmotes.Contains(activeEmote)))
                                using (ImRaii.PushFont(UiBuilder.IconFont)) {
                                    if (ImGui.Button(FontAwesomeIcon.PersonDressBurst.ToIconString(), new Vector2(checkboxSize))) {
                                        if (activeEmote != null) {
                                            e.LinkedEmotes.Add(activeEmote);
                                        }
                                    }
                                }

                                if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && activeCharacterAsCharacter != null) {
                                    if (activeEmote == null) {
                                        ImGui.SetTooltip($"Link Active Emote");
                                    } else {
                                        ImGui.SetTooltip($"Link Active Emote:\n{activeEmote.Name}");
                                    }
                                    
                                }

                                ImGui.SameLine();

                                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                                if (ImGui.BeginCombo("##addLinkedEmote", "Link Emote...", ImGuiComboFlags.HeightLargest)) {
                                    if (ImGui.IsWindowAppearing()) {
                                        searchInput = string.Empty;
                                        ImGui.SetKeyboardFocusHere();
                                    }

                                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                                    ImGui.InputTextWithHint("##searchInput", "Search...", ref searchInput, 128);

                                    if (ImGui.BeginChild("##searchScroll", new Vector2(ImGui.GetContentRegionAvail().X, 300))) {
                                        
                                        using (ImRaii.PushColor(ImGuiCol.FrameBgHovered, ImGui.GetColorU32(ImGuiCol.ButtonHovered))) {
                                            foreach (var emote in EmoteIdentifier.List) {
                                                if (!string.IsNullOrWhiteSpace(searchInput)) {
                                                    if (!(emote.Name.Contains(searchInput, StringComparison.InvariantCultureIgnoreCase) || (ushort.TryParse(searchInput, out var searchShort) && searchShort == emote.EmoteModeId))) continue;
                                                }

                                                if (emote == e.Emote || e.LinkedEmotes.Contains(emote)) continue;
                                                // if (emote.Icon == 0) continue;
                                                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                                                if (ImGuiExt.IconTextFrame(emote.Icon, emote.Name, true)) {
                                                    e.LinkedEmotes.Add(emote);
                                                }
                                            }
                                        }
                                        
                                    }

                                    ImGui.EndChild();

                                    ImGui.EndCombo();
                                }
                            }
                        }
                    }

                    if (deleteEmoteConfig != null) {
                        characterConfig.EmoteConfigs.Remove(deleteEmoteConfig);
                    }

                    ImGui.EndTable();
                }

                if (characterConfig is not IpcCharacterConfig) {
                    var currentEmote = EmoteIdentifier.Get(activeCharacterAsCharacter);
                    using (ImRaii.Disabled(currentEmote == null))
                    using (ImRaii.PushFont(UiBuilder.IconFont)) {
                        if (ImGui.Button(FontAwesomeIcon.PersonDressBurst.ToIconString(), new Vector2(checkboxSize))) {
                            if (currentEmote != null) {
                                characterConfig.EmoteConfigs.Add(new EmoteConfig() { Emote = currentEmote, Enabled = characterConfig.EmoteConfigs.All(ec => ec.Enabled == false || ec.Emote != currentEmote) });
                            }
                        }
                    }

                    if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && activeCharacterAsCharacter != null) {
                        if (currentEmote == null) {
                            ImGui.SetTooltip($"Add Current Emote");
                        } else {
                            ImGui.SetTooltip($"Add Current Emote:\n{currentEmote.Name}");
                        }
                    }

                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(250 * ImGuiHelpers.GlobalScale);
                    if (ImGui.BeginCombo("##addCurrentEmote", "Add Emote...", ImGuiComboFlags.HeightLargest)) {
                        if (ImGui.IsWindowAppearing()) {
                            searchInput = string.Empty;
                            ImGui.SetKeyboardFocusHere();
                        }

                        ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                        ImGui.InputTextWithHint("##searchInput", "Search...", ref searchInput, 128);
                        if (ImGui.BeginChild("##searchScroll", new Vector2(ImGui.GetContentRegionAvail().X, 300))) {
                            using (ImRaii.PushColor(ImGuiCol.FrameBgHovered, ImGui.GetColorU32(ImGuiCol.ButtonHovered))) {
                                foreach (var emote in EmoteIdentifier.List) {
                                    if (!string.IsNullOrWhiteSpace(searchInput)) {
                                        if (!(emote.Name.Contains(searchInput, StringComparison.InvariantCultureIgnoreCase) || (ushort.TryParse(searchInput, out var searchShort) && searchShort == emote.EmoteModeId))) continue;
                                    }
                                    
                                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                                    if (ImGuiExt.IconTextFrame(emote.Icon, emote.Name, true)) {
                                        characterConfig.EmoteConfigs.Add(new EmoteConfig() { Emote = emote, Enabled = characterConfig.EmoteConfigs.All(ec => ec.Enabled == false || ec.Emote != emote) });
                                    }
                                }
                            }
                        }

                        ImGui.EndChild();
                        ImGui.EndCombo();
                    }
                }
            }

            ImGuiExt.Separator();
            ImGuiExt.FloatEditor("Default Offset", ref characterConfig.DefaultOffset, 0.001f, allowPlusMinus: characterConfig is not IpcCharacterConfig, resetValue: 0f);
            ImGui.SameLine();
            ImGuiComponents.HelpMarker("The default offset will be used for all footwear that has not been configured.");
            ImGui.SameLine();
            ShowActiveOffsetMarker(activeCharacterAsCharacter != null && usingDefault, true, activeHeelConfig?.Is(characterConfig) ?? false, "Default offset is active");
        }

        if (Plugin.IsDebug && activeCharacter != null) {
            if (ImGui.TreeNode("Debug Information")) {
                if (ImGui.TreeNode("Active Offset")) {
                    if (activeHeelConfig == null) {
                        ImGui.TextDisabled("No Active Offset");
                    } else if (activeHeelConfig is CharacterConfig cc) {
                        ImGui.Text($"Using Default Offset: {cc.DefaultOffset}");
                    } else {
                        Util.ShowStruct(activeHeelConfig, 0);
                    }
                    ImGui.TreePop();
                }
                
                if (ImGui.TreeNode("Emote")) {
                    var emoteId = EmoteIdentifier.Get(activeCharacterAsCharacter);
                    if (emoteId == null) {
                        ImGui.TextDisabled("None");
                    } else {
                        Util.ShowObject(emoteId);
                    }
                    
                    ImGui.TreePop();
                }
                ImGui.TreePop();
            }
        }

        if (Plugin.IsDebug) {
            if (characterConfig is IpcCharacterConfig ipcCharacter && ImGui.TreeNode("IPC Data")) {
                if (ipcCharacter.EmotePosition != null && activeCharacterAsCharacter->Mode is CharacterModes.EmoteLoop or CharacterModes.InPositionLoop) {
                    ImGui.Text("Position Error:");
                    var pos = (Vector3) activeCharacter->Position;
                    var emotePos = ipcCharacter.EmotePosition.GetOffset();

                    var eR = 180f / MathF.PI * ipcCharacter.EmotePosition.R;
                    var cR = 180f / MathF.PI * activeCharacter->Rotation;
                    
                    var rotDif = 180 - MathF.Abs(MathF.Abs(eR - cR) - 180);
                    ImGui.Indent();
                    ImGui.Text($"Position: {Vector3.Distance(pos, emotePos)}");
                    ImGui.Text($"Rotation: {rotDif}");
                    if (ImGui.GetIO().KeyShift) {
                        if (PluginService.GameGui.WorldToScreen(pos, out var a)) {
                            var dl = ImGui.GetBackgroundDrawList(ImGuiHelpers.MainViewport);
                            dl.AddCircleFilled(a, 3, 0xFF0000FF);
                        }

                        if (PluginService.GameGui.WorldToScreen(emotePos, out var b)) {
                            var dl = ImGui.GetBackgroundDrawList(ImGuiHelpers.MainViewport);
                            dl.AddCircle(b, 3, 0xFF00FF00);
                        }
                    }
                    
                    ImGui.Unindent();
                }
                
                ImGui.TextWrapped(ipcCharacter.IpcJson);
                ImGui.TreePop();
            }
        }
    }

    private void ShowActiveOffsetMarker(bool show, bool isEnabled, bool isActive, string tooltipText) {
        if (!show) {
            using (ImRaii.PushFont(UiBuilder.IconFont)) 
                ImGui.Dummy(ImGui.CalcTextSize(FontAwesomeIcon.ArrowLeft.ToIconString()));
            return;
        }
        
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, isEnabled && isActive))
        using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudViolet, isEnabled && isActive == false))
        using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled), isEnabled == false))
        using (ImRaii.PushFont(UiBuilder.IconFont)) {
            ImGui.Text(FontAwesomeIcon.ArrowLeft.ToIconString());
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            using (ImRaii.Tooltip()) {
                ImGui.Text(tooltipText);
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.HealerGreen, isEnabled && isActive))
                using (ImRaii.PushColor(ImGuiCol.Text, ImGuiColors.DalamudViolet, isEnabled && isActive == false))
                using (ImRaii.PushColor(ImGuiCol.Text, ImGui.GetColorU32(ImGuiCol.TextDisabled), isEnabled == false)) {
                    ImGui.Text($"This entry is {(isActive ? "ACTIVE" : "INACTIVE")}");
                    if (!isActive) {
                        if (isEnabled) {
                            ImGui.Text("Another entry is being applied first.");
                        } else {
                            ImGui.Text("This entry has been disabled.");
                        }
                    }
                }
            }
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
            _ => ushort.MaxValue
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
        var feetModel = modelArray[(byte)slot];
        if (feetModel == null) return null;
        var modelResource = feetModel->ModelResourceHandle;
        if (modelResource == null) return null;
        return System.Text.Encoding.UTF8.GetString(modelResource->ResourceHandle.FileName.AsSpan());
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
        Plugin.SaveConfig();
        base.OnClose();
    }

    private bool MouseWithin(Vector2 min, Vector2 max) {
        var mousePos = ImGui.GetMousePos();
        return mousePos.X >= min.X && mousePos.Y <= max.X && mousePos.Y >= min.Y && mousePos.Y <= max.Y;
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
        } else if (IsOpen == false) {
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
        } else {
            notVisibleWarningCancellationTokenSource?.Cancel();
            IsOpen = false;
        }
    }

    private class ShoeModel {
        public readonly List<Item> Items = new();
        public ushort Id;

        private string? nameCache;
        public ModelSlot Slot;

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

    private enum TableHeaderAlign {
        Left,
        Center,
        Right
    }
}
