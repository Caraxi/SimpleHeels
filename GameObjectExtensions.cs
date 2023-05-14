using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.Game.Object;
using FFXIVClientStructs.FFXIV.Client.Graphics.Scene;
using FFXIVClientStructs.FFXIV.Common.Math;

namespace SimpleHeels;

[StructLayout(LayoutKind.Explicit, Size = 0x1A0)]
public unsafe struct GameObjectExt {
    [FieldOffset(0x000)] public GameObject GameObject;
    [FieldOffset(0x0E0)] public Vector3 DrawOffset;
    
    public byte ObjectKind => GameObject.ObjectKind;
    public byte SubKind => GameObject.SubKind;
    public DrawObject* DrawObject => GameObject.GetDrawObject();
    public byte* GetName() => GameObject.GetName();

    public Vector3 Position => GameObject.Position;
    public float GetHeight() => GameObject.GetHeight();
}
