using System;
using System.Numerics;
using static System.Buffers.Binary.BinaryPrimitives;
using static Common.Util;

namespace Common
{
	public struct MD2Skin : ILumpData
	{
		public string Path; // max 64 chars

		public int Size => 64;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			Path = ReadNullTerminated(bytes.Slice(0, 64));
		}
	}

	public struct MD2TexCoord : ILumpData
	{
		public short S;
		public short T;

		public int Size => 2 * 2;

		public Vector2 AsVector2() => new Vector2(S, T);

		public void Read(ReadOnlySpan<byte> bytes)
		{
			S = ReadInt16LittleEndian(bytes);
			T = ReadInt16LittleEndian(bytes.Slice(2));
		}
	}

	public struct MD2Triangle : ILumpData
	{
		public ushort VertexID1;
		public ushort VertexID2;
		public ushort VertexID3;
		public ushort TexCoordID1;
		public ushort TexCoordID2;
		public ushort TexCoordID3;


		public int Size => 2 * 6;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			VertexID1 = ReadUInt16LittleEndian(bytes);
			VertexID2 = ReadUInt16LittleEndian(bytes.Slice(2));
			VertexID3 = ReadUInt16LittleEndian(bytes.Slice(4));
			TexCoordID1 = ReadUInt16LittleEndian(bytes.Slice(6));
			TexCoordID2 = ReadUInt16LittleEndian(bytes.Slice(8));
			TexCoordID3 = ReadUInt16LittleEndian(bytes.Slice(10));
		}
	}

	public struct MD2Vertex : ILumpData
	{
		public byte X;
		public byte Y;
		public byte Z;
		public byte NormalIndex;

		public int Size => 4;

		public Vector3 GetPosition() => new Vector3(X, Y, Z);

		public void Read(ReadOnlySpan<byte> bytes)
		{
			X = bytes[0];
			Y = bytes[2];
			Z = bytes[1];
			NormalIndex = bytes[3];
		}
	}

	public struct MD2Frame
	{
		public Vector3 Scale;
		public Vector3 Translate;
		public string Name; // max 16 chars
		public Memory<MD2Vertex> Vertices;
	}
}