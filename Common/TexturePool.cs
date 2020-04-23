using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Numerics;
using Common;
using SharpFileSystem;
using Veldrid;

namespace Common
{

	public class TexturePool : IDisposable
	{
		private readonly Dictionary<string, Texture> _mapTextures
			= new Dictionary<string, Texture>();
		private Texture _fallbackTex;

		private readonly List<Texture> _allTextures = new List<Texture>();

		private readonly GraphicsDevice _gd;
		private readonly IArrayAllocator _allocator;
		private readonly IFileSystem _fs;

		public TexturePool(GraphicsDevice gd, IFileSystem fs, IArrayAllocator allocator)
		{
			(_gd, _allocator, _fs) = (gd, allocator, fs);
			CreateFallbackTexture();
		}

		public static Texture CreateSingleColorTexture(GraphicsDevice device, uint layers, ColorRGBA color, TextureUsage usageFlags = 0) =>
			TexturePool.CreateTexture(device, 1, 1, new ColorRGBA[] {
				color,
			}, $"_COLOR_RGBA_{color.R}_{color.G}_{color.B}_{color.A}", layers, usageFlags);

		private bool _isDisposed = false;
		public void Dispose()
		{
			if (_isDisposed) return;
			_isDisposed = true;
			foreach (var tex in _allTextures)
				tex.Dispose();
			_allTextures.Clear();
			_mapTextures.Clear();
		}

		public Texture GetTexture(string name) =>
			name == null ?
				_fallbackTex :
				_mapTextures.ContainsKey(name) ? _mapTextures[name] : _fallbackTex;

		public Texture LoadMapTexture(string name)
		{
			var wal = FileSystemPath.Parse($"/textures/{name}.wal");
			Texture texture = null;
			if (_fs.Exists(wal))
				texture = LoadWAL(wal);
			if (texture != null)
				_mapTextures.Add(name, texture);
			return texture;
		}

		public Texture LoadAbsolute(string path)
		{
			Texture texture = null;
			var f = FileSystemPath.Parse('/' + path.TrimStart('/'));
			if (_fs.Exists(f) && path.EndsWith(".pcx"))
				texture = LoadPCX(f);

			if (texture != null)
				_allTextures.Add(texture);
			return texture;
		}

		private Texture LoadPCX(FileSystemPath path)
		{
			var file = _fs.OpenFile(path, FileAccess.Read);
			var pcxTex = PCXReader.ReadPCX(file, _allocator);
			var pixelCount = pcxTex.Width * pcxTex.Height;
			var palette = pcxTex.Palette;
			var pixels = _allocator.Rent<ColorRGBA>(pixelCount);
			var indexes = pcxTex.Pixels;
			for (var i = 0; i < pixelCount; i++)
				pixels[i] = palette[indexes[i]];
			var texture = CreateTexture(pcxTex.Width, pcxTex.Height, pixels, path.ToString());
			_allocator.Return(pixels);
			pcxTex.DisposePixelData();
			file.Close();
			return texture;
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
			walTex.DisposePixelData();
			file.Close();
			return texture;
		}

		private Texture CreateTexture(int width, int height, ColorRGBA[] pixels, string name = null)
		{
			var texture = TexturePool.CreateTexture(_gd, width, height, pixels, name);
			_allTextures.Add(texture);
			return texture;
		}

		public static Texture CreateTexture(GraphicsDevice device, int width, int height, ColorRGBA[] pixels, string name = null, uint layers = 1, TextureUsage usageFlags = 0)
		{
			var genMipmaps = (width > 1 && height > 1);
			var usage = TextureUsage.Sampled;
			if (genMipmaps) usage |= TextureUsage.GenerateMipmaps;
			usage |= usageFlags;

			var rf = device.ResourceFactory;
			var texture = rf.CreateTexture(new TextureDescription(
				(uint)width, (uint)height, 1, genMipmaps ? 4u : 1u, layers, PixelFormat.R8_G8_B8_A8_UNorm, usage, TextureType.Texture2D
			));
			if (name != null) texture.Name = name;
			for (var layer = 0u; layer < layers; layer++)
				device.UpdateTexture<ColorRGBA>(texture, pixels, 0, 0, 0, (uint)width, (uint)height, 1, 0, layer);

			if (!genMipmaps) return texture;

			var cl = rf.CreateCommandList();
			cl.Begin();
			cl.GenerateMipmaps(texture);
			cl.End();
			device.SubmitCommands(cl);

			return texture;
		}

		public static Texture CreateFallbackTexture(GraphicsDevice device, IArrayAllocator _allocator)
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

			var result = CreateTexture(device, size, size, pixels);
			_allocator.Return(pixels);
			return result;
		}

		private void CreateFallbackTexture() =>
			_fallbackTex = CreateFallbackTexture(_gd, _allocator);

		private static string[] s_envSuffixes = new[] {
			"rt", "lf", "up", "dn", "ft", "bk"
		};
		public Texture LoadSky(string name)
		{
			Func<string, string, FileSystemPath> path = (n, s) => FileSystemPath.Parse($"/env/{n}{s}.pcx");
			if (name == null) goto fallback;

			foreach (var suffix in s_envSuffixes)
				if (!_fs.Exists(path(name, suffix)))
					goto fallback;

			var faces = _allocator.Rent<PCXTexture>(6);
			for (var i = 0; i < 6; i++)
			{
				var file = _fs.OpenFile(path(name, s_envSuffixes[i]), FileAccess.Read);
				faces[i] = PCXReader.ReadPCX(file, _allocator);
				file.Close();
			}
			// TODO: Check if all cubemap faces have same dimensions
			var texture = _gd.ResourceFactory.CreateTexture(new TextureDescription(
				(uint)faces[0].Width, (uint)faces[0].Height, 1, 1, 1,
				PixelFormat.R8_G8_B8_A8_UNorm,
				TextureUsage.Sampled | TextureUsage.Cubemap,
				TextureType.Texture2D
			));

			var pixelCount = texture.Width * texture.Height;
			var pixels = _allocator.Rent<ColorRGBA>((int)pixelCount);
			for (var f = 0; f < 6; f++)
			{
				var indexes = faces[f].Pixels;
				var palette = faces[f].Palette;
				for (var i = 0; i < pixelCount; i++)
					pixels[i] = palette[indexes[i]];
				_gd.UpdateTexture<ColorRGBA>(texture, pixels, 0, 0, 0, texture.Width, texture.Height, 1, 0, (uint)f);
			}
			_allocator.Return(pixels);
			_allocator.Return(faces);
			_allTextures.Add(texture);
			return texture;

		fallback:
			var color = new ColorRGBA(150, 150, 150);
			return CreateSingleColorTexture(_gd, 6, color, TextureUsage.Cubemap);
		}
	}
}