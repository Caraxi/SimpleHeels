using System;
using System.Collections.Generic;
using System.Linq;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using Lumina.Excel.GeneratedSheets2;

namespace SimpleHeels;

public static unsafe class Utils {
    public static GameObject* GetGameObjectById(uint objectId) {
        for (var i = 0; i < Constants.ObjectLimit; i++) {
            var o = GameObjectManager.GetGameObjectByIndex(i);
            if (o == null || o->ObjectID != objectId) continue;
            return o;
        }

        return null;
    }
    
    public static Lazy<HashSet<uint>> StaticMinions = new(() => {
        try {
            return PluginService.Data.GetExcelSheet<Companion>()?.Where(c => c.Behavior.Row == 3).Select(c => c.RowId)?.ToHashSet() ?? new HashSet<uint>();
        } catch {
            return new HashSet<uint>();
        }
    });

    public static bool IsPlayerWorld(this World world) {
        if (world.Name.RawData.IsEmpty) return false;
        if (world.DataCenter.Row == 0) return false;
        if (world.IsPublic) return true;
        return char.IsUpper((char)world.Name.RawData[0]);
    }
}
