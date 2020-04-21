using System;
using System.IO;
using Common;
using FluentAssertions;
using Xunit;

namespace Tests
{
	public class PCXReaderTests
	{
		[Fact]
		public void Paletted256PCXRead()
		{
			Stream stream = null;
			try
			{
				stream = File.OpenRead("skin.pcx");
				var pcx = PCXReader.ReadPCX(stream, SharedArrayPoolAllocator.Instance);
				pcx.Width.Should().Be(276);
				pcx.Height.Should().Be(194);
				pcx.Pixels[0].Should().Be(191);
				pcx.Pixels[pcx.Width * pcx.Height - 1].Should().Be(191);
				pcx.Pixels[55 * pcx.Width].Should().Be(232);
				// We check only first 16 colors because the palette in the file
				// is a bit different from the one specified in colormap.pcx
				// It gets replaced in the game anyway
				pcx.Palette.AsSpan().Slice(0, 16).ToArray().Should()
					.BeEquivalentTo(QuakePalette.Colors.AsSpan().Slice(0, 16).ToArray());
			}
			finally
			{
				stream?.Dispose();
			}
		}
	}
}