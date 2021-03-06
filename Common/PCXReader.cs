using System;
using System.IO;
using static Common.Util;
using static System.Buffers.Binary.BinaryPrimitives;

namespace Common
{
	public struct PCXTexture : IDisposable
	{
		public int Width;
		public int Height;
		public DisposableArray<byte> Pixels;
		public DisposableArray<ColorRGBA> Palette;

		public PCXTexture(int width, int height, DisposableArray<byte> pixels, DisposableArray<ColorRGBA> palette)
		{
			Width = width;
			Height = height;
			Pixels = pixels;
			Palette = palette;
		}

		public void Dispose()
		{
			Pixels.Dispose();
			Palette.Dispose();
		}
	}

	public static class PCXReader
	{
		public static PCXTexture ReadPCX(Stream stream, IMemoryAllocator allocator)
		{
			Span<byte> header = stackalloc byte[128];
			EnsureRead(stream, header);
			if (header[0] != 0x0a)
				throw new IOException("Not a valid PCX file");
			if (header[1] != 5)
				throw new IOException("Only PCX version 5 is supported");
			var rle = header[2] > 0;
			var bpp = header[3];
			if (bpp != 8)
				throw new IOException("Only 256-color paletted PCX images are supported");

			var minX = ReadUInt16LittleEndian(header.Slice(4));
			var minY = ReadUInt16LittleEndian(header.Slice(6));
			var maxX = ReadUInt16LittleEndian(header.Slice(8));
			var maxY = ReadUInt16LittleEndian(header.Slice(10));

			var width = maxX - minX + 1;
			var height = maxY - minY + 1;

			if (header[65] != 1)
				throw new IOException("Only 256-color paletted PCX images are supported");
			// if (header[68] != 2)
			// throw new IOException("Only RGB palette is supported");

			var total = width * height;
			var pixels = new DisposableArray<byte>(total, allocator);
			if (!rle)
				EnsureRead(stream, pixels);
			else
			{
				var index = 0;
				var runLength = 0;
				while (index < total)
				{
					var dataByte = EnsureReadByte(stream);
					if ((dataByte & 0xC0) == 0xC0)
					{
						runLength = dataByte & 0x3F;
						dataByte = EnsureReadByte(stream);
					}
					else runLength = 1;

					while (runLength-- > 0)
					{
						if (index < total)
							pixels[index++] = dataByte;
						else
							break;
					}
				}
			}

			var fileLength = stream.Length;

			var palette = new DisposableArray<ColorRGBA>(256, allocator);
			Span<byte> paletteBytes = stackalloc byte[768];

			stream.Seek(-paletteBytes.Length - 1, SeekOrigin.End);
			if (stream.ReadByte() != 0x0C)
				throw new IOException("PCX file contains no palette");
			EnsureRead(stream, paletteBytes);
			for (var i = 0; i < palette.Length; i++)
				palette[i] = new ColorRGBA(
					paletteBytes[i * 3],
					paletteBytes[i * 3 + 1],
					paletteBytes[i * 3 + 2]
				);


			return new PCXTexture(width, height, pixels, palette);
		}
	}
}