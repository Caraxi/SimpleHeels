using System.Runtime.InteropServices;
using FFXIVClientStructs.FFXIV.Client.System.Resource.Handle;
using FFXIVClientStructs.Interop;
using FFXIVClientStructs.STD;

namespace SimpleHeels; 

// TODO: Use FFXIVClientStructs directly once updated

[StructLayout(LayoutKind.Explicit, Size = 0x260)]
public struct ModelResourceHandle {
    [FieldOffset(0x00)] public ResourceHandle ResourceHandle;
    [FieldOffset(0x208)] public StdMap<Pointer<byte>, short> Attributes;
}

[StructLayout(LayoutKind.Explicit, Size = 0xF0)]
public unsafe struct Model {
    [FieldOffset(0x30)] public ModelResourceHandle* ModelResourceHandle;
}
