using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Numerics;
using Dalamud.Interface.Utility;
using Dalamud.Bindings.ImGui;

namespace SimpleHeels;

public static class PerformanceMonitors {
    private static DisplayType _displayType = DisplayType.Milliseconds;

    private static readonly Dictionary<string, PerformanceLog> Logs = new();

    public static PerformanceLogger? Run(string k, bool enable = true) {
        if (!(Plugin.IsDebug && enable)) return null;
        return new PerformanceLogger(k);
    }

    private static void DisplayValue(long ticks) {
        var text = _displayType switch {
            DisplayType.Ticks => $"{ticks}",
            DisplayType.Milliseconds => $"{ticks / (float)TimeSpan.TicksPerMillisecond: 0.000}ms",
            _ => $"{ticks}"
        };

        ImGui.Text($"{text}");
    }

    public static void DrawTable(float? height = null) {
        if (ImGui.Button("Reset All")) ClearAll();

        ImGui.SameLine();
        ImGui.SetNextItemWidth(150);
        if (ImGui.BeginCombo("Display Type", $"{_displayType}")) {
            foreach (var e in Enum.GetValues<DisplayType>())
                if (ImGui.Selectable($"{e}", _displayType == e))
                    _displayType = e;
            ImGui.EndCombo();
        }

        if (ImGui.BeginTable("performanceTable", 8, ImGuiTableFlags.Borders | ImGuiTableFlags.ScrollX | ImGuiTableFlags.Resizable, new Vector2(ImGui.GetContentRegionAvail().X, height ?? 150 * ImGuiHelpers.GlobalScale))) {
            ImGui.TableSetupColumn("Reset");
            ImGui.TableSetupColumn("Key");
            ImGui.TableSetupColumn("Last Check");
            ImGui.TableSetupColumn("Average");
            ImGui.TableSetupColumn("Maximum");
            ImGui.TableSetupColumn("Per Second");
            ImGui.TableSetupColumn("Count");
            ImGui.TableSetupColumn("Hits per Second");

            ImGui.TableHeadersRow();

            foreach (var log in Logs) {
                if (log.Value.Count == 0) continue;
                ImGui.TableNextColumn();
                if (ImGui.SmallButton($"Reset##{log.Key}")) log.Value.Clear();
                ImGui.TableNextColumn();
                ImGui.Text($"{log.Key}");
                ImGui.TableNextColumn();
                DisplayValue(log.Value.Last);
                ImGui.TableNextColumn();
                DisplayValue(log.Value.Average);
                ImGui.TableNextColumn();
                DisplayValue(log.Value.Max);
                ImGui.TableNextColumn();
                DisplayValue((long)log.Value.AveragePerSecond);
                ImGui.TableNextColumn();
                ImGui.Text($"{log.Value.Count}");
                ImGui.TableNextColumn();
                ImGui.Text($"{log.Value.HitsPerSecond:F2}");
            }

            ImGui.EndTable();
        }
    }

    public static void Begin(string k) {
        Logs.TryAdd(k, new PerformanceLog());
        Logs[k].Begin();
    }

    public static void End(string k) {
        if (!Logs.ContainsKey(k)) return;
        Logs[k].End();
    }

    public static void ClearAll() {
        foreach (var l in Logs) l.Value.Clear();
    }

    public class PerformanceLogger : IDisposable {
        private readonly string runKey;

        public PerformanceLogger(string k) {
            Logs.TryAdd(k, new PerformanceLog());
            Logs[k].Begin();
            runKey = k;
        }

        public void Dispose() {
            if (!Logs.ContainsKey(runKey)) return;
            Logs[runKey].End();
        }
    }

    private enum DisplayType {
        Ticks,
        Milliseconds
    }

    private class PerformanceLog {
        private readonly Stopwatch started = new();
        private readonly Stopwatch stopwatch = new();
        private readonly Stopwatch total = new();

        public long Last { get; private set; } = -1;
        public long Max { get; private set; } = -1;

        public long Average { get; private set; } = -1;

        public long Count { get; private set; }
        public double HitsPerSecond => started.ElapsedTicks == 0 ? 0 : Count / started.Elapsed.TotalSeconds;

        public double AveragePerSecond => HitsPerSecond * Average;

        public void Begin() {
            if (!started.IsRunning) started.Start();
            if (stopwatch.IsRunning) End();
            stopwatch.Restart();
            total.Start();
        }

        public void End() {
            if (!stopwatch.IsRunning) return;
            stopwatch.Stop();
            total.Stop();
            Last = stopwatch.Elapsed.Ticks;
            if (Last > Max) Max = Last;
            if (Count > 0) {
                Average -= Average / Count;
                Average += Last / Count;
            } else {
                Average = Last;
            }

            Count++;
        }

        public void Clear() {
            Average = -1;
            Count = 0;
            Last = -1;
            Max = -1;
            started.Reset();
            total.Reset();
        }
    }
}
