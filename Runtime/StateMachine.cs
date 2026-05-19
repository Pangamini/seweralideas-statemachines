#nullable enable

#if UNITY_5_3_OR_NEWER && DEBUG
#define UNITY_PROFILING
#endif

#if UNITY_PROFILING
using UnityEngine.Profiling;
#endif

using System;
using System.Collections.Generic;
using Debug = System.Diagnostics.Debug;

namespace SeweralIdeas.StateMachines
{
    public class StateMachine
    {
        public enum InitState : byte
        {
            Offline,
            Initializing,
            Initialized,
            ShuttingDown
        }

        internal interface ITransition
        {
            void TransitTo(IState state);
            void TransitTo<TArg>(IState<TArg> state, TArg arg);
        }

        public readonly object Actor;

        private readonly IState _rootState;
        private readonly Action<string> _debugLog;
        private readonly Queue<Message> _messageQueue = new();
        private readonly Queue<Message> _transitionQueue = new();

        private State _topState = null!;
        private bool _receivingMessages;

        internal bool _messageConsumed;
        public LogFlags Logging { get; set; } = 0;
        public readonly string Name;

        internal State RootState => _rootState.State;
        internal void SetTopState(State state) => _topState = state;

        /// <summary>
        /// Walk the state machine's tree. Returns an allocation-aware struct enumerable;
        /// use with <c>foreach</c>. Iteration yields each visited state in depth-first order
        /// with depth, active-flag, and has-children metadata on each <see cref="StateNode"/>.
        /// If the machine is not initialized the walk is empty.
        /// </summary>
        public StateWalker Walk(WalkMode mode = WalkMode.AllStates) => new(this, mode);

        /// <summary>
        /// Recursively visit the tree, calling <see cref="IStateVisitor.BeginState"/> before
        /// each state's children and <see cref="IStateVisitor.EndState"/> after. Use this when
        /// you need explicit boundaries per state — for example, to recreate the nested-box
        /// IMGUI layout where each composite state's children sit inside its box.
        /// No-op if the machine is not initialized.
        /// </summary>
        public void Visit(IStateVisitor visitor, WalkMode mode = WalkMode.AllStates)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            if (!IsInitialized) return;
            VisitState(_rootState.State, depth: 0, isActive: true, visitor, mode);
        }

        private static void VisitState(State state, int depth, bool isActive, IStateVisitor visitor, WalkMode mode)
        {
            bool hasChildren = mode == WalkMode.AllStates
                ? state.ChildCount > 0
                : state.ActiveSubState != null;

            visitor.BeginState(state, depth, isActive, hasChildren);

            if (mode == WalkMode.AllStates)
            {
                int count = state.ChildCount;
                for (int i = 0; i < count; i++)
                {
                    var child = state.GetChild(i);
                    bool childActive = isActive && ReferenceEquals(state.ActiveSubState, child);
                    VisitState(child, depth + 1, childActive, visitor, mode);
                }
            }
            else
            {
                var active = state.ActiveSubState;
                if (active != null)
                    VisitState(active, depth + 1, isActive, visitor, mode);
            }

            visitor.EndState(state, depth, isActive, hasChildren);
        }

        public void WriteLine(string text) => _debugLog(text);

        public bool IsInitialized => InitializationState is InitState.Initialized or InitState.ShuttingDown;
        public InitState InitializationState { get; private set; }

        [Flags]
        public enum LogFlags
        {
            None = 0,
            EnterExit = 1 << 0,
        }

        public StateMachine(string name, object actor, IState rootState, Action<string>? debugLog = null)
        {
            _debugLog = debugLog ?? Console.WriteLine;
            Name = name;
            _rootState = rootState;
            Actor = actor;
            
            List<IStateBase> stateList = new();
            rootState.State.Build(new(this, stateList));
        }

        public void Initialize()
        {
            if (InitializationState != InitState.Offline)
                throw new InvalidOperationException("StateMachine already initialized");

            InitializationState = InitState.Initializing;
            WriteLine($"{Name} initializing");

            _messageQueue.Clear();
            _transitionQueue.Clear();

            try
            {
                _rootState.State.Initialize();
            }
            catch(Exception initializationException)
            {
                Exception? shutdownException = null;
                try
                {
                    _rootState.State.Shutdown();
                }
                catch(Exception ex)
                {
                    shutdownException = ex;
                }
                finally
                {
                    _messageQueue.Clear();
                    _transitionQueue.Clear();
                    InitializationState = InitState.Offline;
                }

                if (shutdownException != null)
                    throw new AggregateException("Initialization and shutdown both failed.", initializationException, shutdownException);
                throw;
            }
            InitializationState = InitState.Initialized;

            try
            {
                if (!StartMessageReceiving())
                    throw new InvalidProgramException("This should not ever happen..?");

                _messageConsumed = false;
                _rootState.StateEnter();
            }
            finally
            {
                StopMessageReceiving();
                HandleMessagesInternal();
            }
        }

