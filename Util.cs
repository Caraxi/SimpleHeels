using FFXIVClientStructs.FFXIV.Client.Game.Object;

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
}
