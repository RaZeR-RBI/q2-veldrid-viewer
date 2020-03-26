using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using FluentAssertions;
using Q2Viewer;
using Xunit;

namespace Tests
{
	public class BSPReaderTests
	{
		[Fact]
		public void TriangulationTest()
		{
			Stream stream = null;
			try
			{
				stream = File.OpenRead("box.bsp");
				var bsp = new BSPFile(stream, SharedArrayPoolAllocator.Instance);
				var reader = new BSPReader(bsp);
				var worldspawn = reader.GetModels().First();
				var data = new List<(LFace face, Entry<VertexTL>[] vertices)>();
				reader.ProcessVertices(
					worldspawn,
					(face, vertices) =>
						data.Add((face, vertices.ToArray())));
				data.Count.Should().Be(16);

				var pair = data.First();
				var face = pair.face;
				var srcVerts = pair.vertices;
				var triangleCount = BSPReader.GetFaceTriangleCount(srcVerts);
				Span<Entry<VertexTL>> dstVerts = stackalloc Entry<VertexTL>[triangleCount * 3];
				BSPReader.Triangulate(srcVerts, dstVerts);
				Span<int> indices = stackalloc int[triangleCount * 3];
				for (var i = 0; i < dstVerts.Length; i++)
					indices[i] = dstVerts[i].Index;
				indices[0].Should().Be(1);
				indices[1].Should().Be(2);
				indices[2].Should().Be(3);
				indices[3].Should().Be(1);
				indices[4].Should().Be(3);
				indices[5].Should().Be(4);
			}
			finally
			{
				stream?.Dispose();
			}
		}
	}
}