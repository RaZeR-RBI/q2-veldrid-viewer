using System;
using Xunit;
using Q2Viewer;
using System.IO;
using System.Linq;
using FluentAssertions;
using SharpFileSystem;
using System.Text;
using Common;

namespace Tests
{
	public class QPakFilesystemTests
	{
		private FileSystemPath Path(string s) => FileSystemPath.Parse(s);

		[Fact]
		public void TestFileList()
		{
			var pak = new QPakFS(new MemoryStream(TestPak));
			var expected = new string[] {
				"/hello.txt",
				"/progs/main.c"
			}.Select(Path).ToList();

			pak.GetEntities(FileSystemPath.Root).Should().BeEquivalentTo(expected);
			pak.GetEntities(Path("/hello.txt")).Should().BeEquivalentTo(expected.Take(1));
			pak.GetEntities(Path("/non-existing/")).Should().BeEmpty();
			pak.GetEntities(Path("/non.existing")).Should().BeEmpty();
			pak.GetEntities(Path("/progs/")).Should().BeEquivalentTo(expected.Skip(1).Take(1));
		}

		[Fact]
		public void TestReading()
		{
			var pak = new QPakFS(new MemoryStream(TestPak));
			var stream = pak.OpenFile(Path("/hello.txt"), FileAccess.Read);
			var reader = new StreamReader(stream, Encoding.ASCII);
			var actual = reader.ReadToEnd();
			actual.Should().Be("Hello, world!\x0a");
		}

		[Fact]
		public void TestReadingBigFile()
		{
			var sampleData = string.Join(',', Enumerable.Range(0, 5000).Select(i => i.ToString()));
			var size = sampleData.Length;

			var bytes = new byte[] {
				0x50, 0x41, 0x43, 0x4b, // PACK
				0x0C, 0x00, 0x00, 0x00, // file table offset: 12
				0x40, 0x00, 0x00, 0x00, // file table size: 64
				// file entry
				0x46, 0x49, 0x4c, 0x45, // FILE
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x00, 0x00, 0x00, 0x00,
				0x4C, 0x00, 0x00, 0x00, // offset: 76
 				(byte)(size & 0xFF), (byte)((size >> 8) & 0xFF), (byte)((size >> 16) & 0xFF), (byte)((size >> 24) & 0xFF), // size
			}.ToList();
			bytes.AddRange(Encoding.ASCII.GetBytes(sampleData));

			var pak = new QPakFS(new MemoryStream(bytes.ToArray()));
			pak.GetEntities(FileSystemPath.Root).Should().BeEquivalentTo(Path("/FILE"));
			var file = pak.OpenFile(Path("/FILE"), FileAccess.Read);
			var actual = new StreamReader(file, Encoding.ASCII).ReadToEnd();

			actual.Length.Should().Be(sampleData.Length);
			actual.Should().Be(sampleData);
		}

		private byte[] TestPak = {
			0x50, 0x41, 0x43, 0x4b, 0x40, 0x00, 0x00, 0x00, 0x80, 0x00, 0x00, 0x00,
			0x48, 0x65, 0x6c, 0x6c, 0x6f, 0x2c, 0x20, 0x77, 0x6f, 0x72, 0x6c, 0x64,
			0x21, 0x0a, 0x00, 0x00, 0x69, 0x6e, 0x74, 0x20, 0x6d, 0x61, 0x69, 0x6e,
			0x28, 0x76, 0x6f, 0x69, 0x64, 0x29, 0x0a, 0x7b, 0x0a, 0x20, 0x20, 0x20,
			0x20, 0x72, 0x65, 0x74, 0x75, 0x72, 0x6e, 0x20, 0x30, 0x3b, 0x0a, 0x7d,
			0x0a, 0x00, 0x00, 0x00, 0x68, 0x65, 0x6c, 0x6c, 0x6f, 0x2e, 0x74, 0x78,
			0x74, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			0x0c, 0x00, 0x00, 0x00, 0x0e, 0x00, 0x00, 0x00, 0x70, 0x72, 0x6f, 0x67,
			0x73, 0x2f, 0x6d, 0x61, 0x69, 0x6e, 0x2e, 0x63, 0x00, 0x00, 0x00, 0x00,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00,
			0x00, 0x00, 0x00, 0x00, 0x1c, 0x00, 0x00, 0x00, 0x21, 0x00, 0x00, 0x00
		};
	}
}
