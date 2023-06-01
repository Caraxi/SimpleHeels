using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using Lumina.Data;
using Lumina.Data.Parsing;
using Lumina.Extensions;

namespace SimpleHeels.Files;

// Taken directly from
//      https://github.com/xivdev/Penumbra/blob/master/Penumbra.GameData/Files/MdlFile.cs
// and  https://github.com/xivdev/Penumbra/blob/master/Penumbra.GameData/Files/MdlFile.Write.cs

public partial class MdlFile
{
    public const uint NumVertices    = 17;
    public const uint FileHeaderSize = 0x44;

    // Refers to string, thus not Lumina struct.
    public struct Shape
    {
        public string   ShapeName = string.Empty;
        public ushort[] ShapeMeshStartIndex;
        public ushort[] ShapeMeshCount;

        public Shape(MdlStructs.ShapeStruct data, uint[] offsets, string[] strings)
        {
            var idx = offsets.AsSpan().IndexOf(data.StringOffset);
            ShapeName           = idx >= 0 ? strings[idx] : string.Empty;
            ShapeMeshStartIndex = data.ShapeMeshStartIndex;
            ShapeMeshCount      = data.ShapeMeshCount;
        }
    }

    // Raw data to write back.
    public uint   Version;
    public float  Radius;
    public float  ModelClipOutDistance;
    public float  ShadowClipOutDistance;
    public byte   BgChangeMaterialIndex;
    public byte   BgCrestChangeMaterialIndex;
    public ushort Unknown4;
    public byte   Unknown5;
    public byte   Unknown6;
    public ushort Unknown7;
    public ushort Unknown8;
    public ushort Unknown9;

    // Offsets are stored relative to RuntimeSize instead of file start.
    public uint[] VertexOffset;
    public uint[] IndexOffset;

    public uint[] VertexBufferSize;
    public uint[] IndexBufferSize;
    public byte   LodCount;
    public bool   EnableIndexBufferStreaming;
    public bool   EnableEdgeGeometry;


    public MdlStructs.ModelFlags1 Flags1;
    public MdlStructs.ModelFlags2 Flags2;

    public MdlStructs.BoundingBoxStruct BoundingBoxes;
    public MdlStructs.BoundingBoxStruct ModelBoundingBoxes;
    public MdlStructs.BoundingBoxStruct WaterBoundingBoxes;
    public MdlStructs.BoundingBoxStruct VerticalFogBoundingBoxes;

    public MdlStructs.VertexDeclarationStruct[]    VertexDeclarations;
    public MdlStructs.ElementIdStruct[]            ElementIds;
    public MdlStructs.MeshStruct[]                 Meshes;
    public MdlStructs.BoneTableStruct[]            BoneTables;
    public MdlStructs.BoundingBoxStruct[]          BoneBoundingBoxes;
    public MdlStructs.SubmeshStruct[]              SubMeshes;
    public MdlStructs.ShapeMeshStruct[]            ShapeMeshes;
    public MdlStructs.ShapeValueStruct[]           ShapeValues;
    public MdlStructs.TerrainShadowMeshStruct[]    TerrainShadowMeshes;
    public MdlStructs.TerrainShadowSubmeshStruct[] TerrainShadowSubMeshes;
    public MdlStructs.LodStruct[]                  Lods;
    public MdlStructs.ExtraLodStruct[]             ExtraLods;
    public ushort[]                                SubMeshBoneMap;

    // Strings are written in order
    public string[] Attributes;
    public string[] Bones;
    public string[] Materials;
    public Shape[]  Shapes;

    // Raw, unparsed data.
    public byte[] RemainingData;

    public bool Valid { get; }

