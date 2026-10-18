using NUnit.Framework;
using MoonSharp.Interpreter;

namespace MoonSharp.Interpreter.Tests.Fork
{
	// Fork: arithmetic results reuse scratch numbers; a value kept anywhere must never change afterwards
	[TestFixture]
	public class ScratchNumberTests
	{
		private static double Run(string code)
		{
			return new Script().DoString(code).Number;
		}

		[Test]
		public void KeptInATable_ValuesDoNotChangeWithLaterArithmetic()
		{
			Assert.AreEqual(14, Run("local x = 3; local t = {}; t[1] = x * 2; t.b = x + 1; local y = x * 5 + 7; return t[1] + t.b + 0 * y + (x + x) - 6 + 4"));
		}

		[Test]
		public void KeptInATableConstructorAndTuple_ValuesStay()
		{
			Assert.AreEqual(30, Run("local x = 2; local t = { x * 3, x * 4, k = x * 5 }; local a, b = x * 1, x * 2; local z = x * 100; return t[1] + t[2] + t.k + a + b - 0 * z"));
		}

		[Test]
		public void PassedToFunctionsAndReturned_ValuesStay()
		{
			Assert.AreEqual(6, Run("local function f(a, b) local c = a * 2; return c + b end; local x = f(1 + 1, 2 * 1); local y = f(x * 0, 1); return x - 1 + y * 0 + 0 * f(3, 3) + 1 - y + y"));
		}

		[Test]
		public void PassedToCSharpFunctionsThatKeepThem_ValuesStay()
		{
			Assert.AreEqual(9, Run("local t = {}; local x = 2; table.insert(t, x * 2); table.insert(t, x + 3); local junk = x * 99 + x * 7; return t[1] + t[2]"));
		}

		[Test]
		public void CapturedByClosures_ValuesStay()
		{
			Assert.AreEqual(11, Run("local x = 2; local y = x * 3; local f = function() return y end; local z = x * 50; y = y + 5; return f()"));
		}

		[Test]
		public void LoopsAndComparisons_StillCountRight()
		{
			Assert.AreEqual(5050, Run("local s = 0; for i = 1, 10 * 10 do if i * 2 > 0 and i + 0 == i then s = s + i * 1 end end; return s"));
		}

		[Test]
		public void SwappedAndCopied_ValuesStay()
		{
			Assert.AreEqual(20, Run("local a, b = 1 * 2, 3 * 3; a, b = b + 0, a + 0; local t = { v = 1 }; t.v = t.v + a * 1; return t.v + b * 0 + a + 1"));
		}

		[Test]
		public void YieldedFromCoroutines_ValuesStay()
		{
			Assert.AreEqual(15, Run("local co = coroutine.create(function(x) coroutine.yield(x * 2, x * 3) return x * 10 end); local _, a, b = coroutine.resume(co, 1); local _, c = coroutine.resume(co); return a + b + c"));
		}

		[Test]
		public void ReturnedToCSharp_ValueDoesNotChangeOnTheNextCall()
		{
			var script = new Script();
			script.DoString("function f(x) return x * 2 end");
			var first = script.Call(script.Globals.Get("f"), 2);
			script.Call(script.Globals.Get("f"), 50);
			Assert.AreEqual(4, first.Number);
		}
	}
}
