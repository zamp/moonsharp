using System.Collections.Generic;
using System.Runtime.CompilerServices;
using MoonSharp.Interpreter.Interop.BasicDescriptors;

namespace MoonSharp.Interpreter.Interop
{
	/// <summary>
	/// Fork: the callback a method member gives for one object and script, made once and reused, so calling a
	/// method on a userdata (person:Method()) no longer builds a closure, delegate, CallbackFunction and DynValue each time.
	/// </summary>
	internal sealed class CallbackCache
	{
		// whether parameters of these types can't hold on to a DynValue passed in
		public static bool KeepsNoArguments(ParameterDescriptor[] parameters)
		{
			foreach (var parameter in parameters)
				if (!KeepsNoDynValue(parameter.Type))
					return false;

			return true;
		}

		private static bool KeepsNoDynValue(System.Type type)
		{
			if (type.IsByRef || type.IsArray)
				return KeepsNoDynValue(type.GetElementType());

			if (typeof(DynValue).IsAssignableFrom(type) || type == typeof(CallbackArguments))
				return false;

			if (type.IsGenericType)
				foreach (var argument in type.GetGenericArguments())
					if (!KeepsNoDynValue(argument))
						return false;

			return true;
		}

		private readonly ConditionalWeakTable<object, Dictionary<Script, DynValue>> m_ByObject = new ConditionalWeakTable<object, Dictionary<Script, DynValue>>();
		private readonly Dictionary<Script, DynValue> m_Static = new Dictionary<Script, DynValue>();

		private static readonly ConditionalWeakTable<object, Dictionary<Script, DynValue>>.CreateValueCallback s_NewByScript = _ => new Dictionary<Script, DynValue>();

		public DynValue Get(Script script, object obj, System.Func<Script, object, DynValue> create, bool keepsNoArguments)
		{
			Dictionary<Script, DynValue> byScript = obj == null ? m_Static : m_ByObject.GetValue(obj, s_NewByScript);
			DynValue value;

			lock (byScript)
			{
				if (!byScript.TryGetValue(script, out value))
				{
					value = create(script, obj).AsReadOnly();
					value.Callback.LendsCallData = true;
					value.Callback.KeepsNoArguments = keepsNoArguments;
					byScript.Add(script, value);
				}
			}

			return value;
		}
	}
}