    public MdlFile(byte[] data)
    {
        using var stream = new MemoryStream(data);
        using var r      = new LuminaBinaryReader(stream);

        var header = LoadModelFileHeader(r);
        LodCount         = header.LodCount;
        VertexBufferSize = header.VertexBufferSize;
        IndexBufferSize  = header.IndexBufferSize;
        VertexOffset     = header.VertexOffset;
        IndexOffset      = header.IndexOffset;
        for (var i = 0; i < 3; ++i)
        {
            if (VertexOffset[i] > 0)
                VertexOffset[i] -= header.RuntimeSize;

            if (IndexOffset[i] > 0)
                IndexOffset[i] -= header.RuntimeSize;
        }

        VertexDeclarations = new MdlStructs.VertexDeclarationStruct[header.VertexDeclarationCount];
        for (var i = 0; i < header.VertexDeclarationCount; ++i)
            VertexDeclarations[i] = MdlStructs.VertexDeclarationStruct.Read(r);

        var (offsets, strings) = LoadStrings(r);

        var modelHeader = LoadModelHeader(r);
        ElementIds = new MdlStructs.ElementIdStruct[modelHeader.ElementIdCount];
        for (var i = 0; i < modelHeader.ElementIdCount; i++)
            ElementIds[i] = MdlStructs.ElementIdStruct.Read(r);

        Lods = r.ReadStructuresAsArray<MdlStructs.LodStruct>(3);
        ExtraLods = modelHeader.ExtraLodEnabled
            ? r.ReadStructuresAsArray<MdlStructs.ExtraLodStruct>(3)
            : Array.Empty<MdlStructs.ExtraLodStruct>();

        Meshes = new MdlStructs.MeshStruct[modelHeader.MeshCount];
        for (var i = 0; i < modelHeader.MeshCount; i++)
            Meshes[i] = MdlStructs.MeshStruct.Read(r);

        Attributes = new string[modelHeader.AttributeCount];
        for (var i = 0; i < modelHeader.AttributeCount; ++i)
        {
            var offset    = r.ReadUInt32();
            var stringIdx = offsets.AsSpan().IndexOf(offset);
            Attributes[i] = stringIdx >= 0 ? strings[stringIdx] : string.Empty;
        }

        TerrainShadowMeshes    = r.ReadStructuresAsArray<MdlStructs.TerrainShadowMeshStruct>(modelHeader.TerrainShadowMeshCount);
        SubMeshes              = r.ReadStructuresAsArray<MdlStructs.SubmeshStruct>(modelHeader.SubmeshCount);
        TerrainShadowSubMeshes = r.ReadStructuresAsArray<MdlStructs.TerrainShadowSubmeshStruct>(modelHeader.TerrainShadowSubmeshCount);

        Materials = new string[modelHeader.MaterialCount];
        for (var i = 0; i < modelHeader.MaterialCount; ++i)
        {
            var offset    = r.ReadUInt32();
            var stringIdx = offsets.AsSpan().IndexOf(offset);
            Materials[i] = stringIdx >= 0 ? strings[stringIdx] : string.Empty;
        }

        Bones = new string[modelHeader.BoneCount];
        for (var i = 0; i < modelHeader.BoneCount; ++i)
        {
            var offset    = r.ReadUInt32();
            var stringIdx = offsets.AsSpan().IndexOf(offset);
            Bones[i] = stringIdx >= 0 ? strings[stringIdx] : string.Empty;
        }

        BoneTables = new MdlStructs.BoneTableStruct[modelHeader.BoneTableCount];
        for (var i = 0; i < modelHeader.BoneTableCount; i++)
            BoneTables[i] = MdlStructs.BoneTableStruct.Read(r);

        Shapes = new Shape[modelHeader.ShapeCount];
        for (var i = 0; i < modelHeader.ShapeCount; i++)
            Shapes[i] = new Shape(MdlStructs.ShapeStruct.Read(r), offsets, strings);

        ShapeMeshes = r.ReadStructuresAsArray<MdlStructs.ShapeMeshStruct>(modelHeader.ShapeMeshCount);
        ShapeValues = r.ReadStructuresAsArray<MdlStructs.ShapeValueStruct>(modelHeader.ShapeValueCount);

        var submeshBoneMapSize = r.ReadUInt32();
        SubMeshBoneMap = r.ReadStructures<ushort>((int)submeshBoneMapSize / 2).ToArray();

        var paddingAmount = r.ReadByte();
        r.Seek(r.BaseStream.Position + paddingAmount);

        // Dunno what this first one is for?
        BoundingBoxes            = MdlStructs.BoundingBoxStruct.Read(r);
        ModelBoundingBoxes       = MdlStructs.BoundingBoxStruct.Read(r);
        WaterBoundingBoxes       = MdlStructs.BoundingBoxStruct.Read(r);
        VerticalFogBoundingBoxes = MdlStructs.BoundingBoxStruct.Read(r);
        BoneBoundingBoxes        = new MdlStructs.BoundingBoxStruct[modelHeader.BoneCount];
        for (var i = 0; i < modelHeader.BoneCount; i++)
            BoneBoundingBoxes[i] = MdlStructs.BoundingBoxStruct.Read(r);

        var runtimePadding = header.RuntimeSize + FileHeaderSize + header.StackSize - r.BaseStream.Position;
        if (runtimePadding > 0)
            r.ReadBytes((int)runtimePadding);
        RemainingData = r.ReadBytes((int)(r.BaseStream.Length - r.BaseStream.Position));
        Valid         = true;
    }

