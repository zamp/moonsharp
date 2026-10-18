using System;
using System.Collections.Generic;
using System.Threading;
using MoonSharp.Interpreter.DataStructs;
using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	sealed partial class Processor
	{
		ByteCode m_RootChunk;

		FastStack<DynValue> m_ValueStack = new FastStack<DynValue>(131072);
		FastStack<CallStackItem> m_ExecutionStack = new FastStack<CallStackItem>(131072);

		// Fork: call frames and their local arrays are reused once their call returns
		Stack<CallStackItem> m_CallStackItemPool = new Stack<CallStackItem>();
		Dictionary<int, Stack<DynValue[]>> m_LocalScopePool = new Dictionary<int, Stack<DynValue[]>>();

		// Fork: what a call to an interop member is given, lent and taken back once it returns
		Stack<Slice<DynValue>> m_ArgsSlicePool = new Stack<Slice<DynValue>>();
		Stack<CallbackArguments> m_CallbackArgumentsPool = new Stack<CallbackArguments>();
		Stack<ScriptExecutionContext> m_ContextPool = new Stack<ScriptExecutionContext>();

		static readonly DynValue[] s_NoArgs = new DynValue[0];

		// Fork: scratch numbers consumed and ready for the next arithmetic result
		const int MAX_FREE_SCRATCH = 256;
		Stack<DynValue> m_FreeScratch = new Stack<DynValue>();

		// Fork: argument counts pushed under a call's arguments, read-only and shared
		static readonly DynValue[] s_ArgCounts = MakeArgCounts();

		static DynValue[] MakeArgCounts()
		{
			var counts = new DynValue[16];
			for (int i = 0; i < counts.Length; i++)
				counts[i] = DynValue.NewNumber(i).AsReadOnly();
			return counts;
		}

		private static DynValue ArgCount(int count)
		{
			return count < s_ArgCounts.Length ? s_ArgCounts[count] : DynValue.NewNumber(count);
		}

		private CallStackItem RentCallStackItem()
		{
			return m_CallStackItemPool.Count > 0 ? m_CallStackItemPool.Pop() : new CallStackItem();
		}

		private DynValue[] RentLocalScope(int length)
		{
			Stack<DynValue[]> pool;
			return m_LocalScopePool.TryGetValue(length, out pool) && pool.Count > 0 ? pool.Pop() : new DynValue[length];
		}

		private void Recycle(CallStackItem csi)
		{
			var scope = csi.LocalScope;
			if (scope != null)
			{
				Array.Clear(scope, 0, scope.Length);
				Stack<DynValue[]> pool;
				if (!m_LocalScopePool.TryGetValue(scope.Length, out pool))
					m_LocalScopePool[scope.Length] = pool = new Stack<DynValue[]>();
				pool.Push(scope);
			}

			csi.Reset();
			m_CallStackItemPool.Push(csi);
		}
		List<Processor> m_CoroutinesStack;

		Table m_GlobalTable;
		Script m_Script;
		Processor m_Parent = null;
		CoroutineState m_State;
		bool m_CanYield = true;
		int m_SavedInstructionPtr = -1;
		DebugContext m_Debug;

		public Processor(Script script, Table globalContext, ByteCode byteCode)
		{
			m_CoroutinesStack = new List<Processor>();

			m_Debug = new DebugContext();
			m_RootChunk = byteCode;
			m_GlobalTable = globalContext;
			m_Script = script;
			m_State = CoroutineState.Main;
			DynValue.NewCoroutine(new Coroutine(this)); // creates an associated coroutine for the main processor
		}

		private Processor(Processor parentProcessor)
		{
			m_Debug = parentProcessor.m_Debug;
			m_RootChunk = parentProcessor.m_RootChunk;
			m_GlobalTable = parentProcessor.m_GlobalTable;
			m_Script = parentProcessor.m_Script;
			m_Parent = parentProcessor;
			m_State = CoroutineState.NotStarted;
		}



		public DynValue Call(DynValue function, DynValue[] args)
		{
			List<Processor> coroutinesStack = m_Parent != null ? m_Parent.m_CoroutinesStack : this.m_CoroutinesStack;

			if (coroutinesStack.Count > 0 && coroutinesStack[coroutinesStack.Count - 1] != this)
				return coroutinesStack[coroutinesStack.Count - 1].Call(function, args);

			EnterProcessor();

			try
			{
				var stopwatch = this.m_Script.PerformanceStats.StartStopwatch(Diagnostics.PerformanceCounter.Execution);

				m_CanYield = false;

				try
				{
					int entrypoint = PushClrToScriptStackFrame(CallStackItemFlags.CallEntryPoint, function, args);
					return Processing_Loop(entrypoint);
				}
				finally
				{
					m_CanYield = true;

					if (stopwatch != null)
						stopwatch.Dispose();
				}
			}
			finally
			{
				LeaveProcessor();
			}
		}

		// pushes all what's required to perform a clr-to-script function call. function can be null if it's already
		// at vstack top.
		private int PushClrToScriptStackFrame(CallStackItemFlags flags, DynValue function, DynValue[] args)
		{
			if (function == null) 
				function = m_ValueStack.Peek();
			else
				m_ValueStack.Push(function);  // func val

			// Fork: arguments without a tuple to unpack are pushed as they are, with no adjusted copy made
			if (args.Length > 0 && args[args.Length - 1].Type == DataType.Tuple)
			{
				args = Internal_AdjustTuple(args);

				for (int i = 0; i < args.Length; i++)
					m_ValueStack.Push(args[i]);
			}
			else
			{
				for (int i = 0; i < args.Length; i++)
					m_ValueStack.Push(args[i].ToScalar());
			}

			m_ValueStack.Push(ArgCount(args.Length));  // func args count

			var csi = RentCallStackItem();
			csi.BasePointer = m_ValueStack.Count;
			csi.Debug_EntryPoint = function.Function.EntryPointByteCodeLocation;
			csi.ReturnAddress = -1;
			csi.ClosureScope = function.Function.ClosureContext;
			csi.CallingSourceRef = SourceRef.GetClrLocation();
			csi.Flags = flags;
			m_ExecutionStack.Push(csi);

			return function.Function.EntryPointByteCodeLocation;
		}


		int m_OwningThreadID = -1;
		int m_ExecutionNesting = 0;

		private void LeaveProcessor()
		{
			m_ExecutionNesting -= 1;
			m_OwningThreadID = -1;

			if (m_Parent != null)
			{
				m_Parent.m_CoroutinesStack.RemoveAt(m_Parent.m_CoroutinesStack.Count - 1);
			}

			if (m_ExecutionNesting == 0 && m_Debug != null && m_Debug.DebuggerEnabled 
				&& m_Debug.DebuggerAttached != null)
			{
				m_Debug.DebuggerAttached.SignalExecutionEnded();
			}
		}

		int GetThreadId()
		{
			#if ENABLE_DOTNET || NETFX_CORE
				return 1;
			#else
				return Thread.CurrentThread.ManagedThreadId;
			#endif
		}

		private void EnterProcessor()
		{
			int threadID = GetThreadId();

			if (m_OwningThreadID >= 0 && m_OwningThreadID != threadID && m_Script.Options.CheckThreadAccess)
			{
				string msg = string.Format("Cannot enter the same MoonSharp processor from two different threads : {0} and {1}", m_OwningThreadID, threadID);
				throw new InvalidOperationException(msg);
			}

			m_OwningThreadID = threadID;

			m_ExecutionNesting += 1;

			if (m_Parent != null)
			{
				m_Parent.m_CoroutinesStack.Add(this);
			}
		}

		internal SourceRef GetCoroutineSuspendedLocation()
		{
			return GetCurrentSourceRef(m_SavedInstructionPtr);
		}
	}
}
