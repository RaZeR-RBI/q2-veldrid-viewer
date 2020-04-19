using System.IO;
using Common;
using FluentAssertions;
using MD2Viewer;
using Xunit;

namespace Tests
{
	public class MD2FileTests
	{
		[Fact]
		public void SimpleMD2Load()
		{
			Stream stream = null;
			try
			{
				stream = File.OpenRead("infantry.md2");
				var md2 = new MD2File(stream, SharedArrayPoolAllocator.Instance);

				md2.SkinWidth.Should().Be(276);
				md2.SkinHeight.Should().Be(194);
				md2.Skins.Length.Should().Be(2);
				md2.Skins.Data[0].Path.Should().Be("models/monsters/infantry/skin.pcx");
				md2.Skins.Data[1].Path.Should().Be("models/monsters/infantry/pain.pcx");
				md2.VertexCount.Should().Be(240);
				md2.Triangles.Length.Should().Be(460);
				md2.FrameCount.Should().Be(214);
				md2.Frames.Should().OnlyContain(f => f.Name != null);
			}
			finally
			{
				stream?.Dispose();
			}
		}
	}
}