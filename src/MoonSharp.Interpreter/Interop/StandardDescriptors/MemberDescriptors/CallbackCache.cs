using System.Collections.Generic;
using System.Runtime.CompilerServices;

namespace MoonSharp.Interpreter.Interop
{
	/// <summary>
	/// Fork: the callback a method member gives for one object and script, made once and reused, so calling a
	/// method on a userdata (person:Method()) no longer builds a closure, delegate, CallbackFunction and DynValue each time.
	/// </summary>
	internal sealed class CallbackCache
	{
		private readonly ConditionalWeakTable<object, Dictionary<Script, DynValue>> m_ByObject = new ConditionalWeakTable<object, Dictionary<Script, DynValue>>();
		private readonly Dictionary<Script, DynValue> m_Static = new Dictionary<Script, DynValue>();

		private static readonly ConditionalWeakTable<object, Dictionary<Script, DynValue>>.CreateValueCallback s_NewByScript = _ => new Dictionary<Script, DynValue>();

		public DynValue Get(Script script, object obj, System.Func<Script, object, DynValue> create)
		{
			Dictionary<Script, DynValue> byScript = obj == null ? m_Static : m_ByObject.GetValue(obj, s_NewByScript);
			DynValue value;

			lock (byScript)
			{
				if (!byScript.TryGetValue(script, out value))
				{
					value = create(script, obj).AsReadOnly();
					byScript.Add(script, value);
				}
			}

			return value;
		}
	}
}
