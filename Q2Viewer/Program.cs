using System;

namespace Q2Viewer
{
	class Program
	{
		static void Main(string[] args)
		{
			// TODO: Read args properly
			var bspPath = string.Join(' ', args);
			(new Q2Viewer(bspPath)).Run();
		}
	}
}
