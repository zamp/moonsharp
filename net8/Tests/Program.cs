using System;
using MoonSharp.Interpreter.Tests;

// Fork: runs upstream's test suite with its embedded runner, printing failures; exits 1 if any fail
internal static class Program
{
	private static int Main(string[] args)
	{
		var runner = new TestRunner(result =>
		{
			if (result.Type == TestResultType.Fail)
				Console.WriteLine("FAIL " + result.TestName + ": " + result.Message + " " + result.Exception?.Message);
		});

		runner.Test(args.Length > 0 ? args[0] : null, new[] { "TestMore_308_io", "TestMore_309_os" });
		Console.WriteLine("ok " + runner.Ok + ", failed " + runner.Fail + ", skipped " + runner.Skipped + ", total " + runner.Total);
		return runner.Fail > 0 ? 1 : 0;
	}
}
