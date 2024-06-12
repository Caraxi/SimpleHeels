using System;
using System.Numerics;
using Dalamud.Interface;
using Dalamud.Interface.Colors;
using ImGuiNET;

namespace SimpleHeels;

public static class Changelog {
    private static bool _displayedTitle;
    private static float _latestChangelog = 1;
    private static PluginConfig? _config;
    private static bool _showAll;

    private static int _configIndex;
    private static bool _isOldExpanded;

    private static void Changelogs() {
        ChangelogFor(9.35f, "0.9.3.5", "Fix support for new worlds.");
        ChangelogFor(9.34f, "0.9.3.4", "Fixed issue causing other players to appear to teleport for a brief moment when using emotes with precise positioning.");
        ChangelogFor(9.31f, "0.9.3.1", () => {
            C("Added option to allow right clicking offset inputs to return to zero.");
            C("Added button to emote offsets to reset all values to zero.");
            C("Increased range for precise positioning.");
        });
        ChangelogFor(9.3f, "0.9.3.0", "Added optional precise position sharing when performing looping emotes.");
        ChangelogFor(9.2f, "0.9.2.0", "Added optional stationary minion position sharing.");
        ChangelogFor(9.13f, "0.9.1.3", "Fix Plus/Minus buttons applying change multiple times per click.");
        ChangelogFor(9.12f, "0.9.1.2", "Apply temp offsets from synced players to gpose clones.");
        ChangelogFor(9.1f, "0.9.1.0", () => {
            C("Added Temporary Offsets");
            C("Allows setting offsets that are not saved into configs.", 1);
            C("Overrides all other active offsets.", 1);
            C("Edited from overlay window.", 1);
            C("/heels temp", 2);
            C("Can lock and customize overlay window in the plugin settings.", 2);
        });
        ChangelogFor(9.08f, "0.9.0.8", "Fixed reporting emote offsets to other plugins.");
        ChangelogFor(9.07f, "0.9.0.7", () => {
            C("Added an option to allow group offsets to partially apply to minions.");
            C("Removed legacy data from synced offsets.");
        });
        ChangelogFor(9.06f, "0.9.0.6", "Attempt to fix the positioning of other players in GPose.");
        ChangelogFor(9.05f, "0.9.0.5", () => {
            C("Dimmed character and group names in config window when disabled.");
            C("Fixed some UI not functioning correctly");
        });
        ChangelogFor(9.02f, "0.9.0.2", "Fixed applying offsets to GPose actors.");
        ChangelogFor(9.0f, "0.9.0.0", () => {
            C("Major rework of internals");
            C("Added 'Emote Offsets'");
            C("Allows Full 3D positioning while performing emotes.", 1);
            C("Allows rotation while performing emotes.", 1);
            C("Different Sitting and Sleeping poses can be assigned individual offsets.", 1);
            C("");
        });
        ChangelogFor(8.50f, "0.8.5.0", () => {
            C("Added ability to disable processing of config groups.");
            C("Added ability to toggle visibility of the 'Move/Copy' UI in character configs.");
            C("Fixed issue causing config window to be larger than intended on higher resolution screens.");
            C("The 'Create Group' option from an existing character config now requires holding SHIFT.");
            C("Improved 'active offset' displays for groups.");
            C("Added option to prefer model paths over equipment ID when adding new entries.");
        });
        ChangelogFor(8.42f, "0.8.4.2", "Fixed an issue causing synced ground sitting offset from not appearing when chair sitting offsets are at zero.");
        ChangelogFor(8.41f, "0.8.4.1", () => { C("Improved support for baked in model offsets, allowing mod developers to define offsets in TexTools."); });
        ChangelogFor(8.4f, "0.8.4.0", () => {
            C("Added a 'Default Offset' to apply to all unconfigured footwear.");
            C("Offset will no longer be applied while crafting.");
        });
        ChangelogFor(8.3f, "0.8.3.0", "Reapers will no longer have their offset changed while under the effect of Enshroud.");
        ChangelogFor(8.2f, "0.8.2.0", () => {
            C("Added name filtering for groups.");
            C("A character can appear in multiple groups, the top group will be applied first.", 1);
        });
        ChangelogFor(8.11f, "0.8.1.1", "Fixed offsets not applying in gpose and cutscenes.");
        ChangelogFor(8f, "0.8.0.0", () => {
            C("Added an option to create a group from a configured character.");
            C("Added an option to ignore Dalamud's 'Hide UI while GPose is active' option.");
            C("Added options to assign an offset for ground sitting and sleeping.");
        });
        ChangelogFor(7f, "0.7.0.0", () => {
            C("Defaulted new heel entries to enabled");
            C("Improved UX");
            C("Made it more clear when no config is enabled", 1);
            C("Made it more clear which entry is currently active.", 1);
            C("Added a note explaining conflicts when wearing multiple items that are configured", 1);
            C("Entries can now be enabled or disabled when locked.");
        });
        ChangelogFor(6.3f, "0.6.3.0", () => {
            C("Improved ordering method a bit");
            C("Added a lock to entries");
            C("Added method of renaming or copying character configs.");
        });
        ChangelogFor(6.2f, "0.6.2.0", () => { C("Added a way to reorder heel config entries."); });
        ChangelogFor(6.12f, "0.6.1.3", "Another attempt to fix offset getting stuck for some people.");
        ChangelogFor(6.12f, "0.6.1.2", "Fixed plugin breaking when character is redrawn by Penumbra or Glamourer.");
        ChangelogFor(6.11f, "0.6.1.1", "Fixed 0 offset not being reported correctly to other plugins.");
        ChangelogFor(6.10f, "0.6.1.0", "Allow NPC characters to have their offsets assigned by groups.");
        ChangelogFor(6.00f, "0.6.0.0", () => {
            C("Added Groups (");
            ImGui.SameLine();
            ImGui.PushFont(UiBuilder.IconFont);
            ImGui.Text($"{(char)FontAwesomeIcon.PeopleGroup}");
            ImGui.PopFont();
            ImGui.SameLine();
            ImGui.Text(") to allow setting offsets to a range of characters");
        });
        ChangelogFor(5.11f, "0.5.1.1", "Increased maximum offset value.");
        ChangelogFor(5.1f, "0.5.1.0", () => {
            C("Now allows assigning sitting offset to characters that have only their standing offset assigned by IPC.");
            C("Now applies offsets to GPose and Cutscene actors.");
            C("Due to the way the game handles cutscenes, cutscenes featuring non-standing poses will be incorrect.", 1, ImGuiColors.DalamudGrey3);
        });
        ChangelogFor(5, "0.5.0.0", () => {
            C("Added support for assigning an offset when sitting in a chair.");
            C("This will not be synced until support is added through Mare Synchronos", 1, ImGuiColors.DalamudGrey3);
        });

        ChangelogFor(4, "0.4.0.0", "Added support for Body and Legs equipment that hide shoes.");
    }

