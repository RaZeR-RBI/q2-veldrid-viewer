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
		private readonly IMemoryAllocator _memAlloc;
		private readonly IArrayAllocator _arrAlloc;
		private readonly IFileSystem _fs;

		public TexturePool(GraphicsDevice gd, IFileSystem fs, IMemoryAllocator memAlloc, IArrayAllocator arrAlloc)
		{
			(_gd, _memAlloc, _arrAlloc, _fs) = (gd, memAlloc, arrAlloc, fs);
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
			using (var file = _fs.OpenFile(path, FileAccess.Read))
			using (var pcxTex = PCXReader.ReadPCX(file, _memAlloc))
			{
				var pixelCount = pcxTex.Width * pcxTex.Height;
				var palette = pcxTex.Palette;
				var pixels = new DisposableArray<ColorRGBA>(pixelCount, _memAlloc);
				var indexes = pcxTex.Pixels;
				for (var i = 0; i < pixelCount; i++)
					pixels[i] = palette[indexes[i]];
				var texture = CreateTexture(pcxTex.Width, pcxTex.Height, pixels.AsSpan(), path.ToString());
				pixels.Dispose();
				return texture;
			}
		}

		private Texture LoadWAL(FileSystemPath path)
		{
			using (var file = _fs.OpenFile(path, FileAccess.Read))
			using (var walTex = WALReader.ReadWAL(file, _arrAlloc, _memAlloc))
			{
				var pixelCount = walTex.Width * walTex.Height;
				var pixels = new DisposableArray<ColorRGBA>(pixelCount, _memAlloc);
				var indexes = walTex.Mips[0].Pixels;
				for (var i = 0; i < pixelCount; i++)
					pixels[i] = QuakePalette.Colors[indexes[i]];
				var texture = CreateTexture(walTex.Width, walTex.Height, pixels.AsSpan(), path.ToString());
				pixels.Dispose();
				return texture;
			}
		}

		private Texture CreateTexture(int width, int height, ReadOnlySpan<ColorRGBA> pixels, string name = null)
		{
			var texture = TexturePool.CreateTexture(_gd, width, height, pixels, name);
			_allTextures.Add(texture);
			return texture;
		}

		public static Texture CreateTexture(GraphicsDevice device, int width, int height, ReadOnlySpan<ColorRGBA> pixels, string name = null, uint layers = 1, TextureUsage usageFlags = 0)
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

		public static Texture CreateFallbackTexture(GraphicsDevice device, IMemoryAllocator memAlloc)
		{
			var O = new ColorRGBA(100, 100, 100);
			var X = new ColorRGBA(150, 150, 150);
			var size = 256;
			var gridStep = 8;
			var pixels = new DisposableArray<ColorRGBA>(size * size, memAlloc);
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

			var result = CreateTexture(device, size, size, pixels.AsSpan());
			pixels.Dispose();
			return result;
		}

		private void CreateFallbackTexture() =>
			_fallbackTex = CreateFallbackTexture(_gd, _memAlloc);

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

			var faces = _arrAlloc.Rent<PCXTexture>(6);
			for (var i = 0; i < 6; i++)
			{
				var file = _fs.OpenFile(path(name, s_envSuffixes[i]), FileAccess.Read);
				faces[i] = PCXReader.ReadPCX(file, _memAlloc);
				file.Close();
			}
			var widths = faces.Take(6).Select(f => f.Width).Distinct();
			var heights = faces.Take(6).Select(f => f.Height).Distinct();
			if (widths.Count() > 1 || heights.Count() > 1)
				throw new IOException("Sky textures should have identical width and height");
			if (widths.First() != heights.First())
				throw new IOException("Sky textures should be square");

			var texture = _gd.ResourceFactory.CreateTexture(new TextureDescription(
				(uint)faces[0].Width, (uint)faces[0].Height, 1, 1, 1,
				PixelFormat.R8_G8_B8_A8_UNorm,
				TextureUsage.Sampled | TextureUsage.Cubemap,
				TextureType.Texture2D
			));

			var pixelCount = (int)(texture.Width * texture.Height);
			var pixels = new DisposableArray<ColorRGBA>(pixelCount, _memAlloc);
			var size = (int)texture.Width;
			for (var f = 0; f < 6; f++)
			{
				var indexes = faces[f].Pixels;
				var palette = faces[f].Palette;
				for (var x = 0; x < size; x++)
					for (var y = 0; y < texture.Height; y++)
					{
						// up or down
						if (f == 2 || f == 3)
						{
							// flip x and y axes
							var src = y + x * size;
							var dst = x + y * size;
							pixels[dst] = palette[indexes[src]];
						}
						else
						{
							// flip horizontally
							var src = x + y * size;
							var dst = (size - x - 1) + (y * size);
							pixels[dst] = palette[indexes[src]];
						}
					}

				_gd.UpdateTexture<ColorRGBA>(texture, pixels.AsSpan(), 0, 0, 0, texture.Width, texture.Height, 1, 0, (uint)f);
			}
			pixels.Dispose();
			_arrAlloc.Return(faces);
			_allTextures.Add(texture);
			return texture;

		fallback:
			var color = new ColorRGBA(150, 150, 150);
			return CreateSingleColorTexture(_gd, 6, color, TextureUsage.Cubemap);
		}
	}
}