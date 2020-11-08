using System;
using System.Collections.Generic;
using CommandLine;
using Veldrid;

namespace Q2Viewer
{
	public class Options
	{
		[Option('m', "map", Required = true, HelpText = "Path to a .bsp map")]
		public string MapPath { get; set; }

		[Option('p', "paks", Required = false, HelpText = "List of paths to .pak files")]
		public IEnumerable<string> PakPaths { get; set; }

		[Option('b', "backend", Required = false, HelpText = "Backend to use (Direct3D11, OpenGL, OpenGLES, Vulkan, Metal)")]
		public GraphicsBackend? Backend { get; set; }
	}

	class Program
	{
		static void Main(string[] args)
		{
			Parser.Default.ParseArguments<Options>(args)
				.WithParsed(Start)
				.WithNotParsed(ParseError);
		}

		static void Start(Options options) =>
			(new Q2Viewer(options)).Run();

		static void ParseError(IEnumerable<Error> errors)
		{
			foreach (var error in errors)
				Console.Error.WriteLine(error.ToString());
			Environment.Exit(1);
		}
	}
}
