using System;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Components;
using Dalamud.Interface.Utility.Raii;
using Dalamud.Bindings.ImGui;

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
                var icon = PluginService.TextureProvider.GetFromGameIcon(previewIcon).GetWrapOrDefault();
                if (icon != null) dl.AddImage(icon.Handle, pos, pos + new Vector2(size.Y));
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

    private static Stopwatch _clickHoldThrottle = Stopwatch.StartNew();
    private static Stopwatch _holdingClick = Stopwatch.StartNew();

    public static bool FloatEditor(string label, ref float value, float speed = 1, float min = float.MinValue, float max = float.MaxValue, string format = "%.5f", ImGuiSliderFlags flags = ImGuiSliderFlags.None, bool allowPlusMinus = true, float? customPlusMinus = null, bool? forcePlusMinus = null, float? resetValue = null) {
        if (_holdingClick.IsRunning && !ImGui.IsMouseDown(ImGuiMouseButton.Left)) _holdingClick.Restart();
        
        using var group = ImRaii.Group();
        var showPlusMinus = allowPlusMinus && (forcePlusMinus ?? Plugin.Config.ShowPlusMinusButtons);
        var c = false;
        var w = ImGui.CalcItemWidth();

        if (showPlusMinus) {
            if (ImGuiComponents.IconButton($"##{label}_minus", FontAwesomeIcon.Minus) || (_holdingClick.ElapsedMilliseconds > 500 && _clickHoldThrottle.ElapsedMilliseconds > 50 && ImGui.IsItemHovered() && ImGui.IsMouseDown(ImGuiMouseButton.Left))) {
                _clickHoldThrottle.Restart();
                value -= customPlusMinus ?? Plugin.Config.PlusMinusDelta;
                c = true;
            }

            w -= ImGui.GetItemRectSize().X * 2 + ImGui.GetStyle().ItemSpacing.X * 2;
            ImGui.SameLine();
        }

        ImGui.SetNextItemWidth(w);

        c |= ImGui.DragFloat($"##{label}_slider", ref value, speed, min, max, format, flags);

        if (resetValue != null && Plugin.Config.RightClickResetValue && ImGui.IsItemClicked(ImGuiMouseButton.Right) && ImGui.GetIO().KeyShift) {
            value = resetValue.Value;
            c = true;
        }
        
        if (showPlusMinus) {
            ImGui.SameLine();
            if (ImGuiComponents.IconButton($"##{label}_plus", FontAwesomeIcon.Plus) || (_holdingClick.ElapsedMilliseconds > 500 && _clickHoldThrottle.ElapsedMilliseconds > 50 && ImGui.IsItemHovered() && ImGui.IsMouseDown(ImGuiMouseButton.Left))) {
                _clickHoldThrottle.Restart();
                value += customPlusMinus ?? MathF.Round(Plugin.Config.PlusMinusDelta, 5, MidpointRounding.AwayFromZero);
                c = true;
            }
        }

        var displayText = label.Split("##")[0];
        if (!string.IsNullOrEmpty(displayText)) {
            ImGui.SameLine();
            ImGui.Text(displayText);
        }
        
        return c;
    }
}
