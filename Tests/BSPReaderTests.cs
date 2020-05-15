using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Common;
using FluentAssertions;
using Q2Viewer;
using Xunit;

namespace Tests
{
	public class BSPReaderTests
	{
		[Fact]
		public void ReaderTest()
		{
			Stream stream = null;
			try
			{
				stream = File.OpenRead("box.bsp");
				var bsp = new BSPFile(stream, DirectHeapMemoryAllocator.Instance);
				var reader = new BSPReader(bsp);
				var worldspawn = reader.GetModels()[0];
				var data = new List<(LFace face, Entry<VertexNTL>[] vertices)>();
				reader.ProcessVertices(
					worldspawn,
					(_, face, vertices, __, ___) =>
						data.Add((face, vertices.ToArray())));
				data.Count.Should().Be(16);

				var pair = data.First();
				var face = pair.face;
				var srcVerts = pair.vertices;
				var triangleCount = Geometry.GetTriangleCount<Entry<VertexNTL>>(srcVerts);
				Span<Entry<VertexNTL>> dstVerts = stackalloc Entry<VertexNTL>[triangleCount * 3];
				Geometry.Triangulate(srcVerts, dstVerts);
				Span<int> indices = stackalloc int[triangleCount * 3];
				for (var i = 0; i < dstVerts.Length; i++)
					indices[i] = dstVerts[i].Index;
				indices[0].Should().Be(1);
				indices[1].Should().Be(2);
				indices[2].Should().Be(3);
				indices[3].Should().Be(1);
				indices[4].Should().Be(3);
				indices[5].Should().Be(4);

				var leaves = reader.EnumerateLeafIndexes(worldspawn).ToList();
				leaves.Count.Should().BePositive();
				leaves.Should().OnlyHaveUniqueItems();
				leaves.Should().OnlyContain(i => i >= 0 && i < bsp.Leaves.Length);

				var brushIndexes = reader.EnumerateBrushIndexes(worldspawn).ToList();
				brushIndexes.Count.Should().BePositive();
				brushIndexes.Should().OnlyContain(i => i >= 0 && i < bsp.Brushes.Length);

				var brushes = reader.EnumerateBrushes(worldspawn).ToList();
				brushes.Count.Should().Be(6);
				brushes.Should().OnlyHaveUniqueItems();
			}
			finally
			{
				stream?.Dispose();
			}
		}
	}
}