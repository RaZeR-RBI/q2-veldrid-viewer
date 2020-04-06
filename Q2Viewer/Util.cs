using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Text;
using Veldrid;

namespace Q2Viewer
{
	public static class Util
	{
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

		public static Vector3 ReadVector3(ReadOnlySpan<byte> bytes)
		{
			return new Vector3(
				BitConverter.ToSingle(bytes.Slice(0)),
				BitConverter.ToSingle(bytes.Slice(8)),
				-BitConverter.ToSingle(bytes.Slice(4))
			);
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
	}
}