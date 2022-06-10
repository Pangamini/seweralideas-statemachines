#if UNITY_5_3_OR_NEWER
#define UNITY
using UnityEngine;
#endif

using System.Collections.Concurrent;
using System;
using System.Threading;

namespace SeweralIdeas.StateMachines
{
    
    internal interface IHasTopState
    {
        State topState { get; set; }
        IState rootState { get; }
    }
    
    public class StateMachine : IHasTopState
    {
        public enum InitializationState : byte { Offline, Initializing, Initialized, ShuttingDown }

        internal interface ITransition
        {
            void TransitTo(IState state);
            void TransitTo<TArg>(IState<TArg> state, TArg arg);
        }

        public object actor { get; private set; }
        private volatile int m_messageReceivingThread;
        private object m_lock = new object();
        private IState m_rootState;
        private State m_topState;
        State IHasTopState.topState
        { 
            get => m_topState;
            set => m_topState = value;
        }
        
        IState IHasTopState.rootState => m_rootState;
        
        private Action<string> m_debugLog;

        public void WriteLine(string text)
        {
            m_debugLog(text);
        }

        public bool IsInitialized => initializationState == InitializationState.Initialized || initializationState == InitializationState.ShuttingDown;

        public InitializationState initializationState
        {
            get { return m_initializationState; }
            set { m_initializationState = value; }
        }

        private volatile InitializationState m_initializationState;
        public readonly bool synchronousReceiving;
        public readonly AutoResetEvent messagesReadyEvent;

        private ConcurrentQueue<Message> m_messageQueue;
        private ConcurrentQueue<Message> m_messagePriorityQueue;
        private ConcurrentQueue<Message> m_transitionQueue;
        internal bool m_messageConsumed;

        [Flags]
        public enum LogFlags
        {
            None = 0,
            EnterExit = 1 <<0,
        }
        public LogFlags logFlags = 0;
        public readonly string Name;

        public StateMachine(string name, IState rootState, bool synchronousReceiving = true, Action<string> debugLog = null)
        {
            if (debugLog == null)
                m_debugLog = Console.WriteLine;
            else
                m_debugLog = debugLog;
            Name = name;
            this.synchronousReceiving = synchronousReceiving;
            m_rootState = rootState;
            messagesReadyEvent = null;
        }

        public StateMachine(string name, IState rootState, AutoResetEvent messagesReadyEvent, Action<string> debugLog = null) : this(name, rootState, false, debugLog)
        {
            this.messagesReadyEvent = messagesReadyEvent;
        }
        
        public StateMachine(string name, IState rootState, Action onMessagesAvailable, Action<string> debugLog = null) : this(name, rootState, false, debugLog)
        {
            this.m_onMessagesAvailable = onMessagesAvailable;
        }

        private ConcurrentQueue<Message> GetQueue(bool priority)
        {
            return priority ? m_messagePriorityQueue : m_messageQueue;
        }

        public void Initialize(object actor)
        {
            lock (m_lock)
            {
                messagesReadyEvent?.Reset();
                m_messageReceivingThread = 0;
                m_hasMessages = 0;
                if (initializationState != InitializationState.Offline)
                    throw new InvalidOperationException("StateMachine already initialized");
                initializationState = InitializationState.Initializing;
                WriteLine($"{Name} initializing");

                this.actor = actor;
                m_messageQueue = new ConcurrentQueue<Message>();
                m_messagePriorityQueue = new ConcurrentQueue<Message>();
                m_transitionQueue = new ConcurrentQueue<Message>();

                try
                {
                    m_rootState.state.Initialize(this, this);
                }
                catch
                {
                    m_rootState?.state?.Shutdown();
                    m_messageQueue = null;
                    m_messagePriorityQueue = null;
                    m_transitionQueue = null;
                    this.actor = null;
                    initializationState = InitializationState.Offline;
                    throw;
                }

                initializationState = InitializationState.Initialized;
                
                try
                {
                    if (!StartMessageReceiving(out _))
                        throw new InvalidProgramException("This should not ever happen..?");
                    m_messageConsumed = false;
                    m_rootState.StateEnter();
                }
                finally
                {
                    StopMessageReceiving();
                    if (synchronousReceiving)
                    {
                        HandleMessagesInternal();
                    }
                }
                
            }
        }

