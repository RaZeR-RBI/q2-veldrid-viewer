using System.IO;
using Common;
using FluentAssertions;
using Q2Viewer;
using Xunit;

namespace Tests
{
	public class WALReaderTests
	{
		[Fact]
		public void TestWALReaderOnClipWal()
		{
			Stream stream = null;
			try
			{
				stream = File.OpenRead("clip.wal");
				var wal = WALReader.ReadWAL(stream, SharedArrayPoolAllocator.Instance, DirectHeapMemoryAllocator.Instance);

				wal.Width.Should().Be(32);
				wal.Height.Should().Be(32);
				wal.Flags.Should().Be(0x80);
				wal.Contents.Should().Be(0x30000);
				wal.Value.Should().Be(0);

				wal.Mips[0].Width.Should().Be(32);
				wal.Mips[0].Height.Should().Be(32);
				wal.Mips[1].Width.Should().Be(16);
				wal.Mips[1].Height.Should().Be(16);
				wal.Mips[2].Width.Should().Be(8);
				wal.Mips[2].Height.Should().Be(8);
				wal.Mips[3].Width.Should().Be(4);
				wal.Mips[3].Height.Should().Be(4);

				byte[][] first_4_pixels = new byte[][] {
									new byte[]{0x1d, 0x1d, 0x1b, 0x5d},
									new byte[]{0x16, 0x10, 0x11, 0x10},
									new byte[]{0x10, 0x36, 0x37, 0x10},
									new byte[]{0x36, 0x10, 0x36, 0x85}};

				for (int i = 0; i < 4; i++)
				{
					wal.Mips[i].Pixels[0].Should().Be(first_4_pixels[i][0]);
					wal.Mips[i].Pixels[1].Should().Be(first_4_pixels[i][1]);
					wal.Mips[i].Pixels[2].Should().Be(first_4_pixels[i][2]);
					wal.Mips[i].Pixels[3].Should().Be(first_4_pixels[i][3]);
				}
			}
			finally
			{
				stream?.Dispose();
			}
		}
	}
}