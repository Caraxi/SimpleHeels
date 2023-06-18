﻿using System;
using System.Numerics;
using Dalamud.Interface.Colors;
using ImGuiNET;

namespace SimpleHeels; 

public static class Changelog {
    private static void Changelogs() {
        ChangelogFor(5.1f, "0.5.1.0", () => {
            C("Now allows assigning sitting offset to characters that have only their standing offset assigned by IPC.");
        });
        ChangelogFor(5, "0.5.0.0", () => {
            C("Added support for assigning an offset when sitting in a chair.");
            C("This will not be synced until support is added through Mare Synchronos", indent: 1, color: ImGuiColors.DalamudGrey3);
        });
        
        ChangelogFor(4, "0.4.0.0", () => {
            C("Added support for Body and Legs equipment that hide shoes.");
        });
    }

    
    private static bool _displayedTitle;
    private static float _latestChangelog = 1;
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
    
    private static void ChangelogFor(float version, string label, Action draw) {
        if (version > _latestChangelog) _latestChangelog = version;
        if (!_showAll && _config != null && _config.DismissedChangelog >= version) return;
        Title();
        ImGui.Text($"{label}:");
        ImGui.Indent();
        draw();
        ImGui.Unindent();
    }

    private static void C(string text, int indent = 0, Vector4? color = null) {
        for (var i = 0; i < indent; i++) ImGui.Indent();
        if (color != null) {
            ImGui.TextColored(color.Value, $"- {text}");
        } else {
            ImGui.Text($"- {text}");
        }
        
        for (var i = 0; i < indent; i++) ImGui.Unindent();
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
