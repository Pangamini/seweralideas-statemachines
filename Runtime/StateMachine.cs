#nullable enable

#if UNITY_5_3_OR_NEWER
#define UNITY
#if DEBUG
#define UNITY_PROFILING
#endif
#endif

#if UNITY
using UnityEngine;
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
#if UNITY
                Debug.Assert(_messageQueue.Count == 0);
                Debug.Assert(_transitionQueue.Count == 0);
#endif
                _messageQueue.Clear();
                _transitionQueue.Clear();
                InitializationState = InitState.Offline;
            }
        }


        private static readonly Handler<ITransition, IState> msg_transition = (handler, dest) =>
        {
            handler.TransitTo(dest);
        };

        private static class TransitionHandler<TArg>
        {
            public static readonly Handler<ITransition, (IState<TArg>, TArg)> msg_transition =
                (ITransition handler, (IState<TArg> destination, TArg _arg) args) =>
                {
                    handler.TransitTo(args.destination, args._arg);
                };
        }

        internal void TransitTo(IState destination)
        {
            InitGuard();
            Debug.Assert(destination?.state?.stateMachine == this, $"Destination state {destination} is not a part of the StateMachine {Name}");
            var handler = msg_transition;

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
            var handler = TransitionHandler<TArg>.msg_transition;

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

        public void SendMessage<TReceiver>(Handler<TReceiver> handler) where TReceiver : class
        {
            InitGuard();

            if(StartMessageReceiving())
            {
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
                }
                finally
                {
                    StopMessageReceiving();
                    HandleMessagesInternal();
                }
            }
            else
            {
                _messageQueue.Enqueue(Message<TReceiver>.Create(handler));
                HandleMessagesInternal();
            }
        }

        public void SendMessage<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg) where TReceiver : class
        {
            InitGuard();

            if (StartMessageReceiving())
            {
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
                }
                finally
                {
                    StopMessageReceiving();
                    HandleMessagesInternal();
                }
            }
            else
            {
                _messageQueue.Enqueue(Message<TReceiver, TArg>.Create(handler, arg));
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

#if UNITY
        [Serializable]
        public class GUISettings
        {
            public enum FieldsMode
            {
                None,
                Fields,
                AllFields
            }

            public FieldsMode fieldsMode;
            public Color stateColor_normal;
            public Color stateColor_active;

            public Color GetColor(bool isActive)
            {
                if (isActive)
                    return stateColor_active;
                else
                    return stateColor_normal;
            }
        }
        
        public void OnGUI(GUISettings settings)
        {
            if (!IsInitialized)
            {
                GUILayout.Label("StateMachine not initialized");
                return;
            }
            
            _rootState.state.DrawGUI(settings, IsInitialized);
        }
#endif

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

        // Fields are always populated via object-initializer at construction; default(InitContext)
        // is never used. Suppress the uninitialized-field warning.
#pragma warning disable CS8618
        internal struct InitContext
        {
            public StateMachine stateMachine;
            public List<IState> iStates;
            public List<IStateBase> iBaseStates;
        }
#pragma warning restore CS8618
    }
}