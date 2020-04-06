using System;
using System.Collections.Generic;
using CommandLine;

namespace Q2Viewer
{
	public class Options
	{
		[Option('m', "map", Required = true, HelpText = "Path to a .bsp map")]
		public string MapPath { get; set; }

		[Option('p', "paks", Required = false, HelpText = "List of paths to .pak files")]
		public IEnumerable<string> PakPaths { get; set; }
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
