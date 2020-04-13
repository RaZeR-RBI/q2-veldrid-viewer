using System;
using System.Numerics;
using static System.Buffers.Binary.BinaryPrimitives;
using static Q2Viewer.Util;

namespace Q2Viewer
{
	public struct LRawValue : ILumpData
	{
		public int Size => -1;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			throw new InvalidOperationException();
		}
	}

	public struct LShortValue : ILumpData
	{
		public short Value;

		public int Size => 2;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			Value = ReadInt16LittleEndian(bytes);
		}
	}

	public struct LUShortValue : ILumpData
	{
		public ushort Value;

		public int Size => 2;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			Value = ReadUInt16LittleEndian(bytes);
		}
	}

	public struct LIntValue : ILumpData
	{
		public int Value;

		public int Size => 4;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			Value = ReadInt32LittleEndian(bytes);
		}
	}

	public struct LVertexPosition : ILumpData
	{
		public Vector3 Point;

		public int Size => 4 * 3;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			Point = ReadVector3(bytes);
		}
	}

	public struct LEdge : ILumpData
	{
		public ushort VertexID1;
		public ushort VertexID2;

		public int Size => 4;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			VertexID1 = ReadUInt16LittleEndian(bytes);
			VertexID2 = ReadUInt16LittleEndian(bytes.Slice(2));
		}
	}

	public enum PlaneType : int
	{
		X = 0,
		Y = 1,
		Z = 2,
		AnyX = 3,
		AnyY = 4,
		AnyZ = 5
	};

	public struct LPlane : ILumpData
	{
		public Vector3 Normal;
		public float Distance;
		public PlaneType Type;

		public int Size => (4 * 3) + 4 + 4;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			Normal = ReadVector3(bytes);
			Distance = BitConverter.ToSingle(bytes.Slice(12));
			Type = (PlaneType)ReadInt32LittleEndian(bytes.Slice(16));
		}
	}

	[Flags]
	public enum SurfaceFlags : int
	{
		Light = 1,
		Slick = 2,
		Sky = 4,
		Warp = 8,
		Transparent33 = 0x10,
		Transparent66 = 0x20,
		Flowing = 0x40,
		NoDraw = 0x80,
	}

	public struct LTextureInfo : ILumpData
	{
		public Vector4 S;
		public Vector4 T;
		public SurfaceFlags Flags;
		public int Value;
		public string TextureName; // up to 32 chars
		public int NextTextureInfoId;

		public int Size => (4 * 4) + (4 * 4) + 4 + 4 + 32 + 4;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			S = ReadVector4(bytes);
			T = ReadVector4(bytes.Slice(16));
			Flags = (SurfaceFlags)ReadInt32LittleEndian(bytes.Slice(32));
			Value = ReadInt32LittleEndian(bytes.Slice(36));
			TextureName = ReadNullTerminated(bytes.Slice(40, 32));
			NextTextureInfoId = ReadInt32LittleEndian(bytes.Slice(72));
		}
	}

	public struct LFace : ILumpData
	{
		public ushort PlaneId;
		public short Side;

		public int FirstEdgeId;

		public short EdgeCount;
		public short TextureInfoId;

		public byte LightmapStyle1;
		public byte LightmapStyle2;
		public byte LightmapStyle3;
		public byte LightmapStyle4;

		public int LightmapStyles =>
			(LightmapStyle1) +
			(LightmapStyle2 << 8) +
			(LightmapStyle3 << 16) +
			(LightmapStyle4 << 24);

		public int LightOffset;

		public int Size => 2 * 2 + 4 + 2 * 2 + 4 + 4;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			PlaneId = ReadUInt16LittleEndian(bytes);
			Side = ReadInt16LittleEndian(bytes.Slice(2));
			FirstEdgeId = ReadInt32LittleEndian(bytes.Slice(4));
			EdgeCount = ReadInt16LittleEndian(bytes.Slice(8));
			TextureInfoId = ReadInt16LittleEndian(bytes.Slice(10));
			LightmapStyle1 = bytes[12];
			LightmapStyle2 = bytes[13];
			LightmapStyle3 = bytes[14];
			LightmapStyle4 = bytes[15];
			LightOffset = ReadInt32LittleEndian(bytes.Slice(16));
		}
	}

	[Flags]
	public enum ContentFlags : int
	{
		Solid = 1,
		Window = 2,
		Aux = 4,
		Lava = 8,
		Slime = 16,
		Water = 32,
		Mist = 64,
		LastVisibleContents = 64,
		AreaPortal = 0x8000,
		PlayerClip = 0x10000,
		MonsterClip = 0x20000,
		Current0 = 0x40000,
		Current90 = 0x80000,
		Current180 = 0x100000,
		Current270 = 0x200000,
		CurrentUp = 0x400000,
		CurrentDown = 0x800000,
		Origin = 0x1000000,
		Monster = 0x2000000,
		DeadMonster = 0x4000000,
		Detail = 0x8000000,
		Translucent = 0x10000000,
		Ladder = 0x20000000,
	}

	public struct LLeaf : ILumpData
	{
		public ContentFlags Contents;

		public short Cluster;
		public short Area;

		public short MinX;
		public short MinY;
		public short MinZ;
		public short MaxX;
		public short MaxY;
		public short MaxZ;

		public ushort FirstLeafFace;
		public ushort NumLeafFaces;

		public ushort FirstLeafBrush;
		public ushort NumLeafBrushes;

		public int Size => 4 + 2 * 2 + 2 * 6 + 2 * 4;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			Contents = (ContentFlags)ReadInt32LittleEndian(bytes);
			Cluster = ReadInt16LittleEndian(bytes.Slice(4));
			Area = ReadInt16LittleEndian(bytes.Slice(6));

			MinX = ReadInt16LittleEndian(bytes.Slice(8));
			MinY = ReadInt16LittleEndian(bytes.Slice(10));
			MinZ = ReadInt16LittleEndian(bytes.Slice(12));
			MaxX = ReadInt16LittleEndian(bytes.Slice(14));
			MaxY = ReadInt16LittleEndian(bytes.Slice(16));
			MaxZ = ReadInt16LittleEndian(bytes.Slice(18));

			FirstLeafFace = ReadUInt16LittleEndian(bytes.Slice(20));
			NumLeafFaces = ReadUInt16LittleEndian(bytes.Slice(22));
			FirstLeafBrush = ReadUInt16LittleEndian(bytes.Slice(24));
			NumLeafBrushes = ReadUInt16LittleEndian(bytes.Slice(26));
		}

		public bool IsLiquid() =>
			Contents.HasFlag(ContentFlags.Water) ||
			Contents.HasFlag(ContentFlags.Lava) ||
			Contents.HasFlag(ContentFlags.Slime);
	}

	public struct LNode : ILumpData
	{
		public int PlaneId;
		public int Children1;
		public int Children2;

		public short MinX;
		public short MinY;
		public short MinZ;
		public short MaxX;
		public short MaxY;
		public short MaxZ;

		public ushort FirstFace;
		public ushort NumFaces;

		public int Size => 4 * 3 + 2 * 6 + 2 * 2;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			PlaneId = ReadInt32LittleEndian(bytes.Slice(0));
			Children1 = ReadInt32LittleEndian(bytes.Slice(4));
			Children2 = ReadInt32LittleEndian(bytes.Slice(8));

			MinX = ReadInt16LittleEndian(bytes.Slice(12));
			MinY = ReadInt16LittleEndian(bytes.Slice(14));
			MinZ = ReadInt16LittleEndian(bytes.Slice(16));
			MaxX = ReadInt16LittleEndian(bytes.Slice(18));
			MaxY = ReadInt16LittleEndian(bytes.Slice(20));
			MaxZ = ReadInt16LittleEndian(bytes.Slice(22));

			FirstFace = ReadUInt16LittleEndian(bytes.Slice(24));
			NumFaces = ReadUInt16LittleEndian(bytes.Slice(26));
		}
	}

	public struct LModel : ILumpData
	{
		public Vector3 Min;
		public Vector3 Max;
		public Vector3 Origin;
		public int HeadNode;
		public int FirstFace;
		public int NumFaces;

		public int Size => (4 * 3) * 3 + 4 + 4 + 4;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			Min = ReadVector3(bytes);
			Max = ReadVector3(bytes.Slice(12));
			Origin = ReadVector3(bytes.Slice(24));
			HeadNode = ReadInt32LittleEndian(bytes.Slice(36));
			FirstFace = ReadInt32LittleEndian(bytes.Slice(40));
			NumFaces = ReadInt32LittleEndian(bytes.Slice(44));
		}
	}

	public struct LBrush : ILumpData
	{
		public int FirstSide;
		public int NumSides;
		public int Contents;

		public int Size => 4 * 3;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			FirstSide = ReadInt32LittleEndian(bytes);
			NumSides = ReadInt32LittleEndian(bytes.Slice(4));
			Contents = ReadInt32LittleEndian(bytes.Slice(8));
		}
	}

	public struct LBrushSide : ILumpData
	{
		public ushort PlaneId;
		public short TexInfoId;

		public int Size => 2 * 2;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			PlaneId = ReadUInt16LittleEndian(bytes);
			TexInfoId = ReadInt16LittleEndian(bytes);
		}
	}

	public struct LArea : ILumpData
	{
		public int NumAreaPortals;
		public int FirstAreaPortal;

		public int Size => 4 * 2;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			NumAreaPortals = ReadInt32LittleEndian(bytes);
			FirstAreaPortal = ReadInt32LittleEndian(bytes.Slice(4));
		}
	}

	public struct LAreaPortal : ILumpData
	{
		public int PortalId;
		public int OtherArea;

		public int Size => 4 * 2;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			PortalId = ReadInt32LittleEndian(bytes);
			OtherArea = ReadInt32LittleEndian(bytes.Slice(4));
		}
	}
}