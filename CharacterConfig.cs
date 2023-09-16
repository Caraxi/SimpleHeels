using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;

namespace SimpleHeels; 

public class CharacterConfig {
    public List<HeelConfig> HeelsConfig = new();

    public float SittingOffsetZ = 0f;
    public float SittingOffsetY = 0f;

    public float GroundSitOffset = 0f;
    public float SleepOffset = 0f;


    public unsafe HeelConfig? GetFirstMatch(Human* human) {
        string? feetModelPath = null;
        string? topModelPath = null;
        string? legsModelPath = null;

        return HeelsConfig.Select((hc, index) => (hc, index)).OrderBy(a => a.hc.Slot).ThenBy(a => a.index).Select(a => a.hc).FirstOrDefault(hc => {
            if (!hc.Enabled) return false;
            switch (hc.Slot) {
                case ModelSlot.Feet:
                    feetModelPath ??= Plugin.GetModelPath(human, ModelSlot.Feet);
                    return (hc.PathMode == false && hc.ModelId == human->Feet.Id) || (hc.PathMode && feetModelPath != null && feetModelPath.Equals(hc.Path));
                case ModelSlot.Top:
                    topModelPath ??= Plugin.GetModelPath(human, ModelSlot.Top);
                    return (hc.PathMode == false && hc.ModelId == human->Top.Id) || (hc.PathMode && topModelPath != null && topModelPath.Equals(hc.Path));
                case ModelSlot.Legs:
                    legsModelPath ??= Plugin.GetModelPath(human, ModelSlot.Legs);
                    return (hc.PathMode == false && hc.ModelId == human->Legs.Id) || (hc.PathMode && legsModelPath != null && legsModelPath.Equals(hc.Path));
                default:
                    return false;
            }
        });
    }

    public List<HeelConfig> GetDuplicates(HeelConfig hc, bool enabledOnly = false) {
        var l = new List<HeelConfig>();
        foreach (var h in HeelsConfig) {
            if (enabledOnly && h.Enabled == false) continue;
            if (h.PathMode != hc.PathMode) continue;
            if (h.Slot != hc.Slot) continue;

            if (h.PathMode) {
                if (h.Path != hc.Path) continue;
            } else {
                if (h.ModelId != hc.ModelId) continue;
            }
            l.Add(h);
        }
        return l;
    }
}