    private MdlStructs.ModelFileHeader LoadModelFileHeader(LuminaBinaryReader r)
    {
        var header = MdlStructs.ModelFileHeader.Read(r);
        Version                    = header.Version;
        EnableIndexBufferStreaming = header.EnableIndexBufferStreaming;
        EnableEdgeGeometry         = header.EnableEdgeGeometry;
        return header;
    }

    private MdlStructs.ModelHeader LoadModelHeader(BinaryReader r)
    {
        var modelHeader = r.ReadStructure<MdlStructs.ModelHeader>();
        Radius = modelHeader.Radius;
        Flags1 = (MdlStructs.ModelFlags1)(modelHeader.GetType()
                .GetField("Flags1", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(modelHeader)
         ?? 0);
        Flags2 = (MdlStructs.ModelFlags2)(modelHeader.GetType()
                .GetField("Flags2", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(modelHeader)
         ?? 0);
        ModelClipOutDistance  = modelHeader.ModelClipOutDistance;
        ShadowClipOutDistance = modelHeader.ShadowClipOutDistance;
        Unknown4              = modelHeader.Unknown4;
        Unknown5 = (byte)(modelHeader.GetType()
                .GetField("Unknown5", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public)?.GetValue(modelHeader)
         ?? 0);
        Unknown6                   = modelHeader.Unknown6;
        Unknown7                   = modelHeader.Unknown7;
        Unknown8                   = modelHeader.Unknown8;
        Unknown9                   = modelHeader.Unknown9;
        BgChangeMaterialIndex      = modelHeader.BGChangeMaterialIndex;
        BgCrestChangeMaterialIndex = modelHeader.BGCrestChangeMaterialIndex;

        return modelHeader;
    }

    private static (uint[], string[]) LoadStrings(BinaryReader r)
    {
        var stringCount = r.ReadUInt16();
        r.ReadUInt16();
        var stringSize = (int)r.ReadUInt32();
        var stringData = r.ReadBytes(stringSize);
        var start      = 0;
        var strings    = new string[stringCount];
        var offsets    = new uint[stringCount];
        for (var i = 0; i < stringCount; ++i)
        {
            var span = stringData.AsSpan(start);
            var idx  = span.IndexOf((byte)'\0');
            strings[i] = Encoding.UTF8.GetString(span[..idx]);
            offsets[i] = (uint)start;
            start      = start + idx + 1;
        }

        return (offsets, strings);
    }

    public unsafe uint StackSize
        => (uint)(VertexDeclarations.Length * NumVertices * sizeof(MdlStructs.VertexElement));
    
    private static uint Write(BinaryWriter w, string s, long basePos)
    {
        var currentPos = w.BaseStream.Position;
        w.Write(Encoding.UTF8.GetBytes(s));
        w.Write((byte)0);
        return (uint)(currentPos - basePos);
    }

    private List<uint> WriteStrings(BinaryWriter w)
    {
        var startPos = (int)w.BaseStream.Position;
        var basePos  = startPos + 8;
        var count    = (ushort)(Attributes.Length + Bones.Length + Materials.Length + Shapes.Length);

        w.Write(count);
        w.Seek(basePos, SeekOrigin.Begin);
        var ret = Attributes.Concat(Bones)
            .Concat(Materials)
            .Concat(Shapes.Select(s => s.ShapeName))
            .Select(attribute => Write(w, attribute, basePos)).ToList();

        var padding = (w.BaseStream.Position & 0b111) > 0 ? (w.BaseStream.Position & ~0b111) + 8 : w.BaseStream.Position;
        for (var i = w.BaseStream.Position; i < padding; ++i)
            w.Write((byte)0);
        var size = (int)w.BaseStream.Position - basePos;
        w.Seek(startPos + 4, SeekOrigin.Begin);
        w.Write((uint)size);
        w.Seek(basePos + size, SeekOrigin.Begin);
        return ret;
    }

    private void WriteModelFileHeader(BinaryWriter w, uint runtimeSize)
    {
        w.Write(Version);
        w.Write(StackSize);
        w.Write(runtimeSize);
        w.Write((ushort)VertexDeclarations.Length);
        w.Write((ushort)Materials.Length);
        w.Write(VertexOffset[0] > 0 ? VertexOffset[0] + runtimeSize : 0u);
        w.Write(VertexOffset[1] > 0 ? VertexOffset[1] + runtimeSize : 0u);
        w.Write(VertexOffset[2] > 0 ? VertexOffset[2] + runtimeSize : 0u);
        w.Write(IndexOffset[0] > 0 ? IndexOffset[0] + runtimeSize : 0u);
        w.Write(IndexOffset[1] > 0 ? IndexOffset[1] + runtimeSize : 0u);
        w.Write(IndexOffset[2] > 0 ? IndexOffset[2] + runtimeSize : 0u);
        w.Write(VertexBufferSize[0]);
        w.Write(VertexBufferSize[1]);
        w.Write(VertexBufferSize[2]);
        w.Write(IndexBufferSize[0]);
        w.Write(IndexBufferSize[1]);
        w.Write(IndexBufferSize[2]);
        w.Write(LodCount);
        w.Write(EnableIndexBufferStreaming);
        w.Write(EnableEdgeGeometry);
        w.Write((byte)0); // Padding
    }

    private void WriteModelHeader(BinaryWriter w)
    {
        w.Write(Radius);
        w.Write((ushort)Meshes.Length);
        w.Write((ushort)Attributes.Length);
        w.Write((ushort)SubMeshes.Length);
        w.Write((ushort)Materials.Length);
        w.Write((ushort)Bones.Length);
        w.Write((ushort)BoneTables.Length);
        w.Write((ushort)Shapes.Length);
        w.Write((ushort)ShapeMeshes.Length);
        w.Write((ushort)ShapeValues.Length);
        w.Write(LodCount);
        w.Write((byte)Flags1);
        w.Write((ushort)ElementIds.Length);
        w.Write((byte)TerrainShadowMeshes.Length);
        w.Write((byte)Flags2);
        w.Write(ModelClipOutDistance);
        w.Write(ShadowClipOutDistance);
        w.Write(Unknown4);
        w.Write((ushort)TerrainShadowSubMeshes.Length);
        w.Write(Unknown5);
        w.Write(BgChangeMaterialIndex);
        w.Write(BgCrestChangeMaterialIndex);
        w.Write(Unknown6);
        w.Write(Unknown7);
        w.Write(Unknown8);
        w.Write(Unknown9);
        w.Write((uint)0); // 6 byte padding
        w.Write((ushort)0);
    }


    private static void Write(BinaryWriter w, in MdlStructs.VertexElement vertex)
    {
        w.Write(vertex.Stream);
        w.Write(vertex.Offset);
        w.Write(vertex.Type);
        w.Write(vertex.Usage);
        w.Write(vertex.UsageIndex);
        w.Write((ushort)0); // 3 byte padding
        w.Write((byte)0);
    }

    private static void Write(BinaryWriter w, in MdlStructs.VertexDeclarationStruct vertexDecl)
    {
        foreach (var vertex in vertexDecl.VertexElements)
            Write(w, vertex);

        Write(w, new MdlStructs.VertexElement() { Stream = 255 });
        w.Seek((int)(NumVertices - 1 - vertexDecl.VertexElements.Length) * 8, SeekOrigin.Current);
    }

    private static void Write(BinaryWriter w, in MdlStructs.ElementIdStruct elementId)
    {
        w.Write(elementId.ElementId);
        w.Write(elementId.ParentBoneName);
        w.Write(elementId.Translate[0]);
        w.Write(elementId.Translate[1]);
        w.Write(elementId.Translate[2]);
        w.Write(elementId.Rotate[0]);
        w.Write(elementId.Rotate[1]);
        w.Write(elementId.Rotate[2]);
    }

    private static unsafe void Write<T>(BinaryWriter w, in T data) where T : unmanaged
    {
        fixed (T* ptr = &data)
        {
            var bytePtr = (byte*)ptr;
            var size    = sizeof(T);
            var span    = new ReadOnlySpan<byte>(bytePtr, size);
            w.Write(span);
        }
    }

    private static void Write(BinaryWriter w, MdlStructs.MeshStruct mesh)
    {
        w.Write(mesh.VertexCount);
        w.Write((ushort)0); // padding
        w.Write(mesh.IndexCount);
        w.Write(mesh.MaterialIndex);
        w.Write(mesh.SubMeshIndex);
        w.Write(mesh.SubMeshCount);
        w.Write(mesh.BoneTableIndex);
        w.Write(mesh.StartIndex);
        w.Write(mesh.VertexBufferOffset[0]);
        w.Write(mesh.VertexBufferOffset[1]);
        w.Write(mesh.VertexBufferOffset[2]);
        w.Write(mesh.VertexBufferStride[0]);
        w.Write(mesh.VertexBufferStride[1]);
        w.Write(mesh.VertexBufferStride[2]);
        w.Write(mesh.VertexStreamCount);
    }

    private static void Write(BinaryWriter w, MdlStructs.BoneTableStruct bone)
    {
        foreach (var index in bone.BoneIndex)
            w.Write(index);

        w.Write(bone.BoneCount);
        w.Write((ushort)0); // 3 bytes padding
        w.Write((byte)0);
    }

    private void Write(BinaryWriter w, int shapeIdx, IReadOnlyList<uint> offsets)
    {
        var shape  = Shapes[shapeIdx];
        var offset = offsets[Attributes.Length + Bones.Length + Materials.Length + shapeIdx];
        w.Write(offset);
        w.Write(shape.ShapeMeshStartIndex[0]);
        w.Write(shape.ShapeMeshStartIndex[1]);
        w.Write(shape.ShapeMeshStartIndex[2]);
        w.Write(shape.ShapeMeshCount[0]);
        w.Write(shape.ShapeMeshCount[1]);
        w.Write(shape.ShapeMeshCount[2]);
    }

    private static void Write(BinaryWriter w, MdlStructs.BoundingBoxStruct box)
    {
        w.Write(box.Min[0]);
        w.Write(box.Min[1]);
        w.Write(box.Min[2]);
        w.Write(box.Min[3]);
        w.Write(box.Max[0]);
        w.Write(box.Max[1]);
        w.Write(box.Max[2]);
        w.Write(box.Max[3]);
    }

    public byte[] Write()
    {
        using var stream = new MemoryStream();
        using (var w = new BinaryWriter(stream))
        {
            // Skip and write this later when we actually know it.
            w.Seek((int)FileHeaderSize, SeekOrigin.Begin);

            foreach (var vertexDecl in VertexDeclarations)
                Write(w, vertexDecl);

            var offsets = WriteStrings(w);
            WriteModelHeader(w);

            foreach (var elementId in ElementIds)
                Write(w, elementId);

            foreach (var lod in Lods)
                Write(w, lod);

            if (Flags2.HasFlag(MdlStructs.ModelFlags2.ExtraLodEnabled))
                foreach (var extraLod in ExtraLods)
                    Write(w, extraLod);

            foreach (var mesh in Meshes)
                Write(w, mesh);

            for (var i = 0; i < Attributes.Length; ++i)
                w.Write(offsets[i]);

            foreach (var terrainShadowMesh in TerrainShadowMeshes)
                Write(w, terrainShadowMesh);

            foreach (var subMesh in SubMeshes)
                Write(w, subMesh);

            foreach (var terrainShadowSubMesh in TerrainShadowSubMeshes)
                Write(w, terrainShadowSubMesh);

            for (var i = 0; i < Materials.Length; ++i)
                w.Write(offsets[Attributes.Length + Bones.Length + i]);

            for (var i = 0; i < Bones.Length; ++i)
                w.Write(offsets[Attributes.Length + i]);

            foreach (var boneTable in BoneTables)
                Write(w, boneTable);

            for (var i = 0; i < Shapes.Length; ++i)
                Write(w, i, offsets);

            foreach (var shapeMesh in ShapeMeshes)
                Write(w, shapeMesh);

            foreach (var shapeValue in ShapeValues)
                Write(w, shapeValue);

            w.Write(SubMeshBoneMap.Length * 2);
            foreach (var bone in SubMeshBoneMap)
                w.Write(bone);

            var pos     = w.BaseStream.Position + 1;
            var padding = (byte) (pos & 0b111);
            if (padding > 0)
                padding = (byte) (8 - padding);
            w.Write(padding);
            for (var i = 0; i < padding; ++i)
                w.Write((byte) (0xDEADBEEFF00DCAFEu >> (8 * (7 - i))));

            Write(w, BoundingBoxes);
            Write(w, ModelBoundingBoxes);
            Write(w, WaterBoundingBoxes);
            Write(w, VerticalFogBoundingBoxes);
            foreach (var box in BoneBoundingBoxes)
                Write(w, box);

            var totalSize   = w.BaseStream.Position;
            var runtimeSize = (uint)(totalSize - StackSize - FileHeaderSize);
            w.Write(RemainingData);

            // Write header data.
            w.Seek(0, SeekOrigin.Begin);
            WriteModelFileHeader(w, runtimeSize);
        }

        return stream.ToArray();
    }
    
}