        public void Shutdown()
        {
            InitGuard();

            InitializationState = InitState.ShuttingDown;
            WriteLine($"{Name} shutting down");

            _messageConsumed = false;
            _rootState.State.Exit();

            try
            {
                _rootState.State.Shutdown();
            }
            finally
            {
                Debug.Assert(_messageQueue.Count == 0);
                Debug.Assert(_transitionQueue.Count == 0);
                _messageQueue.Clear();
                _transitionQueue.Clear();
                InitializationState = InitState.Offline;
            }
        }

        // === Transitions ===

        private static readonly Handler<ITransition, IState> MsgTransition = (receiver, dest) =>
        {
            receiver.TransitTo(dest);
        };

        private static class TransitionHandler<TArg>
        {
            public static readonly Handler<ITransition, (IState<TArg>, TArg)> MsgTransition =
                (ITransition receiver, (IState<TArg> destination, TArg _arg) args) =>
                {
                    receiver.TransitTo(args.destination, args._arg);
                };
        }

        internal void TransitTo(IState destination)
        {
            InitGuard();
            Debug.Assert(destination?.State?.StateMachine == this, $"Destination state {destination} is not a part of the StateMachine {Name}");
            var handler = MsgTransition;

            if (StartMessageReceiving())
            {
                try
                {
                    _messageConsumed = false;
                    _topState.ReceiveMessage(handler, destination!);
                    WarnUnclaimedTransition(destination?.State?.Name);
                }
                finally
                {
                    StopMessageReceiving();
                    HandleMessagesInternal();
                }
            }
            else
            {
                _transitionQueue.Enqueue(Message<ITransition, IState>.Create(handler, destination!));
                HandleMessagesInternal();
            }
        }

        internal void TransitTo<TArg>(IState<TArg> destination, TArg arg)
        {
            InitGuard();
            Debug.Assert(destination?.State?.StateMachine == this, $"Destination state {destination} is not a part of the StateMachine {Name}");
            var handler = TransitionHandler<TArg>.MsgTransition;

            if (StartMessageReceiving())
            {
                try
                {
                    _messageConsumed = false;
                    _topState.ReceiveMessage(handler, (destination!, arg));
                    WarnUnclaimedTransition(destination?.State?.Name);
                }
                finally
                {
                    StopMessageReceiving();
                    HandleMessagesInternal();
                }
            }
            else
            {
                _transitionQueue.Enqueue(
                    Message<ITransition, (IState<TArg>, TArg)>.Create(handler, (destination!, arg)));
                HandleMessagesInternal();
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void WarnUnclaimedTransition(string? destinationName)
        {
            if (!_messageConsumed)
            {
                WriteLine($"{Name}: transition to {destinationName ?? "<null>"} was not claimed by any state on the active chain. " +
                          "The target is not reachable from the currently active state — verify it is a child of some state on the active chain.");
            }
        }

        // === Message send ===

        internal void SendMessage(Message message)
        {
            _messageQueue.Enqueue(message);
            HandleMessagesInternal();
        }

        /// <summary>
        /// Send a message to the active state chain. Fire-and-forget. If called from inside a
        /// handler (nested), the message is queued and dispatched after the outer dispatch
        /// frame completes. Use <see cref="SendMessageNow{TReceiver}(Handler{TReceiver})"/> or
        /// the Func-handler overloads when you need to know whether the message was consumed
        /// (or want a return value).
        /// </summary>
        public void SendMessage<TReceiver>(Handler<TReceiver> handler) where TReceiver : class
        {
            InitGuard();
            if (_receivingMessages)
            {
                _messageQueue.Enqueue(Message<TReceiver>.Create(handler));
                HandleMessagesInternal();
            }
            else
            {
                DispatchSyncCore(handler);
            }
        }

        /// <inheritdoc cref="SendMessage{TReceiver}(Handler{TReceiver})"/>
        public void SendMessage<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg) where TReceiver : class
        {
            InitGuard();
            if (_receivingMessages)
            {
                _messageQueue.Enqueue(Message<TReceiver, TArg>.Create(handler, arg));
                HandleMessagesInternal();
            }
            else
            {
                DispatchSyncCore(handler, arg);
            }
        }

        // === SendMessageNow / TrySendMessageNow — void handlers ===

        /// <summary>
        /// Try to send a message synchronously. Returns <c>true</c> if dispatch happened in this
        /// call, in which case <paramref name="consumed"/> reflects whether any state consumed the
        /// message. Returns <c>false</c> when called from inside a handler — in that case the
        /// message is <b>not</b> sent at all and <paramref name="consumed"/> is set to <c>false</c>.
        /// </summary>
        public bool TrySendMessageNow<TReceiver>(Handler<TReceiver> handler, out bool consumed) where TReceiver : class
        {
            InitGuard();
            if (_receivingMessages)
            {
                consumed = false;
                return false;
            }
            consumed = DispatchSyncCore(handler);
            return true;
        }

        /// <inheritdoc cref="TrySendMessageNow{TReceiver}(Handler{TReceiver}, out bool)"/>
        public bool TrySendMessageNow<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg, out bool consumed) where TReceiver : class
        {
            InitGuard();
            if (_receivingMessages)
            {
                consumed = false;
                return false;
            }
            consumed = DispatchSyncCore(handler, arg);
            return true;
        }

