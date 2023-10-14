using System.Runtime.InteropServices;

namespace SimpleHeels; 

// TODO: Use ClientStructs when updated.
[StructLayout(LayoutKind.Explicit, Size = 0x50)]
public struct ReaperShroud {
    [FieldOffset(0x30)] public byte ShroudFlags;
}
