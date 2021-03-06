using System;
using System.IO;
using Common;
using static System.Buffers.Binary.BinaryPrimitives;
using static Common.Util;

namespace Common
{
	public struct WALMipData : IDisposable
	{
		public int Length;
		public int Width;
		public int Height;
		public DisposableArray<byte> Pixels;

		public void Dispose() => Pixels.Dispose();
	}

	public struct WALTexture : IDisposable
	{
		public string Name;
		public int Width;
		public int Height;
		public string NextFrameName;
		public uint Flags, Contents, Value;
		public WALMipData[] Mips;
		public int MipCount;

		private IArrayAllocator _allocator;

		public WALTexture(string name, int width, int height, string nextFrameName, uint flags, uint contents, uint value, WALMipData[] mips, int mipCount, IArrayAllocator allocator)
		{
			Name = name;
			Width = width;
			Height = height;
			NextFrameName = nextFrameName;
			Flags = flags;
			Contents = contents;
			Value = value;
			Mips = mips;
			MipCount = mipCount;
			_allocator = allocator;
		}

		public void Dispose()
		{
			foreach (var mip in Mips.AsSpan().Slice(0, MipCount))
				mip.Dispose();
			_allocator.Return(Mips);
			Mips = null;
		}
	}

	public static class WALReader
	{
		public const int MipCount = 4;
		public static WALTexture ReadWAL(Stream stream, IArrayAllocator arrAlloc, IMemoryAllocator memAlloc)
		{
			// Note: only Quake 2 WALs are supported
			if (!stream.CanRead || !stream.CanSeek)
				throw new ArgumentException("Supplied stream must be seekable and readable", nameof(stream));
			Span<byte> headerBytes = stackalloc byte[100];
			EnsureRead(stream, headerBytes);
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


			var mips = arrAlloc.Rent<WALMipData>(MipCount);
			var expectedSize = width * height;
			for (var i = 0; i < MipCount; i++)
			{
				var bytes = new DisposableArray<byte>(expectedSize, memAlloc);
				EnsureRead(stream, bytes.AsSpan());

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

			return new WALTexture(
				name,
				width,
				height,
				animname,
				flags,
				contents,
				value,
				mips,
				MipCount,
				arrAlloc
			);
		}
	}
}