        public void Shutdown()
        {
            lock (m_lock)
            {
                while (!StartMessageReceiving(out bool sameThread))
                {
                    if (sameThread)
                        break;  // don't wait, we are called from within the message handling
                    Thread.Sleep(0);
                }
                if (!IsInitialized)
                    return;

                initializationState = InitializationState.ShuttingDown;
                WriteLine($"{Name} shutting down");

                m_messageConsumed = false;
                m_rootState.state.Exit();
             
                m_rootState.state.Shutdown();
                m_messageQueue = null;
                m_messagePriorityQueue = null;
                m_transitionQueue = null;
                initializationState = InitializationState.Offline;
                messagesReadyEvent?.Set();    // let the handling thread close/crash instead of dangling;
            }
        }


        private static Handler<ITransition, IState> msg_transition = (ITransition handler, IState _dest) => { handler.TransitTo(_dest); };
        private static class TransitionHandler<TArg>
        {
            public readonly static Handler<ITransition, (IState<TArg>, TArg)> msg_transition =
                (ITransition handler, (IState<TArg> destination, TArg _arg) args) => { handler.TransitTo(args.destination, args._arg); };
        }


        private void OnMessageEnqueued()
        {
            if (Interlocked.CompareExchange(ref m_hasMessages, 1, 0) == 0)
            {
                messagesReadyEvent?.Set();
                m_onMessagesAvailable?.Invoke();
            }
        }

        internal void TransitTo(IState destination)
        {
            InitGuard();
            var handler = msg_transition;
            if (synchronousReceiving)
            {
                if (StartMessageReceiving(out bool sameThread))
                {
                    try
                    {
                        m_messageConsumed = false;
                        m_topState.ReceiveMessage(handler, destination);
                    }
                    finally
                    {
                        StopMessageReceiving();
                        HandleMessagesInternal();
                    }
                }
                else
                {
                    m_transitionQueue.Enqueue(Message<ITransition, IState>.Create(handler, destination));
                    HandleMessagesInternal();
                }
            }
            else
            {
                StartMessageReceiving(out _);
                m_transitionQueue.Enqueue(Message<ITransition, IState>.Create(handler, destination));
                StopMessageReceiving();
                OnMessageEnqueued();
            }
        }

        internal void TransitTo<TArg>(IState<TArg> destination, TArg arg)
        {
            InitGuard();
            var handler = TransitionHandler<TArg>.msg_transition;
            if (synchronousReceiving)
            {
                if (StartMessageReceiving(out bool sameThread))
                {
                    try
                    {
                        m_messageConsumed = false;
                        m_topState.ReceiveMessage(handler, (destination, arg));
                    }
                    finally
                    {
                        StopMessageReceiving();
                        HandleMessagesInternal();
                    }
                }
                else
                {
                    m_transitionQueue.Enqueue(Message<ITransition, (IState<TArg>, TArg)>.Create(handler, (destination, arg)));
                    HandleMessagesInternal();
                }
            }
            else
            {
                StartMessageReceiving(out _);
                m_transitionQueue.Enqueue(Message<ITransition, (IState<TArg>, TArg)>.Create(handler, (destination, arg)));
                StopMessageReceiving();
                OnMessageEnqueued();
            }
        }

        public void SendMessage<TReceiver>(Handler<TReceiver> handler) where TReceiver:class
        {
            InitGuard();
            if (synchronousReceiving)
            {
                if (StartMessageReceiving(out bool sameThread))
                {
                    try
                    {
                        m_messageConsumed = false;
                        m_topState.ReceiveMessage(handler);
                    }
                    finally
                    {
                        StopMessageReceiving();
                        HandleMessagesInternal();
                    }
                }
                else
                {
                    GetQueue(sameThread)?.Enqueue( Message<TReceiver>.Create(handler) );
                    HandleMessagesInternal();
                }
            }
            else
            {
                StartMessageReceiving(out _);
                bool priority = m_handleMessagesExternalThread == Thread.CurrentThread.ManagedThreadId;
                GetQueue(priority)?.Enqueue(Message<TReceiver>.Create(handler));
                StopMessageReceiving();
                OnMessageEnqueued();
            }
        }

