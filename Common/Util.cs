using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Runtime.CompilerServices;
using System.Text;
using Veldrid;

namespace Common
{
	public static class Util
	{
		// TODO [Optimize] Find a faster way to do frustum culling
		[MethodImpl(MethodImplOptions.AggressiveInlining)]
		public static bool CheckIfOutside(Matrix4x4 clipMatrix, AABB worldAABB)
		{
			// check if any vertex is inside the viewing frustum
			Span<Vector4> vertices = stackalloc Vector4[8];
			worldAABB.GetVertices(ref vertices);

			var allToLeft = true;
			var allToRight = true;
			var allAbove = true;
			var allBelow = true;
			// TODO [Optimize] Check if this loop is being unrolled
			for (var i = 0; i < 8; i++)
			{
				var pos = Vector4.Transform(vertices[i], clipMatrix);
				if (allToLeft && pos.X > -pos.W)
					allToLeft = false;
				if (allToRight && pos.X < pos.W)
					allToRight = false;
				if (allAbove && pos.Y < pos.W)
					allAbove = false;
				if (allBelow && pos.Y > -pos.W)
					allBelow = false;
			}
			return allToLeft || allToRight || allAbove || allBelow;
		}

		public static string ReadNullTerminated(ReadOnlySpan<byte> bytes, Encoding encoding = null)
		{
			if (encoding == null) encoding = Encoding.ASCII;
			var length = bytes.Length;
			for (int i = 0; i < length; i++)
				if (bytes[i] == 0)
				{
					length = i;
					break;
				}
			if (length == 0) return null;
			return encoding.GetString(bytes.Slice(0, length));
		}

		public static Vector3 ReadVector3XZY(ReadOnlySpan<byte> bytes)
		{
			return new Vector3(
				BitConverter.ToSingle(bytes.Slice(0)),
				BitConverter.ToSingle(bytes.Slice(8)),
				-BitConverter.ToSingle(bytes.Slice(4))
			);
		}

		public static Vector3 ReadVector3XZY(MemoryStream ms)
		{
			Span<byte> bytes = stackalloc byte[4 * 3];
			EnsureRead(ms, bytes);
			return ReadVector3XZY(bytes);
		}

		public static Vector4 ReadVector4(ReadOnlySpan<byte> bytes)
		{
			return new Vector4(
				BitConverter.ToSingle(bytes.Slice(0)),
				BitConverter.ToSingle(bytes.Slice(8)),
				-BitConverter.ToSingle(bytes.Slice(4)),
				BitConverter.ToSingle(bytes.Slice(12))
			);
		}

		public static void ChunkedStreamRead(Stream src, MemoryStream dst, int length, int chunkSize = 4096)
		{
			Span<byte> buffer = stackalloc byte[chunkSize];
			var curOffset = 0;
			while (curOffset < length)
			{
				var bytesRead = src.Read(buffer);
				if (length - curOffset < chunkSize)
				{
					dst.Write(buffer.Slice(0, (int)(length - curOffset)));
					break;
				}
				else
				{
					if (bytesRead != chunkSize)
						throw new EndOfStreamException("Unexpected end of stream");
					dst.Write(buffer);
				}
				curOffset += chunkSize;
			}
		}

		public static void EnsureRead(Stream stream, Span<byte> target)
		{
			if (stream.Read(target) != target.Length)
				throw new EndOfStreamException("Unexpected end of stream");
		}

		public static byte EnsureReadByte(Stream stream)
		{
			var result = stream.ReadByte();
			if (result < 0)
				throw new EndOfStreamException("Unexpected end of stream");
			return (byte)result;
		}

		public static RgbaFloat ToRgbaFloat(this Color color) =>
			new RgbaFloat(
				(float)color.R / 255f,
				(float)color.G / 255f,
				(float)color.B / 255f,
				(float)color.A / 255f
			);

		private static readonly List<RgbaFloat> s_colors = null;
		private static readonly Random s_rnd = new Random();
		static Util()
		{
			s_colors = ((KnownColor[])Enum.GetValues(typeof(KnownColor)))
				.Select(Color.FromKnownColor)
				.Select(ToRgbaFloat)
				.ToList();
		}

		public static RgbaFloat GetRandomColor() =>
			s_colors[s_rnd.Next(0, s_colors.Count)];

		public static string FormatSW(Stopwatch sw) =>
			sw.ElapsedMilliseconds > 0 ? $"{sw.ElapsedMilliseconds} ms" : "<1 ms";

		public static int ClosestPowerOf2(int value)
		{
			var result = 1;
			while (result < value) result *= 2;
			return result;
		}
	}
}