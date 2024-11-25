using System;
using System.Collections.Generic;
using System.Numerics;
using Dalamud.Plugin.Ipc;
using Newtonsoft.Json;

namespace SimpleHeels;

public static class IPC {
    public static class CustomizePlus {
        private static readonly ICallGateSubscriber<(int Breaking, int Feature)> GetApiVersionSubscriber;
        private static readonly ICallGateSubscriber<bool> IsValidSubscriber;
        private static readonly ICallGateSubscriber<ushort, (int errorCode, Guid? result)> GetActiveProfileIdOnCharacterSubscriber;
        private static readonly ICallGateSubscriber<Guid, (int errorCode, string? result)> GetProfileByUniqueIdSubscriber;

        public class Profile {
            public class BoneConfig {
                public Vector3 Translation;
                public Vector3 Rotation;
                public Vector3 Scaling;

                public bool PropagateTranslation;
                public bool PropagateRotation;
                public bool PropagateScale;
            }

            public Dictionary<string, BoneConfig> Bones = new();
        }

        static CustomizePlus() {
            GetApiVersionSubscriber = PluginService.PluginInterface.GetIpcSubscriber<(int Breaking, int Feature)>("CustomizePlus.General.GetApiVersion");
            IsValidSubscriber = PluginService.PluginInterface.GetIpcSubscriber<bool>("CustomizePlus.General.IsValid");
            GetActiveProfileIdOnCharacterSubscriber = PluginService.PluginInterface.GetIpcSubscriber<ushort, (int errorCode, Guid? result)>("CustomizePlus.Profile.GetActiveProfileIdOnCharacter");
            GetProfileByUniqueIdSubscriber = PluginService.PluginInterface.GetIpcSubscriber<Guid, (int errorCode, string? result)>("CustomizePlus.Profile.GetByUniqueId");
        }

        public static bool GetApiVersion(out int breaking, out int feature) {
            try {
                (breaking, feature) = GetApiVersionSubscriber.InvokeFunc();
                return true;
            } catch {
                breaking = 0;
                feature = 0;
                return false;
            }
        }

        public static bool IsValid() {
            try {
                if (!GetApiVersion(out var breaking, out _) || breaking != 6) return false;
                return IsValidSubscriber.InvokeFunc();
            } catch {
                return false;
            }
        }

        public static Guid? GetActiveProfileIdOnCharacter(ushort gameObjectIndex) {
            try {
                var r = GetActiveProfileIdOnCharacterSubscriber.InvokeFunc(gameObjectIndex);
                return r.errorCode == 0 ? r.result : null;
            } catch {
                return null;
            }
        }

        public static string? GetProfileByUniqueId(Guid guid) {
            try {
                var r = GetProfileByUniqueIdSubscriber.InvokeFunc(guid);
                return r.errorCode == 0 ? r.result : null;
            } catch {
                return null;
            }
        }

        public static Profile? GetProfileOnCharacter(ushort gameObjectIndex) {
            using var _ = PerformanceMonitors.Run("IPC.GetCustomizePlusProfile");
            try {
                if (!IsValid()) return null;
                var profileId = GetActiveProfileIdOnCharacter(gameObjectIndex);
                return profileId == null ? null : JsonConvert.DeserializeObject<Profile>(GetProfileByUniqueId(profileId.Value) ?? "{}");
            } catch {
                return null;
            }
        }
    }
}
