using System.IO;
using Common;
using FluentAssertions;
using Q2Viewer;
using Xunit;

namespace Tests
{
	public class BSPFileTests
	{
		[Fact]
		public void SimpleBSPLoad()
		{
			Stream stream = null;
			try
			{
				stream = File.OpenRead("box.bsp");
				var bsp = new BSPFile(stream, DirectHeapMemoryAllocator.Instance);
				var expectedEntities = "{\n\"_tb_textures\" \"textures/e1u1\"\n\"classname\" \"worldspawn\"\n}\n{\n\"origin\" \"-0 -0 40\"\n\"classname\" \"info_player_start\"\n}\n{\n\"origin\" \"-40 -40 88\"\n\"classname\" \"light\"\n}\n\0";

				bsp.EntitiesString.Should().Be(expectedEntities);
				bsp.AreaPortals.Length.Should().Be(0);
				bsp.Areas.Length.Should().Be(2);
				bsp.Nodes.Length.Should().Be(27);
				bsp.Faces.Length.Should().Be(16);
				bsp.Vertexes.Length.Should().Be(19); // first seems to be special
				bsp.BrushSides.Length.Should().Be(36);
				bsp.Brushes.Length.Should().Be(6);
				bsp.Edges.Length.Should().Be(33); // first seems to be special
				bsp.Leaves.Length.Should().Be(29);
				bsp.LeafBrushes.Length.Should().Be(16);
				bsp.LeafFaces.Length.Should().Be(16);
				bsp.Lighting.Length.Should().Be(960); // in bytes
				bsp.Planes.Length.Should().Be(40);
				bsp.Submodels.Length.Should().Be(1); // worldspawn only
				bsp.SurfaceEdges.Length.Should().Be(64);
				bsp.TextureInfos.Length.Should().Be(6);
				bsp.Visibility.Length.Should().Be(44); // in bytes

				bsp.TextureInfos.Data.ToArray().Should().Contain(
					t => t.GetName() == "e1u1/floor3_1"
				);
				bsp.TextureInfos.Data.ToArray().Should().Contain(
					t => t.GetName() == "e1u1/c_met7_1"
				);
			}
			finally
			{
				stream?.Dispose();
			}
		}
	}
}