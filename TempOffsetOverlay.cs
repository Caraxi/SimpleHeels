using System;
using System.Numerics;
using Dalamud.Game.ClientState.Conditions;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Interface.Windowing;
using FFXIVClientStructs.FFXIV.Client.Game.Character;
using ImGuiNET;

namespace SimpleHeels;

public sealed unsafe class TempOffsetOverlay : Window {
    private Plugin plugin;
    private PluginConfig config;

    public TempOffsetOverlay(string name, Plugin plugin, PluginConfig config) : base(name) {
        this.plugin = plugin;
        this.config = config;

        SizeCondition = ImGuiCond.FirstUseEver;
        Size = new Vector2(180, 100);
        
        SizeConstraints = new WindowSizeConstraints() {
            MinimumSize = new Vector2(100, 100),
            MaximumSize = new Vector2(float.MaxValue, 100)
        };
        
        RespectCloseHotkey = false;

        IsOpen = true;
        PreDraw();
    }

    public override void OnClose() {
        IsOpen = true;
        config.TempOffsetWindowOpen = false;
    }

    public override void OnOpen() {
        config.TempOffsetWindowOpen = true;
    }

    public override void PreDraw() {
        ShowCloseButton = !config.TempOffsetWindowLock;

        Flags = ImGuiWindowFlags.NoScrollbar | ImGuiWindowFlags.NoScrollWithMouse | ImGuiWindowFlags.NoDocking | ImGuiWindowFlags.NoFocusOnAppearing | ImGuiWindowFlags.NoTitleBar;
        if (config.TempOffsetWindowLock) {
            Flags |= ImGuiWindowFlags.NoMove | ImGuiWindowFlags.NoResize;
            if (config.TempOffsetWindowTransparent) Flags |= ImGuiWindowFlags.NoBackground;
        }
    }

    public bool TryGetActiveCharacter(out Character* character) {
        character = (Character*)(PluginService.ClientState.LocalPlayer?.Address ?? nint.Zero);
        if (character == null) return false;
        if (character->GameObject.ObjectIndex >= Constants.ObjectLimit) return false;
        if (!character->GameObject.IsCharacter()) return false;
        return true;
    }

    public override bool DrawConditions() {
        if (!config.TempOffsetWindowOpen) return false;
        if (PluginService.ClientState.IsGPosing) return false;
        if (PluginService.Condition.Any(ConditionFlag.WatchingCutscene, ConditionFlag.WatchingCutscene78, ConditionFlag.InCombat, ConditionFlag.InFlight)) return false;
        return TryGetActiveCharacter(out _);
    }

