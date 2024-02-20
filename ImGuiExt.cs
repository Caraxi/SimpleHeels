using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;

namespace SimpleHeels;

public static class ImGuiExt {
    public static void Separator() {
        // Because ImGui separators suck
        ImGui.GetWindowDrawList().AddLine(ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + ImGui.GetContentRegionAvail() * Vector2.UnitX, ImGui.GetColorU32(ImGuiCol.Separator));
        ImGui.Spacing();
    }

    public static bool IconTextFrame(uint previewIcon, string previewText, bool hoverColor = false) {
        var pos = ImGui.GetCursorScreenPos();
        var size = new Vector2(ImGui.GetTextLineHeight()) + ImGui.GetStyle().FramePadding * 2;
        var frameSize = new Vector2(ImGui.CalcItemWidth(), ImGui.GetFrameHeight());

        using (ImRaii.PushColor(ImGuiCol.FrameBg, ImGui.GetColorU32(ImGuiCol.FrameBgHovered), hoverColor && ImGui.IsMouseHoveringRect(pos, pos + frameSize))) {
            if (ImGui.BeginChildFrame(ImGui.GetID($"iconTextFrame_{previewIcon}_{previewText}"), frameSize)) {
                var dl = ImGui.GetWindowDrawList();
                var icon = PluginService.TextureProvider.GetIcon(previewIcon);
                if (icon != null) dl.AddImage(icon.ImGuiHandle, pos, pos + new Vector2(size.Y));
                var textSize = ImGui.CalcTextSize(previewText);
                dl.AddText(pos + new Vector2(size.Y + ImGui.GetStyle().FramePadding.X, size.Y / 2f - textSize.Y / 2f), ImGui.GetColorU32(ImGuiCol.Text), previewText);
            }

            ImGui.EndChildFrame();
        }

        return ImGui.IsItemClicked();
    }

    public static bool IconButton(FontAwesomeIcon icon, Vector2? size = null, bool enabled = true, string hoverText = "", string hoverTextDisabled = "") {
        var clicked = false;
        using (ImRaii.Disabled(!enabled))
        using (ImRaii.PushFont(UiBuilder.IconFont)) {
            clicked |= ImGui.Button(icon.ToIconString(), size ?? new Vector2(ImGui.GetFrameHeight()));
        }

        if (ImGui.IsItemHovered(ImGuiHoveredFlags.AllowWhenDisabled) && (!string.IsNullOrEmpty(hoverText) || (enabled == false && !string.IsNullOrEmpty(hoverTextDisabled)))) {
            ImGui.BeginTooltip();
            if (!enabled) ImGui.Text(hoverTextDisabled);
            ImGui.Text(hoverText);
            ImGui.EndTooltip();
        }

        return clicked;
    }
}
