using System;
using System.Numerics;
using System.Runtime.InteropServices;
using static System.Buffers.Binary.BinaryPrimitives;
using static Common.Util;

namespace Common
{
	public unsafe struct MD2Skin : ILumpData
	{
		private const int c_pathSize = 64;

		fixed byte _path[c_pathSize];

		public string GetPath()
		{
			fixed (byte* ptr = _path)
			{
				return ReadNullTerminated(new Span<byte>(ptr, c_pathSize));
			}
		}

		public int Size => 64;

		public void Read(ReadOnlySpan<byte> bytes)
		{
			fixed (byte* ptr = _path)
			{
				var target = new Span<byte>(ptr, c_pathSize);
				bytes.Slice(0, c_pathSize).CopyTo(target);
			}
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

	public unsafe struct MD2Frame
	{
		private const int c_maxChars = 16;
		public Vector3 Scale;
		public Vector3 Translate;

		private fixed byte _name[c_maxChars];

		public string GetName()
		{
			fixed (byte* ptr = _name)
			{
				return ReadNullTerminated(new Span<byte>(ptr, c_maxChars));
			}
		}

		public void SetName(Span<byte> chars)
		{
			fixed (byte* ptr = _name)
			{
				chars.Slice(0, c_maxChars).CopyTo(new Span<byte>(ptr, c_maxChars));
			}
		}

		public int StartVertex;
	}
}