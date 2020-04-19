using System;
using System.IO;
using Common;
using static System.Buffers.Binary.BinaryPrimitives;
using static Common.Util;

namespace Common
{
	public struct WALMipData
	{
		public int Length;
		public int Width;
		public int Height;
		public byte[] Pixels;
	}

	public struct WALTexture
	{
		public string Name;
		public int Width;
		public int Height;
		public string NextFrameName;
		public uint Flags, Contents, Value;
		public WALMipData[] Mips;
		public int MipCount;

		public void DisposePixelData(IArrayAllocator allocator)
		{
			for (var i = 0; i < MipCount; i++)
				allocator.Return(Mips[i].Pixels);
			allocator.Return(Mips);
			Mips = null;
		}
	}

	public static class WALReader
	{
		public const int MipCount = 4;
		public static WALTexture ReadWAL(Stream stream, IArrayAllocator allocator)
		{
			// Note: only Quake 2 WALs are supported
			if (!stream.CanRead || !stream.CanSeek)
				throw new ArgumentException("Supplied stream must be seekable and readable", nameof(stream));
			Span<byte> headerBytes = stackalloc byte[100];
			if (stream.Read(headerBytes) < 100)
				throw new EndOfStreamException("Unexpected end of stream while reading header");
			var name = ReadNullTerminated(headerBytes.Slice(0, 32));
			var width = ReadInt32LittleEndian(headerBytes.Slice(32, 4));
			var height = ReadInt32LittleEndian(headerBytes.Slice(36, 4));

			Span<uint> offsets = stackalloc uint[MipCount];
			for (var i = 0; i < MipCount; i++)
				offsets[i] = ReadUInt32LittleEndian(headerBytes.Slice(40 + i * 4));

			var animname = ReadNullTerminated(headerBytes.Slice(56, 32));
			var flags = ReadUInt32LittleEndian(headerBytes.Slice(88));
			var contents = ReadUInt32LittleEndian(headerBytes.Slice(92));
			var value = ReadUInt32LittleEndian(headerBytes.Slice(96));


			var mips = allocator.Rent<WALMipData>(MipCount);
			var expectedSize = width * height;
			for (var i = 0; i < MipCount; i++)
			{
				var bytes = allocator.Rent<byte>(expectedSize);
				var ms = new MemoryStream(bytes);
				stream.Seek(offsets[i], SeekOrigin.Begin);
				ChunkedStreamRead(stream, ms, expectedSize);
				expectedSize /= 4;
				mips[i] = new WALMipData()
				{
					Length = expectedSize,
					Pixels = bytes,
					Width = Math.Max(1, width / (int)Math.Pow(2, i)),
					Height = Math.Max(1, height / (int)Math.Pow(2, i)),
				};
				if (expectedSize <= 1) expectedSize = 1;
			}

			return new WALTexture()
			{
				Name = name,
				Width = width,
				Height = height,
				NextFrameName = animname,
				Flags = flags,
				Contents = contents,
				Value = value,
				Mips = mips
			};
		}
	}
}