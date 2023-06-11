using System;
using ImGuiNET;

namespace SimpleHeels; 

public static class Changelog {
    private static void Changelogs() {
        
        ChangelogFor(4, "0.4.0.0", () => {
            ImGui.Text("- Added support for Body and Legs equipment that hide shoes.");
        });
        
    }

    
    private static bool _displayedTitle;
    private static int _latestChangelog = 1;
    private static PluginConfig? _config;
    private static bool _showAll;

    
    private static void Title() {
        if (_displayedTitle) return;
        _displayedTitle = true;
        ImGui.Text("Changelog");

        if (!_showAll && _config != null) {
            ImGui.SameLine();
            if (ImGui.SmallButton("Dismiss")) {
                _config.DismissedChangelog = _latestChangelog;
            }
        }

        ImGui.Separator();
    }
    
    private static void ChangelogFor(int version, string label, Action draw) {
        if (version > _latestChangelog) _latestChangelog = version;
        if (!_showAll && _config != null && _config.DismissedChangelog >= version) return;
        Title();
        ImGui.Text($"{label}:");
        ImGui.Indent();
        draw();
        ImGui.Unindent();
    }
    
    public static void Show(PluginConfig config, bool showAll = false) {
        _displayedTitle = false;
        _config = config;
        _showAll = showAll;
        Changelogs();
        if (_displayedTitle) {
            ImGui.Spacing();
            ImGui.Spacing();
        }
    }
}
