using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
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

		public TexturePool(GraphicsDevice gd, BSPFile file, IArrayAllocator allocator)
		{
			(_gd, _bsp, _allocator) = (gd, file, allocator);
			// TODO: Load textures
			CreateFallbackTexture();
		}

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

		private Texture CreateTexture(int width, int height, ColorRGBA[] pixels)
		{
			var rf = _gd.ResourceFactory;
			var texture = rf.CreateTexture(new TextureDescription(
				(uint)width, (uint)height, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Sampled, TextureType.Texture2D
			));
			var staging = rf.CreateTexture(new TextureDescription(
				(uint)width, (uint)height, 1, 1, 1, PixelFormat.R8_G8_B8_A8_UNorm, TextureUsage.Staging, TextureType.Texture2D
			));

			_gd.UpdateTexture<ColorRGBA>(staging, pixels, 0, 0, 0, (uint)width, (uint)height, 1, 0, 0);

			var cl = rf.CreateCommandList();
			cl.Begin();
			cl.CopyTexture(staging, texture);
			cl.End();
			_gd.SubmitCommands(cl);

			_allTextures.Add(texture);
			_allTextures.Add(staging);
			return texture;
		}

		private void CreateFallbackTexture()
		{
			var O = new ColorRGBA(100, 100, 100);
			var X = new ColorRGBA(150, 150, 150);
			var size = 256;
			var gridStep = 8;
			var pixels = new ColorRGBA[size * size];
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
		}
	}
}