    public override void Draw() {
        if (!TryGetActiveCharacter(out var obj)) return;

        var activeEmote = EmoteIdentifier.Get(obj);

        var tempOffset = Plugin.TempOffsets[obj->GameObject.ObjectIndex];

        var showingActive = tempOffset == null;
        var edited = false;

        if (tempOffset == null) {
            tempOffset = new TempOffset(0, 0, 0, 0);
            if (plugin.TryGetCharacterConfig(obj, out var characterConfig))
                if (characterConfig.TryGetFirstMatch(obj, out var offsetProvider))
                    (tempOffset.X, tempOffset.Y, tempOffset.Z, tempOffset.R) = offsetProvider;
        }

        ImGui.PushItemWidth(ImGui.GetContentRegionAvail().X);

        edited |= ImGuiExt.FloatEditor("##height", ref tempOffset.Y, 0.0001f, forcePlusMinus: config.TempOffsetWindowPlusMinus);
        if (config.TempOffsetWindowTooltips && ImGui.IsItemHovered()) ImGui.SetTooltip("Height");

        using (ImRaii.Disabled(activeEmote == null)) {
            edited |= ImGuiExt.FloatEditor("##forward", ref tempOffset.Z, 0.0001f, forcePlusMinus: config.TempOffsetWindowPlusMinus);
            if (config.TempOffsetWindowTooltips && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Forward / Backward");
            edited |= ImGuiExt.FloatEditor("##side", ref tempOffset.X, -0.0001f, forcePlusMinus: config.TempOffsetWindowPlusMinus);
            if (config.TempOffsetWindowTooltips && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Left / Right");
            var rot = tempOffset.R * 180f / MathF.PI;

            if (ImGuiExt.FloatEditor("##rotation", ref rot, format: "%.0f", customPlusMinus: 1, forcePlusMinus: config.TempOffsetWindowPlusMinus)) {
                edited = true;
                if (rot < 0) rot += 360;
                if (rot >= 360) rot -= 360;
                tempOffset.R = rot * MathF.PI / 180f;
            }
            if (config.TempOffsetWindowTooltips && ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled)) ImGui.SetTooltip("Rotation");
        }

        using (ImRaii.Disabled(showingActive)) {
            if (ImGui.Button(ImGui.GetContentRegionAvail().X < 105 * ImGuiHelpers.GlobalScale ? "Reset" : "Reset Offset", new Vector2(ImGui.CalcItemWidth(), ImGui.GetTextLineHeightWithSpacing()))) {
                Plugin.TempOffsets[obj->GameObject.ObjectIndex] = null;
                Plugin.TempOffsetEmote[obj->GameObject.ObjectIndex] = null;
            }
        }

        ImGui.PopItemWidth();

        if (edited) {
            Plugin.TempOffsets[obj->GameObject.ObjectIndex] = tempOffset;
            Plugin.TempOffsetEmote[obj->GameObject.ObjectIndex] = activeEmote;
        }

        // Auto Resize Height Only
        if (Size != null && ImGui.GetContentRegionAvail().Y != 0) {
            Size = Size.Value with { Y = Size.Value.Y - (ImGui.GetContentRegionAvail().Y * 1 / ImGuiHelpers.GlobalScale) };
            SizeConstraints = new WindowSizeConstraints {
                MinimumSize = Size.Value with { X = 100 },
                MaximumSize = Size.Value with { X = float.MaxValue },
            };
        }

        if (ImGui.IsWindowHovered(ImGuiHoveredFlags.AllowWhenOverlapped | ImGuiHoveredFlags.AllowWhenBlockedByPopup | ImGuiHoveredFlags.AllowWhenBlockedByActiveItem)) {
            var dl = ImGui.GetBackgroundDrawList();

            if (!config.TempOffsetWindowTransparent) {
                dl.AddRectFilled(ImGui.GetWindowPos() - new Vector2(0, ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y), ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowWidth(), 0), ImGui.GetColorU32(ImGuiCol.TitleBgActive), ImGui.GetStyle().WindowRounding, ImDrawFlags.RoundCornersTop);
            }

            if (ImGui.GetStyle().WindowBorderSize > 0) {
                dl.AddRect(ImGui.GetWindowPos() - new Vector2(0, ImGui.GetTextLineHeightWithSpacing() + ImGui.GetStyle().ItemSpacing.Y), ImGui.GetWindowPos() + new Vector2(ImGui.GetWindowWidth(), ImGui.GetWindowHeight()), ImGui.GetColorU32(ImGuiCol.Border), ImGui.GetStyle().WindowRounding, ImDrawFlags.RoundCornersTop, ImGui.GetStyle().WindowBorderSize);
                if (config.TempOffsetWindowTransparent) {
                    dl.AddLine(ImGui.GetWindowPos(), ImGui.GetWindowPos() + ImGui.GetWindowSize() with { Y = 0 }, ImGui.GetColorU32(ImGuiCol.Border), ImGui.GetStyle().WindowBorderSize);
                }
            }
            
            dl.AddText(ImGui.GetWindowPos() - new Vector2(-ImGui.GetStyle().WindowPadding.X, ImGui.GetTextLineHeightWithSpacing()), ImGui.GetColorU32(ImGuiCol.Text), "Simple Heels");
        }
    }
}
