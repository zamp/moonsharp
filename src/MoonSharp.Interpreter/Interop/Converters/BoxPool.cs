using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MoonSharp.Interpreter.Interop.Converters
{
	/// <summary>
	/// Fork: boxes for numbers and booleans passed to C# methods whose parameters are those value types. The
	/// method gets a copy of the value, so once it returns the box goes back to this thread's pool and is written over by
	/// the next call instead of a new box being made.
	/// </summary>
	internal static class BoxPool
	{
		const int MAX_POOLED = 64;

		[ThreadStatic] static Stack<object> s_Doubles;
		[ThreadStatic] static Stack<object> s_Floats;
		[ThreadStatic] static Stack<object> s_Ints;
		[ThreadStatic] static Stack<object> s_Bools;

		/// <summary>A box of the parameter type holding the value, or null when the type is not pooled.</summary>
		public static object Lend(Type type, DynValue value)
		{
			if (value.Type == DataType.Number)
			{
				if (type == typeof(double))
				{
					var box = Take(ref s_Doubles) ?? (object)0.0;
					Unsafe.Unbox<double>(box) = value.Number;
					return box;
				}

				if (type == typeof(float))
				{
					var box = Take(ref s_Floats) ?? (object)0f;
					Unsafe.Unbox<float>(box) = (float)value.Number;
					return box;
				}

				if (type == typeof(int))
				{
					var box = Take(ref s_Ints) ?? (object)0;
					Unsafe.Unbox<int>(box) = (int)value.Number;
					return box;
				}
			}
			else if (value.Type == DataType.Boolean && type == typeof(bool))
			{
				var box = Take(ref s_Bools) ?? (object)false;
				Unsafe.Unbox<bool>(box) = value.Boolean;
				return box;
			}

			return null;
		}

		/// <summary>Takes back the box passed for a parameter of this type, once the call has returned.</summary>
		public static void Return(Type type, object box)
		{
			if (box == null)
				return;

			if (type == typeof(double) && box is double)
				Give(ref s_Doubles, box);
			else if (type == typeof(float) && box is float)
				Give(ref s_Floats, box);
			else if (type == typeof(int) && box is int)
				Give(ref s_Ints, box);
			else if (type == typeof(bool) && box is bool)
				Give(ref s_Bools, box);
		}

		static object Take(ref Stack<object> pool)
		{
			return pool != null && pool.Count > 0 ? pool.Pop() : null;
		}

		static void Give(ref Stack<object> pool, object box)
		{
			if (pool == null)
				pool = new Stack<object>();

			if (pool.Count < MAX_POOLED)
				pool.Push(box);
		}
	}
}
