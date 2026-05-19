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
    internal interface IHasTopState
    {
        State? topState { get; set; }
        IState rootState { get; }
    }

    public class StateMachine : IHasTopState
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

        public object? actor { get; private set; }

        private readonly IState _rootState;
        private readonly Action<string> _debugLog;
        private readonly Queue<Message> _messageQueue = new();
        private readonly Queue<Message> _transitionQueue = new();

        private State _topState = null!;
        private bool _receivingMessages;

        internal bool _messageConsumed;
        public LogFlags logFlags = 0;
        public readonly string Name;

        State? IHasTopState.topState
        {
            get => _topState;
            set => _topState = value!;
        }

        IState IHasTopState.rootState => _rootState;

        internal IState Root => _rootState;

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
        /// you need explicit boundaries per state — for example, to recreate the original
        /// nested-box IMGUI layout where each composite state's children sit inside its box.
        /// No-op if the machine is not initialized.
        /// </summary>
        public void Visit(IStateVisitor visitor, WalkMode mode = WalkMode.AllStates)
        {
            if (visitor == null) throw new ArgumentNullException(nameof(visitor));
            if (!IsInitialized) return;
            VisitState(_rootState.state, depth: 0, isActive: true, visitor, mode);
        }

        private static void VisitState(State state, int depth, bool isActive, IStateVisitor visitor, WalkMode mode)
        {
            int childCount = state.WalkChildCount(mode);
            bool hasChildren = childCount > 0;

            visitor.BeginState(state, depth, isActive, hasChildren);

            for (int i = 0; i < childCount; i++)
            {
                State child = state.WalkChild(i, mode);
                bool childActive = isActive && state.WalkIsChildActive(child);
                VisitState(child, depth + 1, childActive, visitor, mode);
            }

            visitor.EndState(state, depth, isActive, hasChildren);
        }

        public void WriteLine(string text)
        {
            _debugLog(text);
        }

        public bool IsInitialized => InitializationState is InitState.Initialized or InitState.ShuttingDown;

        public InitState InitializationState { get; private set; }

        [Flags]
        public enum LogFlags
        {
            None = 0,
            EnterExit = 1 << 0,
        }

        public StateMachine(string name, IState rootState, Action<string>? debugLog = null)
        {
            _debugLog = debugLog ?? Console.WriteLine;
            Name = name;
            _rootState = rootState;
        }

        public void Initialize(object actor)
        {
            if (InitializationState != InitState.Offline)
            {
                throw new InvalidOperationException("StateMachine already initialized");
            }

            InitializationState = InitState.Initializing;
            WriteLine($"{Name} initializing");

            this.actor = actor;
            _messageQueue.Clear();
            _transitionQueue.Clear();

            try
            {
                var context = new InitContext()
                {
                    stateMachine = this,
                    iStates = new List<IState>(),
                    iBaseStates = new List<IStateBase>()
                };

                _rootState.state.Initialize(context, this);
            }
            catch(Exception initializationException)
            {
                Exception? shutdownException = null;
                try
                {
                    _rootState.state.Shutdown();
                }
                catch( Exception ex )
                {
                    shutdownException = ex;
                }
                finally
                {
                    _messageQueue.Clear();
                    _transitionQueue.Clear();
                    this.actor = null;
                    InitializationState = InitState.Offline;
                }

                if (shutdownException != null)
                {
                    throw new AggregateException("Initialization and shutdown both failed.", initializationException, shutdownException);
                }
                throw;
            }
            InitializationState = InitState.Initialized;

            try
            {
                if (!StartMessageReceiving())
                {
                    throw new InvalidProgramException("This should not ever happen..?");
                }

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
            _rootState.state.Exit();

            try
            {
                _rootState.state.Shutdown();
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
            Debug.Assert(destination?.state?.stateMachine == this, $"Destination state {destination} is not a part of the StateMachine {Name}");
            var handler = MsgTransition;

            if (StartMessageReceiving())
            {
                try
                {
                    _messageConsumed = false;
                    _topState.ReceiveMessage(handler, destination);
                    WarnUnclaimedTransition(destination?.state?.name);
                }
                finally
                {
                    StopMessageReceiving();
                    HandleMessagesInternal();
                }
            }
            else
            {
                _transitionQueue.Enqueue(Message<ITransition, IState>.Create(handler, destination));
                HandleMessagesInternal();
            }
        }

        internal void TransitTo<TArg>(IState<TArg> destination, TArg arg)
        {
            InitGuard();
            Debug.Assert(destination?.state?.stateMachine == this, $"Destination state {destination} is not a part of the StateMachine {Name}");
            var handler = TransitionHandler<TArg>.MsgTransition;

            if (StartMessageReceiving())
            {
                try
                {
                    _messageConsumed = false;
                    _topState.ReceiveMessage(handler, (destination, arg));
                    WarnUnclaimedTransition(destination?.state?.name);
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
                    Message<ITransition, (IState<TArg>, TArg)>.Create(handler, (destination, arg)));
                HandleMessagesInternal();
            }
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void WarnUnclaimedTransition(string? destinationName)
        {
            if (!_messageConsumed)
            {
                WriteLine($"{Name}: transition to {destinationName ?? "<null>"} was not claimed by any HierarchicalState on the active chain. " +
                          "The target is not reachable from the currently active state — verify it is a sibling under a common hierarchical parent.");
            }
        }

        internal void SendMessage(Message message)
        {
            _messageQueue.Enqueue(message);
            HandleMessagesInternal();
        }

        /// <summary>
        /// Send a message to the active state chain. Fire-and-forget: returns nothing.
        /// If called from inside a handler (nested), the message is queued and dispatched
        /// after the outer dispatch frame completes. Use <see cref="SendMessageNow{TReceiver}(Handler{TReceiver})"/>
        /// or <see cref="TrySendMessageNow{TReceiver}(Handler{TReceiver}, out bool)"/> when you need
        /// to know whether the message was consumed.
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

        /// <summary>
        /// Try to send a message synchronously. Returns <c>true</c> if dispatch happened in this
        /// call, in which case <paramref name="consumed"/> reflects whether any state consumed the
        /// message. Returns <c>false</c> when called from inside a handler (nested dispatch is not
        /// supported) — in that case the message is <b>not</b> sent at all; <paramref name="consumed"/>
        /// is set to <c>false</c>. Callers who want a queued fallback should follow with
        /// <see cref="SendMessage{TReceiver}(Handler{TReceiver})"/> explicitly.
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
        /// <see cref="InvalidOperationException"/> if called from inside a handler — at that point
        /// the message is not sent. Equivalent to calling <see cref="TrySendMessageNow{TReceiver}(Handler{TReceiver}, out bool)"/>
        /// and throwing when it returns <c>false</c>.
        /// </summary>
        /// <returns><c>true</c> if a state's handler consumed the message without calling
        /// <see cref="State.PropagateMessage"/>; <c>false</c> otherwise.</returns>
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

        // Shared synchronous dispatch core. Precondition: not currently receiving (caller's
        // responsibility). Returns whether the dispatched message was consumed (i.e. some state's
        // handler ran without calling PropagateMessage). The return is captured before the finally's
        // queue-drain runs, so it reflects this message specifically, not any side-effects the
        // bubble queued.
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

        private void HandleMessagesInternal()
        {
            if (!StartMessageReceiving())
            {
                return;
            }
            
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

        private void StopMessageReceiving()
        {
            _receivingMessages = false;
        }

        public class InitializationException : Exception
        {
            public InitializationException() { }
            public InitializationException(string message) : base(message) { }
            public InitializationException(string message, Exception innerException) : base(message, innerException) { }
        }

        internal struct InitContext
        {
            public StateMachine stateMachine;
            public List<IState> iStates;
            public List<IStateBase> iBaseStates;
        }
    }
}