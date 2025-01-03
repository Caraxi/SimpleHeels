using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using Dalamud.Game.ClientState.Keys;
using Dalamud.Interface.Utility;
using Dalamud.Interface.Utility.Raii;
using ImGuiNET;

namespace SimpleHeels;

public static class HotkeyHelper {
    private static string? _settingKey;
    private static string? _focused;
    private static readonly List<VirtualKey> NewKeys = [];

    private static readonly Dictionary<VirtualKey, string> NamedKeys = new() {
        { VirtualKey.KEY_0, "0" },
        { VirtualKey.KEY_1, "1" },
        { VirtualKey.KEY_2, "2" },
        { VirtualKey.KEY_3, "3" },
        { VirtualKey.KEY_4, "4" },
        { VirtualKey.KEY_5, "5" },
        { VirtualKey.KEY_6, "6" },
        { VirtualKey.KEY_7, "7" },
        { VirtualKey.KEY_8, "8" },
        { VirtualKey.KEY_9, "9" },
        { VirtualKey.CONTROL, "Ctrl" },
        { VirtualKey.MENU, "Alt" },
        { VirtualKey.SHIFT, "Shift" },
        { VirtualKey.CAPITAL, "CapsLock"}
    };

    public static string GetKeyName(this VirtualKey k) => NamedKeys.ContainsKey(k) ? NamedKeys[k] : k.ToString();

    public static bool CheckHotkeyState(VirtualKey[] keys, bool clearOnPressed = true) {
        foreach (var vk in PluginService.KeyState.GetValidVirtualKeys()) {
            if (keys.Contains(vk)) {
                if (!PluginService.KeyState[vk]) return false;
            } else {
                if (PluginService.KeyState[vk]) return false;
            }
        }

        if (clearOnPressed) {
            foreach (var k in keys) {
                PluginService.KeyState[(int)k] = false;
            }
        }

        return true;
    }

    public static bool DrawHotkeyConfigEditor(string name, VirtualKey[] keys, out VirtualKey[] outKeys, bool buttonOnSameLine = false) {
        using var group = ImRaii.Group();
        outKeys = [];
        var modified = false;
        var identifier = name.Contains("###") ? $"{name.Split("###", 2)[1]}" : name;
        var strKeybind = string.Join("+", keys.Select(k => k.GetKeyName()));

        ImGui.SetNextItemWidth(100 * ImGuiHelpers.GlobalScale);

        if (_settingKey == identifier) {
            if (ImGui.GetIO()
                    .KeyAlt && !NewKeys.Contains(VirtualKey.MENU))
                NewKeys.Add(VirtualKey.MENU);
            if (ImGui.GetIO()
                    .KeyShift && !NewKeys.Contains(VirtualKey.SHIFT))
                NewKeys.Add(VirtualKey.SHIFT);
            if (ImGui.GetIO()
                    .KeyCtrl && !NewKeys.Contains(VirtualKey.CONTROL))
                NewKeys.Add(VirtualKey.CONTROL);

            for (var k = 0;
                 k < ImGui.GetIO()
                     .KeysDown.Count && k < 160;
                 k++) {
                if (ImGui.GetIO()
                    .KeysDown[k]) {
                    if (!NewKeys.Contains((VirtualKey)k)) {
                        if ((VirtualKey)k == VirtualKey.ESCAPE) {
                            _settingKey = null;
                            NewKeys.Clear();
                            _focused = null;
                            break;
                        }

                        NewKeys.Add((VirtualKey)k);
                    }
                }
            }

            NewKeys.Sort();
            strKeybind = string.Join("+", NewKeys.Select(k => k.GetKeyName()));
        }

        using (ImRaii.PushStyle(ImGuiStyleVar.FrameBorderSize, 2))
        using (ImRaii.PushColor(ImGuiCol.Border, 0xFF00A5FF, _settingKey == identifier)) {
            ImGui.InputText(name, ref strKeybind, 100, ImGuiInputTextFlags.ReadOnly);
        }

        var active = ImGui.IsItemActive();

        if (_settingKey == identifier) {
            if (_focused != identifier) {
                ImGui.SetKeyboardFocusHere(-1);
                _focused = identifier;
            } else {
                var cPos = ImGui.GetCursorScreenPos();
                if (!buttonOnSameLine) ImGui.SetCursorScreenPos(ImGui.GetItemRectMin() - (ImGui.GetItemRectSize() + ImGui.GetStyle().ItemSpacing) * Vector2.UnitY);
                if (buttonOnSameLine) ImGui.SameLine();
                if (ImGui.Button(NewKeys.Count > 0 ? $"Confirm##{identifier}" : $"Cancel##{identifier}", ImGui.GetItemRectSize())) {
                    _settingKey = null;
                    if (NewKeys.Count > 0) {
                        outKeys = NewKeys.ToArray();
                        modified = true;
                    }

                    NewKeys.Clear();
                } else {
                    if (!active) {
                        _focused = null;
                        _settingKey = null;
                        if (NewKeys.Count > 0) {
                            outKeys = NewKeys.ToArray();
                            modified = true;
                        }

                        NewKeys.Clear();
                    }
                }
                if (!buttonOnSameLine) ImGui.SetCursorScreenPos(cPos);
            }
        } else {
            
            var cPos = ImGui.GetCursorScreenPos();
            if (!buttonOnSameLine) ImGui.SetCursorScreenPos(ImGui.GetItemRectMin() - (ImGui.GetItemRectSize() + ImGui.GetStyle().ItemSpacing) * Vector2.UnitY);
            if (buttonOnSameLine) ImGui.SameLine();
            if (ImGui.Button("Set Keybind###setHotkeyButton{identifier}", ImGui.GetItemRectSize())) {
                _settingKey = identifier;
            }

            if (!buttonOnSameLine) ImGui.SetCursorScreenPos(cPos);
        }

        return modified;
    }
}
