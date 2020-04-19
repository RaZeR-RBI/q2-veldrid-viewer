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

				md2.Skins.Length.Should().Be(2);
				md2.Vertexes.Length.Should().Be(240);
				md2.Triangles.Length.Should().Be(460);
			}
			finally
			{
				stream?.Dispose();
			}
		}
	}
}