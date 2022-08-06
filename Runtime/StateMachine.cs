#if UNITY_5_3_OR_NEWER
#define UNITY
using System;
using System.Collections.Generic;
using UnityEngine;
#endif

namespace SeweralIdeas.StateMachines
{
    
    internal interface IHasTopState
    {
        State topState { get; set; }
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

        public object actor { get; private set; }

        private readonly IState m_rootState;
        private readonly Action<string> m_debugLog;
        private readonly Queue<Message> m_messageQueue = new Queue<Message>();
        private readonly Queue<Message> m_transitionQueue = new Queue<Message>();
        
        private State m_topState;
        private bool m_receivingMessages;

        internal bool m_messageConsumed;
        public LogFlags logFlags = 0;
        public readonly string Name;

        State IHasTopState.topState
        {
            get => m_topState;
            set => m_topState = value;
        }

        IState IHasTopState.rootState => m_rootState;

        public void WriteLine(string text)
        {
            m_debugLog(text);
        }

        public bool IsInitialized => InitializationState is InitState.Initialized or InitState.ShuttingDown;

        public InitState InitializationState { get; private set; }

        [Flags]
        public enum LogFlags
        {
            None = 0,
            EnterExit = 1 << 0,
        }

        public StateMachine(string name, IState rootState, Action<string> debugLog = null)
        {
            m_debugLog = debugLog ?? Console.WriteLine;
            Name = name;
            m_rootState = rootState;
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
            m_messageQueue.Clear();
            m_transitionQueue.Clear();

            try
            {
                var context = new InitContext()
                {
                    stateMachine = this,
                    iStates = new List<IState>(),
                    iBaseStates = new List<IStateBase>()
                };

                m_rootState.state.Initialize(context, this);
            }
            catch
            {
                m_rootState?.state?.Shutdown();
                m_messageQueue.Clear();
                m_transitionQueue.Clear();
                this.actor = null;
                InitializationState = InitState.Offline;
                throw;
            }

            InitializationState = InitState.Initialized;

            try
            {
                if (!StartMessageReceiving())
                {
                    throw new InvalidProgramException("This should not ever happen..?");
                }
                
                m_messageConsumed = false;
                m_rootState.StateEnter();
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

            m_messageConsumed = false;
            m_rootState.state.Exit();

            m_rootState.state.Shutdown();
            
            Debug.Assert(m_messageQueue.Count == 0);
            Debug.Assert(m_transitionQueue.Count == 0);
            m_messageQueue.Clear();
            m_transitionQueue.Clear();
            InitializationState = InitState.Offline;
        }


        private static Handler<ITransition, IState> msg_transition = (handler, dest) =>
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
            var handler = msg_transition;

            if (StartMessageReceiving())
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

        internal void TransitTo<TArg>(IState<TArg> destination, TArg arg)
        {
            InitGuard();
            var handler = TransitionHandler<TArg>.msg_transition;

            if (StartMessageReceiving())
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
                m_transitionQueue.Enqueue(
                    Message<ITransition, (IState<TArg>, TArg)>.Create(handler, (destination, arg)));
                HandleMessagesInternal();
            }
        }

        public void SendMessage<TReceiver>(Handler<TReceiver> handler) where TReceiver : class
        {
            InitGuard();

            if (StartMessageReceiving())
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
                m_messageQueue.Enqueue(Message<TReceiver>.Create(handler));
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
                m_messageQueue.Enqueue(Message<TReceiver, TArg>.Create(handler, arg));
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

        private bool StartMessageReceiving()
        {
            bool wasNotReceiving = !m_receivingMessages;
            m_receivingMessages = true;
            return wasNotReceiving;
        }

        private void StopMessageReceiving()
        {
            m_receivingMessages = false;
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