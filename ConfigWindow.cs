using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Objects.SubKinds;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using Dalamud.Interface.Components;
using Dalamud.Interface.Windowing;
using Dalamud.Utility;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using ImGuiNET;
using Lumina.Excel.GeneratedSheets;
using Newtonsoft.Json;
using World = Lumina.Excel.GeneratedSheets.World;

namespace SimpleHeels; 

public class ConfigWindow : Window {
    private readonly PluginConfig config;
    private readonly Plugin plugin;

    public ConfigWindow(string name, Plugin plugin, PluginConfig config) : base(name) {
        this.config = config;
        this.plugin = plugin;
        
        SizeConstraints = new WindowSizeConstraints {
            MinimumSize = ImGuiHelpers.ScaledVector2(800, 400),
            MaximumSize = new Vector2(float.MaxValue)
        };

        Size = ImGuiHelpers.ScaledVector2(1000, 500);
        SizeCondition = ImGuiCond.FirstUseEver;

    }
    
    private Vector2 iconButtonSize = new(16);
    private float checkboxSize = 36;

    


    public void DrawCharacterList() {

        foreach (var (worldId, characters) in config.WorldCharacterDictionary.ToArray()) {
            var world = PluginService.Data.GetExcelSheet<World>()?.GetRow(worldId);
            if (world == null) continue;
            
            ImGui.TextDisabled($"{world.Name.RawString}");
            ImGui.Separator();

            foreach (var (name, characterConfig) in characters.ToArray()) {
                if (ImGui.Selectable($"{name}##{world.Name.RawString}", selectedCharacter == characterConfig)) {
                    selectedCharacter = characterConfig;
                    selectedName = name;
                    selectedWorld = world.RowId;
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

        if (Plugin.IsDebug && Plugin.IpcAssignedOffset.Count > 0) {
            ImGui.TextDisabled("[DEBUG] IPC Assignments");
            ImGui.Separator();

            foreach (var (name, worldId) in Plugin.IpcAssignedOffset.Keys.ToArray()) {
                var world = PluginService.Data.GetExcelSheet<World>()?.GetRow(worldId);
                if (world == null) continue;
                if (ImGui.Selectable($"{name}##{world.Name.RawString}##ipc", selectedName == name && selectedWorld == worldId)) {
                    config.TryGetCharacterConfig(name, worldId, out selectedCharacter);
                    selectedCharacter ??= new CharacterConfig();
                    selectedName = name;
                    selectedWorld = world.RowId;
                }
                ImGui.SameLine();
                ImGui.TextDisabled(world.Name.ToDalamudString().TextValue);
            }
            
            ImGuiHelpers.ScaledDummy(10);
        }

    }

    private CharacterConfig? selectedCharacter;
    private string selectedName = string.Empty;
    private uint selectedWorld;

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
                    var obj = (GameObjectExt*)activePlayer.Address;
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

                    ImGui.Text($"Object Type: {obj->DrawObject->Object.GetObjectType()}");
                    if (obj->DrawObject->Object.GetObjectType() == ObjectType.CharacterBase) {
                        var characterBase = (CharacterBase*)obj->DrawObject;
                        ImGui.Text($"Model Type: {characterBase->GetModelType()}");
                        if (characterBase->GetModelType() == CharacterBase.ModelType.Human) {
                            var human = (Human*)obj->DrawObject;
                            ImGui.Text($"Current Footwear ID: {human->Feet.Id}, {human->Feet.Variant}");
                            ImGui.Text($"Current Footwear Name: {GetModelName(human->Feet.Id)}");
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
        if (!Plugin.IsEnabled) {
            ImGui.TextColored(ImGuiColors.DalamudRed, $"{plugin.Name} is currently disabled due to Heels Plugin being installed.\nPlease uninstall Heels Plugin to allow {plugin.Name} to run.");
            foreach (var n in PluginService.PluginInterface.PluginInternalNames) {
                ImGui.Text($"{n}");
            }
            return;
        }

        ImGui.BeginGroup();
        {
            if (ImGui.BeginChild("character_select", ImGuiHelpers.ScaledVector2(240, 0) - iconButtonSize with { X = 0 }, true)) {
                DrawCharacterList();
            }
            ImGui.EndChild();

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
            
            if (ImGuiComponents.IconButton(FontAwesomeIcon.Cog)) {
                selectedCharacter = null;
                selectedName = string.Empty;
                selectedWorld = 0;
            }
            if (ImGui.IsItemHovered()) ImGui.SetTooltip("Plugin Options");
            iconButtonSize = ImGui.GetItemRectSize() + ImGui.GetStyle().ItemSpacing;
        }
        ImGui.EndGroup();
        
        ImGui.SameLine();
        if (ImGui.BeginChild("character_view", ImGuiHelpers.ScaledVector2(0), true)) {
            if (selectedCharacter != null) {
                ShowDebugInfo();
                if (Plugin.IpcAssignedOffset.TryGetValue((selectedName, selectedWorld), out var offset)) {

                    ImGui.Text("This character's offset is currently assigned by another plugin.");
                    if (Plugin.IsDebug && ImGui.Button("Clear IPC Assignment")) {
                        Plugin.IpcAssignedOffset.Remove((selectedName, selectedWorld));
                    }
                    
                    ImGui.Text($"Assigned Offset: {offset}");
                    
                    
                    return;
                }
                
                DrawCharacterView(selectedCharacter);
            } else {
                
                ImGui.Text("SimpleHeels Options");
                ImGui.Separator();

                ImGui.Checkbox("Show Plus/Minus buttons for offset adjustments", ref config.ShowPlusMinusButtons);
                if (config.ShowPlusMinusButtons) {
                    ImGui.SetNextItemWidth(200 * ImGuiHelpers.GlobalScale);
                    ImGui.SliderFloat("Plus/Minus Button Delta", ref config.PlusMinusDelta, 0.0001f, 0.01f, "%.4f", ImGuiSliderFlags.AlwaysClamp);
                }
                
                #if DEBUG
                ImGui.Checkbox("[DEBUG] Open config window on startup", ref config.DebugOpenOnStartup);
                #endif
            }
            
        }
        ImGui.EndChild();
    }


    private string footwearSearch = string.Empty;
    
    private void DrawCharacterView(CharacterConfig? characterConfig) {
        if (characterConfig == null) return;
        var activeFootwear = GetFootwearForPlayer(selectedName, selectedWorld);
        
        if (ImGui.BeginTable("OffsetsTable", 5)) {
            ImGui.TableSetupColumn("Enable", ImGuiTableColumnFlags.WidthFixed, checkboxSize * 2 + 1);
            ImGui.TableSetupColumn("Label", ImGuiTableColumnFlags.WidthFixed, 120 * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Offset", ImGuiTableColumnFlags.WidthFixed, (90 + (config.ShowPlusMinusButtons ? 50 : 0)) * ImGuiHelpers.GlobalScale);
            ImGui.TableSetupColumn("Footwear", ImGuiTableColumnFlags.WidthStretch);
            ImGui.TableSetupColumn("", ImGuiTableColumnFlags.WidthFixed, checkboxSize);
            ImGui.TableHeadersRow();

            var deleteIndex = -1;
            for (var i = 0; i < characterConfig.HeelsConfig.Count; i++) {
                ImGui.PushID($"heels_{i}");
                var heelConfig = characterConfig.HeelsConfig[i];
                heelConfig.Label ??= string.Empty;
                ImGui.TableNextRow();
                ImGui.TableNextColumn();

                ImGui.PushStyleVar(ImGuiStyleVar.ItemSpacing, Vector2.One);
                ImGui.PushFont(UiBuilder.IconFont);

                if (ImGui.Button($"{(char)FontAwesomeIcon.Trash}##delete", new Vector2(checkboxSize)) && ImGui.GetIO().KeyShift) {
                    deleteIndex = i;
                }

                if (ImGui.IsItemHovered() && !ImGui.GetIO().KeyShift) {
                    ImGui.PopFont();
                    ImGui.SetTooltip("Hold SHIFT to delete.");
                    ImGui.PushFont(UiBuilder.IconFont);
                }

                ImGui.SameLine();
                ImGui.PopFont();
                ImGui.PopStyleVar();
                if (ImGui.Checkbox("##enable", ref heelConfig.Enabled)) {
                    if (heelConfig.Enabled) {
                        foreach (var heel in characterConfig.HeelsConfig.Where(h => h.ModelId == heelConfig.ModelId)) {
                            heel.Enabled = false;
                        }
                        heelConfig.Enabled = true;
                    }
                }

                if (i == 0) checkboxSize = ImGui.GetItemRectSize().X;

                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                ImGui.InputText("##label", ref heelConfig.Label, 100);
                
                ImGui.TableNextColumn();

                if (plugin.Config.ShowPlusMinusButtons) {
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Minus)) {
                        heelConfig.Offset -= plugin.Config.PlusMinusDelta;
                        if (heelConfig.Enabled) Plugin.RequestUpdateAll();
                    }
                    ImGui.SameLine();
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X - ImGui.GetItemRectSize().X - ImGui.GetStyle().ItemSpacing.X);
                    if (ImGui.DragFloat("##offset", ref heelConfig.Offset, 0.001f, -1, 1, "%.4f", ImGuiSliderFlags.AlwaysClamp)) {
                        if (heelConfig.Enabled) Plugin.RequestUpdateAll();
                    }
                    ImGui.SameLine();
                    if (ImGuiComponents.IconButton(FontAwesomeIcon.Plus)) {
                        heelConfig.Offset += plugin.Config.PlusMinusDelta;
                        if (heelConfig.Enabled) Plugin.RequestUpdateAll();
                    }
                    
                    
                } else {
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.DragFloat("##offset", ref heelConfig.Offset, 0.001f, -1, 1, "%.4f", ImGuiSliderFlags.AlwaysClamp)) {
                        if (heelConfig.Enabled) Plugin.RequestUpdateAll();
                    }
                }
                
                ImGui.TableNextColumn();
                ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                if (ImGui.BeginCombo("##footwear", GetModelName(heelConfig.ModelId), ImGuiComboFlags.HeightLargest)) {
                    ImGui.SetNextItemWidth(ImGui.GetContentRegionAvail().X);
                    if (ImGui.IsWindowAppearing()) {
                        footwearSearch = string.Empty;
                        ImGui.SetKeyboardFocusHere();
                    }
                    ImGui.InputTextWithHint("##footwearSearch", "Search...", ref footwearSearch, 100);
                    
                    if (ImGui.BeginChild("##footwearSelectScroll", new Vector2(-1, 200))) {
                        foreach (var shoeModel in shoeModelList.Value.Values) {
                            if (!string.IsNullOrWhiteSpace(footwearSearch)) {
                                if (!((shoeModel.Name ?? $"Unknown#{shoeModel.Id}").Contains(footwearSearch, StringComparison.InvariantCultureIgnoreCase) || shoeModel.Items.Any(shoeItem => shoeItem.Name.ToDalamudString().TextValue.Contains(footwearSearch, StringComparison.InvariantCultureIgnoreCase)))) {
                                    continue;
                                }
                            }
                            
                            if (ImGui.Selectable($"{shoeModel.Name}##shoeModel_{shoeModel.Id}")) {
                                heelConfig.ModelId = shoeModel.Id;
                                ImGui.CloseCurrentPopup();
                            }

                            if (ImGui.IsItemHovered() && shoeModel.Items.Count > 3) {
                                ShowModelTooltip(shoeModel.Id);
                            }
                        }
                    }
                    
                    ImGui.EndChild();
                    ImGui.EndCombo();
                }
                
                ImGui.TableNextColumn();
                if (activeFootwear == heelConfig.ModelId) {
                    ImGui.PushFont(UiBuilder.IconFont);
                    ImGui.Text($"{(char)FontAwesomeIcon.ArrowLeft}");
                    ImGui.PopFont();
                    if (ImGui.IsItemHovered()) {
                        ImGui.SetTooltip("Currently Wearing");
                    }
                }

                ImGui.PopID();
            }

            if (deleteIndex >= 0) {
                characterConfig.HeelsConfig.RemoveAt(deleteIndex);
            }

            ImGui.EndTable();

            if (ImGui.Button("Add an Entry")) {
                characterConfig.HeelsConfig.Add(new HeelConfig() {
                    ModelId = GetFootwearForPlayer(selectedName, selectedWorld)
                });
            }

            if (characterConfig.HeelsConfig.Count > 0) return;
            
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

    private string? heelsConfigPath;
    private bool? heelsConfigExists;
    
    

    private readonly Lazy<Dictionary<ushort, ShoeModel>> shoeModelList = new(() => {
        var dict = new Dictionary<ushort, ShoeModel> {
            [0] = new() { Id = 0, Name = "Smallclothes (Barefoot)"}
        };

        foreach (var item in PluginService.Data.GetExcelSheet<Item>()!.Where(i => i.EquipSlotCategory?.Value?.Feet == 1)) {
            var modelBytes = BitConverter.GetBytes(item.ModelMain);
            var modelId = BitConverter.ToUInt16(modelBytes, 0);
            
            if (!dict.ContainsKey(modelId)) dict.Add(modelId, new ShoeModel { Id = modelId });
            
            dict[modelId].Items.Add(item);
            dict[modelId].Name = null;
        }

        return dict;
    });

    private class ShoeModel {
        public ushort Id;
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

                return nameCache;
            }
            set => nameCache = value;
        }
    }

    private static unsafe ushort GetFootwearForPlayer(string name, uint world) {
        var player = PluginService.Objects.FirstOrDefault(t => t is PlayerCharacter playerCharacter && playerCharacter.Name.TextValue == name && playerCharacter.HomeWorld.Id == world);
        if (player is not PlayerCharacter) return 0;
        var obj = (GameObjectExt*)player.Address;
        if (obj->DrawObject == null) return 0;
        if (obj->DrawObject->Object.GetObjectType() != ObjectType.CharacterBase) return 0;
        var characterBase = (CharacterBase*)obj->DrawObject;
        if (characterBase->GetModelType() != CharacterBase.ModelType.Human) return 0;
        var human = (Human*)obj->DrawObject;
        if (human == null) return 0;
        return human->Feet.Id;
    }
    
    private string GetModelName(ushort modelId) {
        if (modelId == 0) return "Smallclothes (Barefoot)";

        if (shoeModelList.Value.TryGetValue(modelId, out var shoeModel)) {
            return shoeModel.Name ?? $"Unknown#{modelId}";
        }
        
        return $"Unknown#{modelId}";
    }

    private void ShowModelTooltip(ushort modelId) {
        
        ImGui.BeginTooltip();

        try {
            if (modelId == 0) {
                ImGui.Text("Smallclothes (Barefoot)");
                return;
            }

            if (shoeModelList.Value.TryGetValue(modelId, out var shoeModel)) {

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
}
