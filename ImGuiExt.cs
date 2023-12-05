using System.Numerics;
using ImGuiNET;

namespace SimpleHeels; 

public static class ImGuiExt {
    public static void Separator() {
        // Because ImGui separators suck
        ImGui.GetWindowDrawList().AddLine(ImGui.GetCursorScreenPos(), ImGui.GetCursorScreenPos() + ImGui.GetContentRegionAvail() * Vector2.UnitX, ImGui.GetColorU32(ImGuiCol.Separator));
        ImGui.Spacing();
    }
}
