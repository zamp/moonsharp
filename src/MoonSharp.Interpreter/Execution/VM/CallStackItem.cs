using MoonSharp.Interpreter.Debugging;

namespace MoonSharp.Interpreter.Execution.VM
{
	internal class CallStackItem
	{
		public int Debug_EntryPoint;
		public SymbolRef[] Debug_Symbols;

		public SourceRef CallingSourceRef;

		public CallbackFunction ClrFunction;
		public CallbackFunction Continuation;
		public CallbackFunction ErrorHandler;
		public DynValue ErrorHandlerBeforeUnwind;

		public int BasePointer;
		public int ReturnAddress;
		public DynValue[] LocalScope;
		public ClosureContext ClosureScope;

		public CallStackItemFlags Flags;

		// Fork: back to the processor's pool once its call has returned
		internal void Reset()
		{
			Debug_EntryPoint = 0;
			Debug_Symbols = null;
			CallingSourceRef = null;
			ClrFunction = null;
			Continuation = null;
			ErrorHandler = null;
			ErrorHandlerBeforeUnwind = null;
			BasePointer = 0;
			ReturnAddress = 0;
			LocalScope = null;
			ClosureScope = null;
			Flags = CallStackItemFlags.None;
		}
	}

}
