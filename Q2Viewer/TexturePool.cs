using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Linq;
using SharpFileSystem;
using Veldrid;

namespace Q2Viewer
{
	public struct ColorRGBA
	{
		public byte R;
		public byte G;
		public byte B;
		public byte A;

		public ColorRGBA(byte r, byte g, byte b) =>
			(R, G, B, A) = (r, g, b, 255);
	}

	public class TexturePool : IDisposable
	{
		private readonly Dictionary<string, Texture> _textures
			= new Dictionary<string, Texture>();
		private Texture _fallbackTex;

		private readonly List<Texture> _allTextures = new List<Texture>();

		private readonly GraphicsDevice _gd;
		private readonly BSPFile _bsp;
		private readonly IArrayAllocator _allocator;
		private readonly IFileSystem _fs;

		public TexturePool(GraphicsDevice gd, BSPFile file, IFileSystem fs, IArrayAllocator allocator)
		{
			(_gd, _bsp, _allocator, _fs) = (gd, file, allocator, fs);
			var textureNames = file.TextureInfos.Data
				.Where(t => t.TextureName != null)
				.Select(t => t.TextureName?.ToLowerInvariant())
				.Distinct();

			foreach (var name in textureNames)
			{
				var texture = LoadTexture(name);
				if (texture != null)
					_textures.Add(name, texture);
			}

			CreateFallbackTexture();
		}

		public static Texture CreateWhiteTexture(GraphicsDevice device) =>
			TexturePool.CreateTexture(device, 2, 2, new ColorRGBA[] {
				new ColorRGBA(255, 255, 255),
				new ColorRGBA(255, 255, 255),
				new ColorRGBA(255, 255, 255),
				new ColorRGBA(255, 255, 255),
			}, "_FALLBACK_WHITE");

		private bool _isDisposed = false;
		public void Dispose()
		{
			if (_isDisposed) return;
			_isDisposed = true;
			foreach (var tex in _allTextures)
				tex.Dispose();
			_allTextures.Clear();
			_textures.Clear();
		}

		public Texture GetTexture(string name) =>
			_textures.ContainsKey(name) ? _textures[name] : _fallbackTex;

		private Texture LoadTexture(string name)
		{
			var wal = FileSystemPath.Parse($"/textures/{name}.wal");
			if (_fs.Exists(wal))
				return LoadWAL(wal);
			return null;
		}

		private Texture LoadWAL(FileSystemPath path)
		{
			var file = _fs.OpenFile(path, FileAccess.Read);
			var walTex = WALReader.ReadWAL(file, _allocator);
			var pixelCount = walTex.Width * walTex.Height;
			var pixels = _allocator.Rent<ColorRGBA>(pixelCount);
			var indexes = walTex.Mips[0].Pixels;
			for (var i = 0; i < pixelCount; i++)
				pixels[i] = QuakePalette.Colors[indexes[i]];
			var texture = CreateTexture(walTex.Width, walTex.Height, pixels, path.ToString());
			_allocator.Return(pixels);
			return texture;
		}

		private Texture CreateTexture(int width, int height, ColorRGBA[] pixels, string name = null)
		{
			var texture = TexturePool.CreateTexture(_gd, width, height, pixels, name);
			_allTextures.Add(texture);
			return texture;
		}

		public static Texture CreateTexture(GraphicsDevice device, int width, int height, ColorRGBA[] pixels, string name = null)
		{
			var genMipmaps = (width > 1 && height > 1);
			var usage = TextureUsage.Sampled;
			if (genMipmaps) usage |= TextureUsage.GenerateMipmaps;

			var rf = device.ResourceFactory;
			var texture = rf.CreateTexture(new TextureDescription(
				(uint)width, (uint)height, 1, 4, 1, PixelFormat.R8_G8_B8_A8_UNorm, usage, TextureType.Texture2D
			));
			if (name != null) texture.Name = name;
			device.UpdateTexture<ColorRGBA>(texture, pixels, 0, 0, 0, (uint)width, (uint)height, 1, 0, 0);

			if (!genMipmaps) return texture;

			var cl = rf.CreateCommandList();
			cl.Begin();
			cl.GenerateMipmaps(texture);
			cl.End();
			device.SubmitCommands(cl);

			return texture;
		}

		private void CreateFallbackTexture()
		{
			var O = new ColorRGBA(100, 100, 100);
			var X = new ColorRGBA(150, 150, 150);
			var size = 256;
			var gridStep = 8;
			var pixels = _allocator.Rent<ColorRGBA>(size * size);
			for (var x = 0; x < size; x++)
				for (var y = 0; y < size; y++)
				{
					var index = x + size * y;
					var gridX = x / gridStep;
					var gridY = y / gridStep;
					var color = O;
					if (gridX % 2 == gridY % 2)
						color = X;
					pixels[index] = color;
				}

			_fallbackTex = CreateTexture(size, size, pixels);
			_allocator.Return(pixels);
		}
	}
}