        /// <summary>
        /// Send a message synchronously and return whether any state consumed it. Throws
        /// <see cref="InvalidOperationException"/> if called from inside a handler — message is not
        /// sent in that case. Equivalent to <see cref="TrySendMessageNow{TReceiver}(Handler{TReceiver}, out bool)"/>
        /// plus throw-on-failure.
        /// </summary>
        public bool SendMessageNow<TReceiver>(Handler<TReceiver> handler) where TReceiver : class
        {
            if (!TrySendMessageNow(handler, out bool consumed))
                throw new InvalidOperationException(
                    "SendMessageNow cannot be called from inside a handler (nested dispatch). " +
                    "Use TrySendMessageNow to handle this case without throwing, or " +
                    "SendMessage for fire-and-forget queued delivery.");
            return consumed;
        }

        /// <inheritdoc cref="SendMessageNow{TReceiver}(Handler{TReceiver})"/>
        public bool SendMessageNow<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg) where TReceiver : class
        {
            if (!TrySendMessageNow(handler, arg, out bool consumed))
                throw new InvalidOperationException(
                    "SendMessageNow cannot be called from inside a handler (nested dispatch). " +
                    "Use TrySendMessageNow to handle this case without throwing, or " +
                    "SendMessage for fire-and-forget queued delivery.");
            return consumed;
        }

        // === SendMessageNow / TrySendMessageNow — Func handlers (return a value) ===

        /// <summary>
        /// Try to send a message synchronously where the handler returns a value. Returns
        /// <c>true</c> if a state consumed the message and produced a result (in which case
        /// <paramref name="result"/> holds it). Returns <c>false</c> if no state consumed it
        /// <b>or</b> if called from inside a handler — message is not sent in the nested case.
        /// </summary>
        public bool TrySendMessageNow<TReceiver, TResult>(Func<TReceiver, TResult> handler, out TResult result) where TReceiver : class
        {
            InitGuard();
            if (_receivingMessages)
            {
                result = default!;
                return false;
            }
            return DispatchSyncCore(handler, out result);
        }

        /// <inheritdoc cref="TrySendMessageNow{TReceiver, TResult}(Func{TReceiver, TResult}, out TResult)"/>
        public bool TrySendMessageNow<TReceiver, TArg, TResult>(Func<TReceiver, TArg, TResult> handler, TArg arg, out TResult result) where TReceiver : class
        {
            InitGuard();
            if (_receivingMessages)
            {
                result = default!;
                return false;
            }
            return DispatchSyncCore(handler, arg, out result);
        }

        /// <summary>
        /// Send a message synchronously where the handler returns a value. Returns the
        /// consumption bool and the result via <paramref name="result"/>. Throws
        /// <see cref="InvalidOperationException"/> if called from inside a handler — message
        /// is not sent in that case.
        /// </summary>
        public bool SendMessageNow<TReceiver, TResult>(Func<TReceiver, TResult> handler, out TResult result) where TReceiver : class
        {
            InitGuard();
            if (_receivingMessages)
                throw new InvalidOperationException(
                    "SendMessageNow cannot be called from inside a handler (nested dispatch). " +
                    "Use TrySendMessageNow to handle this case without throwing.");
            return DispatchSyncCore(handler, out result);
        }

        /// <inheritdoc cref="SendMessageNow{TReceiver, TResult}(Func{TReceiver, TResult}, out TResult)"/>
        public bool SendMessageNow<TReceiver, TArg, TResult>(Func<TReceiver, TArg, TResult> handler, TArg arg, out TResult result) where TReceiver : class
        {
            InitGuard();
            if (_receivingMessages)
                throw new InvalidOperationException(
                    "SendMessageNow cannot be called from inside a handler (nested dispatch). " +
                    "Use TrySendMessageNow to handle this case without throwing.");
            return DispatchSyncCore(handler, arg, out result);
        }

        // === Synchronous dispatch cores ===
        // Preconditions: not currently receiving (caller's responsibility). Each core sets the
        // _receivingMessages flag, dispatches inline, drains any queue side-effects in finally,
        // and returns the captured consumption flag.

        private bool DispatchSyncCore<TReceiver>(Handler<TReceiver> handler) where TReceiver : class
        {
            StartMessageReceiving();
            try
            {
                _messageConsumed = false;
#if UNITY_PROFILING
                Profiler.BeginSample(ReceiverTypeNameCache.GetName(typeof(TReceiver)));
#endif
                _topState.ReceiveMessage(handler);
#if UNITY_PROFILING
                Profiler.EndSample();
#endif
                return _messageConsumed;
            }
            finally
            {
                StopMessageReceiving();
                HandleMessagesInternal();
            }
        }

        private bool DispatchSyncCore<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg) where TReceiver : class
        {
            StartMessageReceiving();
            try
            {
                _messageConsumed = false;
#if UNITY_PROFILING
                Profiler.BeginSample(ReceiverTypeNameCache.GetName(typeof(TReceiver)));
#endif
                _topState.ReceiveMessage(handler, arg);
#if UNITY_PROFILING
                Profiler.EndSample();
#endif
                return _messageConsumed;
            }
            finally
            {
                StopMessageReceiving();
                HandleMessagesInternal();
            }
        }

        private bool DispatchSyncCore<TReceiver, TResult>(Func<TReceiver, TResult> handler, out TResult result) where TReceiver : class
        {
            StartMessageReceiving();
            try
            {
                _messageConsumed = false;
#if UNITY_PROFILING
                Profiler.BeginSample(ReceiverTypeNameCache.GetName(typeof(TReceiver)));
#endif
                _topState.ReceiveMessage(handler, out result);
#if UNITY_PROFILING
                Profiler.EndSample();
#endif
                return _messageConsumed;
            }
            finally
            {
                StopMessageReceiving();
                HandleMessagesInternal();
            }
        }

        private bool DispatchSyncCore<TReceiver, TArg, TResult>(Func<TReceiver, TArg, TResult> handler, TArg arg, out TResult result) where TReceiver : class
        {
            StartMessageReceiving();
            try
            {
                _messageConsumed = false;
#if UNITY_PROFILING
                Profiler.BeginSample(ReceiverTypeNameCache.GetName(typeof(TReceiver)));
#endif
                _topState.ReceiveMessage(handler, arg, out result);
#if UNITY_PROFILING
                Profiler.EndSample();
#endif
                return _messageConsumed;
            }
            finally
            {
                StopMessageReceiving();
                HandleMessagesInternal();
            }
        }

        private void HandleMessagesInternal()
        {
            if (!StartMessageReceiving())
                return;

            try
            {
                while (InitializationState == InitState.Initialized)
                {
                    if (_transitionQueue.TryDequeue(out Message? transition))
                    {
                        try
                        {
                            _messageConsumed = false;
                            transition.Dispatch(_topState);
                            WarnUnclaimedTransition("<queued transition>");
                        }
                        finally
                        {
                            transition.Destroy();
                        }
                        continue;
                    }

                    if (_messageQueue.TryDequeue(out Message? message))
                    {
                        try
                        {
                            _messageConsumed = false;
#if UNITY_PROFILING
                            Profiler.BeginSample(message.ReceiverName);
#endif
                            message.Dispatch(_topState);
#if UNITY_PROFILING
                            Profiler.EndSample();
#endif
                        }
                        finally
                        {
                            message.Destroy();
                        }
                        continue;
                    }

                    break;
                }
            }
            finally
            {
                StopMessageReceiving();
            }
        }

        private void InitGuard()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("StateMachine is not initialized");
        }

        private bool StartMessageReceiving()
        {
            bool wasNotReceiving = !_receivingMessages;
            _receivingMessages = true;
            return wasNotReceiving;
        }

        private void StopMessageReceiving() => _receivingMessages = false;

        public class InitializationException : Exception
        {
            public InitializationException() { }
            public InitializationException(string message) : base(message) { }
            public InitializationException(string message, Exception innerException) : base(message, innerException) { }
        }
    }
}