        public void SendMessage<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg) where TReceiver : class
        {
            InitGuard();
            if (synchronousReceiving)
            {
                if (StartMessageReceiving(out bool sameThread))
                {
                    try
                    {
                        m_messageConsumed = false;
                        m_topState.ReceiveMessage(handler, arg);
                    }
                    finally
                    {
                        StopMessageReceiving();
                        HandleMessagesInternal();
                    }
                }
                else
                {
                    GetQueue(sameThread)?.Enqueue(Message<TReceiver, TArg>.Create(handler, arg));
                    HandleMessagesInternal();
                }
            }
            else
            {
                StartMessageReceiving(out _);
                bool priority = m_handleMessagesExternalThread == Thread.CurrentThread.ManagedThreadId;
                GetQueue(priority)?.Enqueue(Message<TReceiver,TArg>.Create(handler, arg));
                StopMessageReceiving();
                OnMessageEnqueued();
            }
        }

        volatile int m_handleMessagesExternalThread;
        volatile int m_hasMessages;
        private readonly Action m_onMessagesAvailable;

        public bool HandleMessages(int maxCount = -1)
        {
            lock (m_lock)
            {
                m_handleMessagesExternalThread = Thread.CurrentThread.ManagedThreadId;
                if (!IsInitialized)
                    return false;
                bool reschedule = HandleMessagesInternal(maxCount);
                m_handleMessagesExternalThread = 0;
                m_hasMessages = 0;
                if (reschedule)
                    OnMessageEnqueued();
            }
            return true;
        }

        /// <returns>True if message handling was stopped early dye to maxCount being reached</returns>
        private bool HandleMessagesInternal(int maxCount = -1)
        {
            //Debug.Assert(!receivingMessage);
            if (StartMessageReceiving(out _))
            {
                try
                {
                    while (initializationState == InitializationState.Initialized)
                    {
                        if (maxCount == 0)
                            return true;
                        else if (maxCount > 0)
                            --maxCount;

                        if (m_transitionQueue.TryDequeue(out Message transition))
                        {
                            try
                            {
                                m_messageConsumed = false;
                                transition.Dispatch(m_topState);
                            }
                            finally
                            {
                                transition.Destroy();
                            }
                            continue;
                        }

                        if (m_messagePriorityQueue.TryDequeue(out Message priorityMessage))
                        {
                            try
                            {
                                m_messageConsumed = false;
                                priorityMessage.Dispatch(m_topState);
                            }
                            finally
                            {
                                priorityMessage.Destroy();
                            }
                            continue;
                        }

                        if (m_messageQueue.TryDequeue(out Message message))
                        {
                            try
                            {
                                m_messageConsumed = false;
                                message.Dispatch(m_topState);
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
            return false;
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
            //GUILayout.Label("Root State: " + m_rootState);
            //GUILayout.Label("Top State: " + m_topState);
            if (!IsInitialized)
            {
                GUILayout.Label("StateMachine not initialized");
                return;
            }
            
            m_rootState.state.DrawGUI(settings, IsInitialized);
            // if (m_messageQueue != null)
            // {
            //     foreach (var message in m_messageQueue)
            //         GUILayout.Label(message.ToString());
            // }
        }
#endif

        private void InitGuard()
        {
            if (!IsInitialized)
                throw new InvalidOperationException("StateMachine is not initialized");
        }

        private bool StartMessageReceiving(out bool sameThread)
        {
            int threadId = Thread.CurrentThread.ManagedThreadId;
            int origThread = Interlocked.CompareExchange(ref m_messageReceivingThread, threadId, 0);
            sameThread = origThread == threadId;
            return (origThread == 0);
        }

        private void StopMessageReceiving()
        {
            m_messageReceivingThread = 0;
        }

        public class InitializationException : Exception
        {
            public InitializationException() { }
            public InitializationException(string message) : base(message) { }
            public InitializationException(string message, Exception innerException) : base(message, innerException) { }
        }

    }

}