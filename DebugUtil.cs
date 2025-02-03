using System;
using System.Collections;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Linq;
using System.Reflection;
using Dalamud.Plugin;
using ImGuiNET;

namespace SimpleHeels;

internal static class DebugUtil {
    public static void ClickToCopyText(string text, string? textCopy = null) {
        textCopy ??= text;
        ImGui.Text($"{text}");
        if (ImGui.IsItemHovered()) {
            ImGui.SetMouseCursor(ImGuiMouseCursor.Hand);
            if (textCopy != text) ImGui.SetTooltip(textCopy);
        }
        if (ImGui.IsItemClicked()) ImGui.SetClipboardText($"{textCopy}");
    }

    public static unsafe void ClickToCopy(void* address) {
        ClickToCopyText($"{(ulong)address:X}");
    }
    public static unsafe void ClickToCopy<T>(T* address) where T : unmanaged {
        ClickToCopy((void*) address);
    }
    
    private static ulong _beginModule;
    private static ulong _endModule;
    
    public static unsafe string GetAddressString(void* address, out bool isRelative, bool absoluteOnly = false) {
        var ulongAddress = (ulong)address;
        isRelative = false;
        if (absoluteOnly) return $"{ulongAddress:X}";
            
        try {
            if (_endModule == 0 && _beginModule == 0) {
                try {
                    _beginModule = (ulong)Process.GetCurrentProcess().MainModule!.BaseAddress.ToInt64();
                    _endModule = (_beginModule + (ulong)Process.GetCurrentProcess().MainModule!.ModuleMemorySize);
                } catch {
                    _endModule = 1;
                }
            }
        } catch {
            //
        }

        if (_beginModule > 0 && ulongAddress >= _beginModule && ulongAddress <= _endModule) {
            isRelative = true;
            return $"ffxiv_dx11.exe+{(ulongAddress - _beginModule):X}";
        }
        return $"{ulongAddress:X}";
    }

    public static unsafe string GetAddressString(void* address, bool absoluteOnly = false) => GetAddressString(address, out _, absoluteOnly);

    public static unsafe void PrintAddress(void* address) {
        var addressString = GetAddressString(address, out var isRelative);
        if (isRelative) {
            var absoluteString = GetAddressString(address, true);
            ClickToCopyText(absoluteString);
            ImGui.SameLine();
            ImGui.PushStyleColor(ImGuiCol.Text, 0xffcbc0ff);
            ClickToCopyText(addressString);
            ImGui.PopStyleColor();
        } else {
            ClickToCopyText(addressString);
        }
    }


    public static MethodInfo? showStruct;
    
    private static IList? _installedPluginsList;
    private static bool TryGetLoadedPlugin(string internalName, [NotNullWhen(true)] out IDalamudPlugin? plugin, [NotNullWhen(true)] out object? localPlugin) {
        plugin = null;
        localPlugin = null;
        if (_installedPluginsList == null) {
            var dalamudAssembly = typeof(IDalamudPluginInterface).Assembly;
            var service1T = dalamudAssembly.GetType("Dalamud.Service`1");
            if (service1T == null) throw new Exception("Failed to get Service<T> Type");
            var pluginManagerT = dalamudAssembly.GetType("Dalamud.Plugin.Internal.PluginManager");
            if (pluginManagerT == null) throw new Exception("Failed to get PluginManager Type");

            var serviceInterfaceManager = service1T.MakeGenericType(pluginManagerT);
            var getter = serviceInterfaceManager.GetMethod("Get", BindingFlags.Static | BindingFlags.Public);
            if (getter == null) throw new Exception("Failed to get Get<Service<PluginManager>> method");

            var pluginManager = getter.Invoke(null, null);

            if (pluginManager == null) throw new Exception("Failed to get PluginManager instance");

            var installedPluginsListField = pluginManager.GetType().GetField("installedPluginsList", BindingFlags.NonPublic | BindingFlags.Instance);
            if (installedPluginsListField == null) throw new Exception("Failed to get installedPluginsList field");

            _installedPluginsList = (IList?)installedPluginsListField.GetValue(pluginManager);
            if (_installedPluginsList == null) throw new Exception("Failed to get installedPluginsList value");
        }
        
        PropertyInfo? internalNameProperty = null;

        foreach (var v in _installedPluginsList) {
            internalNameProperty ??= v?.GetType().GetProperty("InternalName");
            
            if (internalNameProperty == null) continue;
            var installedInternalName = internalNameProperty.GetValue(v) as string;
            
            if (installedInternalName == internalName && v != null) {
                var t = v.GetType();
                while (t.Name != "LocalPlugin" && t.BaseType != null) t = t.BaseType;
                plugin = t.GetField("instance", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(v) as IDalamudPlugin;
                localPlugin = v;
                if (plugin != null) return true;
            }
        }

        return false;
    }
    
    public static unsafe void PrintOutObject<T>(T* s) where T : unmanaged {
        if (showStruct == null) {
            if (TryGetLoadedPlugin("SimpleTweaksPlugin", out var plugin, out var localPlugin)) {
                foreach (var t in plugin.GetType().Assembly.GetTypes()) {
                    showStruct = plugin.GetType().Assembly.GetType("SimpleTweaksPlugin.Debugging.DebugManager")?.GetMethods(BindingFlags.Public | BindingFlags.Static).FirstOrDefault(f => f.Name == "PrintOutObject" && f.GetParameters().Length == 4 && f.GetParameters()[1].ParameterType == typeof(ulong));
                }
            }
        }
        
        showStruct ??= typeof(Dalamud.Utility.Util).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).FirstOrDefault(f => f.Name == "ShowStruct" && f.GetParameters().Length == 4);
        if (showStruct == null) return;
        showStruct.Invoke(null, [ *s, (ulong) s, false, null ]);
    }
}