    private static void Title() {
        if (_displayedTitle) return;
        _displayedTitle = true;
        ImGui.Text("Changelog");

        if (!_showAll && _config != null) {
            ImGui.SameLine();
            if (ImGui.SmallButton("Dismiss")) _config.DismissedChangelog = _latestChangelog;
        }

        ImGuiExt.Separator();
    }

    private static void ChangelogFor(float version, string label, Action draw) {
        _configIndex++;
        if (version > _latestChangelog) _latestChangelog = version;
        if (!_showAll && _config != null && _config.DismissedChangelog >= version) return;
        if (_configIndex == 4) _isOldExpanded = ImGui.TreeNodeEx("Old Versions", ImGuiTreeNodeFlags.NoTreePushOnOpen);
        if (_configIndex >= 4 && _isOldExpanded == false) return;
        Title();
        ImGui.Text($"{label}:");
        ImGui.Indent();
        draw();
        ImGui.Unindent();
    }

    private static void ChangelogFor(float version, string label, string singleLineChangelog) {
        ChangelogFor(version, label, () => { C(singleLineChangelog); });
    }

    private static void C(string text, int indent = 0, Vector4? color = null) {
        for (var i = 0; i < indent; i++) ImGui.Indent();
        if (color != null)
            ImGui.TextColored(color.Value, $"- {text}");
        else
            ImGui.Text($"- {text}");

        for (var i = 0; i < indent; i++) ImGui.Unindent();
    }

    public static bool Show(PluginConfig config, bool showAll = false) {
        _displayedTitle = false;
        _config = config;
        _showAll = showAll;
        _configIndex = 0;
        Changelogs();
        if (_displayedTitle) {
            ImGui.Spacing();
            ImGui.Spacing();
            ImGuiExt.Separator();
            return true;
        }

        return false;
    }
}
