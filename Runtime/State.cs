using System;
#if UNITY_5_3_OR_NEWER
#define UNITY
using UnityEngine;
#endif


namespace SeweralIdeas.StateMachines
{
    /// <summary>
    /// Every state must implement this interface
    /// </summary>
    public interface IStateBase { State state { get; } };
    /// <summary>
    /// State that implements this interface can be transited to without arguments
    /// </summary>
    public interface IState : IStateBase { void Enter(); };

    /// <summary>
    /// State that implements this interface can be transited to with a specified generic argument
    /// </summary>
    /// <typeparam name="TArg">Type of the transition argument</typeparam>
    public interface IState<in TArg> : IStateBase
    {
        void Enter(TArg arg);
    };

    public interface IParentState : IStateBase
    {
    }

    public static class StateExtensions
    {
        internal static void StateEnter(this IState state)
        {
            state.state.EnterBegin();
            state.Enter();
            state.state.EnterEnd();
        }

        internal static void StateEnter<TArg>(this IState<TArg> state, TArg arg)
        {
            state.state.EnterBegin();
            state.Enter(arg);
            state.state.EnterEnd();
        }
    }

    public abstract class State : IStateBase
    {
        State IStateBase.state => this;

        internal State()
        {
            
        }

        protected static bool Contains(IStateBase[] states, IStateBase state)
        {
            for (int i = 0; i < states.Length; ++i)
            {
                if (ReferenceEquals(states[i], state))
                    return true;
            }

            return false;
        }

#if UNITY
        public abstract void DrawGUI(StateMachine.GUISettings settings, bool isActive);
        protected virtual void OnGUI() { }

        protected static StateGUIScope StateGUI(State state, StateMachine.GUISettings settings, bool isActive)
        {
            var scope = new StateGUIScope
            {
                settings = settings,
                isActive = isActive,
                state = state
            };
            scope.Initialize();
            return scope;
        }

        protected struct StateGUIScope : IDisposable
        {
            public State state;
            public StateMachine.GUISettings settings;
            public bool isActive;

            public void Initialize()
            {
                var origColor = GUI.color;
                GUI.color = settings.GetColor(isActive);
                GUILayout.BeginVertical(state.name, "Window", GUILayout.ExpandHeight(true));
                GUI.color = origColor;

                state.OnGUI();

                if (settings.fieldsMode != StateMachine.GUISettings.FieldsMode.None)
                {
                    var info = StateDebugInfo.Get(state.GetType());
                    for (int i = 0; i < info.Count; ++i)
                    {
                        var field = info[i];
                        if(field.show || settings.fieldsMode == StateMachine.GUISettings.FieldsMode.AllFields)
                            GUILayout.Label($"{field.fieldInfo.Name}: \t{field.fieldInfo.GetValue(state)}");
                    }
                }
            }

            public void Dispose()
            {
                GUILayout.EndVertical();
            }
        }
#endif

        internal abstract IParentState parentState { get; set; }

        public string name => GetType().Name;

        protected void TransitTo(IState destination)
        {
            stateMachine.TransitTo(destination);
        }

        protected void TransitTo<TArg>(IState<TArg> destination, TArg arg)
        {
            stateMachine.TransitTo(destination, arg);
        }

        internal virtual void ReceiveMessage<TReceiver>(Handler<TReceiver> handler)
        {
            var iterState = this;
            var propagateUntil = m_hasTopState.rootState;
            var sm = stateMachine;

            while (true)
            {
                if (iterState is TReceiver receiver)
                {
                    sm.m_messageConsumed = true;
                    handler(receiver);
                    if (sm.m_messageConsumed)
                        return;
                }

                if (iterState == propagateUntil) break;
                iterState = iterState.parentState.state;
            }
        }

        internal virtual void ReceiveMessage<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg)
        {
            var iterState = this;
            var propagateUntil = m_hasTopState.rootState;
            var sm = stateMachine;

            while (true)
            {
                if (iterState is TReceiver receiver)
                {
                    sm.m_messageConsumed = true;
                    handler(receiver, arg);
                    if (sm.m_messageConsumed)
                        return;
                }

                if (iterState == propagateUntil) break;
                iterState = iterState.parentState.state;
            }
        }

        internal virtual void Initialize(StateMachine machine, IHasTopState hasTopState)
        {
            m_hasTopState = hasTopState;
            stateMachine = machine;
            //OnInitialize();
        }

        internal virtual void Shutdown()
        {
            OnShutdown();
            m_hasTopState = null;
            stateMachine = null;
            parentState = null;
        }

        protected virtual void OnShutdown() { }

        internal virtual void EnterBegin()
        {
            m_hasTopState.topState = this;

            //Debug.Assert(stateMachine.receivingMessage);
            if (stateMachine.logFlags.HasFlag(StateMachine.LogFlags.EnterExit))
            {
                stateMachine.WriteLine($"{stateMachine.Name} entering {name}");
            }

        }

        internal virtual void EnterEnd()
        {
            OnEnter();
        }

        internal virtual void Exit()
        {
            //Debug.Assert(stateMachine.receivingMessage);
            if (stateMachine.logFlags.HasFlag(StateMachine.LogFlags.EnterExit))
            {
                stateMachine.WriteLine($"{stateMachine.Name} exiting {name}");
            }

            if (m_hasTopState.rootState == this)
                m_hasTopState.topState = m_hasTopState.rootState.state;
            else
                m_hasTopState.topState = parentState.state;
        }

        protected virtual void OnEnter() { }
        protected virtual void OnExit() { }

        public StateMachine stateMachine { get; private set; }
        private IHasTopState m_hasTopState;

        public static implicit operator bool(State state)
        {
            return state != null;
        }

        public void PropagateMessage()
        {
            stateMachine.m_messageConsumed = false;
        }
    }

    public abstract class State<TActor, TParent> : State where TParent : IParentState where TActor : class
    {
        public TActor actor { get; private set; }

        internal State()
        {
        }
        
        internal override void Initialize(StateMachine machine, IHasTopState hasTopState)
        {
            actor = machine.actor as TActor;
            base.Initialize(machine, hasTopState);
        }


        private TParent m_parent;
        public TParent parent => m_parent;

        internal override IParentState parentState
        {
            get => m_parent;
            set => m_parent = (TParent)value;
        }
    }

    public class SimpleState<TActor> : SimpleState<TActor, IParentState> where TActor : class { }

    public class SimpleState<TActor, TParent> : State<TActor, TParent> where TParent : IParentState where TActor : class
    {

        internal sealed override void EnterBegin()
        {
            base.EnterBegin();
        }

        internal sealed override void EnterEnd()
        {
            base.EnterEnd();
        }

        internal sealed override void Exit()
        {
            OnExit();
            base.Exit();
        }

        internal sealed override void Initialize(StateMachine machine, IHasTopState hasTopState)
        {
            base.Initialize(machine, hasTopState);
            OnInitialize();
        }
        
        protected virtual void OnInitialize() { }

        internal sealed override void Shutdown()
        {
            base.Shutdown();
        }

#if UNITY
        public sealed override void DrawGUI(StateMachine.GUISettings settings, bool isActive)
        {
            using (StateGUI(this, settings, isActive))
            {             
            }
        }
#endif

    }

    public abstract class HierarchicalState<TActor> : HierarchicalState<TActor, IParentState> where TActor : class
    {
    }

    public abstract class HierarchicalState<TActor, TParent> : State<TActor, TParent>, IParentState, StateMachine.ITransition where TParent : IParentState where TActor : class
    {
        private State m_activeSubState;
        private IState m_entrySubState;

        private State[] m_childStates;

        public int ChildCount => m_childStates.Length;
        public State GetChild(int index) => m_childStates[index];

        void StateMachine.ITransition.TransitTo(IState state)
        {
            if (Contains(m_childStates, state.state))
            {
                m_activeSubState?.Exit();
                m_activeSubState = state.state;
                state.StateEnter();
                return;
            }
            PropagateMessage();
        }

        void StateMachine.ITransition.TransitTo<TArg>(IState<TArg> state, TArg arg)
        {
            if (Contains(m_childStates, state.state))
            {
                m_activeSubState?.Exit();
                m_activeSubState = state.state;
                state.StateEnter(arg);
                return;
            }
            PropagateMessage();
        }


        internal sealed override void EnterBegin()
        {
            base.EnterBegin();
            m_activeSubState = m_entrySubState?.state;
        }

        internal sealed override void EnterEnd()
        {
            base.EnterEnd();
            m_entrySubState?.StateEnter();
        }

        internal sealed override void Exit()
        {
            m_activeSubState?.Exit();
            OnExit();
            base.Exit();
        }

        internal sealed override void Initialize(StateMachine machine, IHasTopState hasTopState)
        {
            base.Initialize(machine, hasTopState);

            OnInitialize(out m_entrySubState, out IStateBase[] childStates);

            if (childStates == null || Array.IndexOf(childStates, null) != -1)
            {
                throw new StateMachine.InitializationException($"ChildStates of {GetType()} cannot be null");
            }
            
            bool addEntrySubstate = m_entrySubState != null && !Contains(childStates, m_entrySubState);

            int offset;
            if (addEntrySubstate)
            {
                m_childStates = new State[childStates.Length + 1];
                m_childStates[0] = m_entrySubState.state;
                offset = 1;
            }
            else
            {
                m_childStates = new State[childStates.Length];
                offset = 0;
            }

            for (int i = 0; i < childStates.Length; ++i)
            {
                var child = childStates[i].state;
                
                if (child.parentState != null)
                {
                    throw new StateMachine.InitializationException($"Cannot add state {child.GetType()} as a child of {GetType()}. already has a parent");
                }

                child.parentState = this;
                m_childStates[i + offset] = child;
            }
            
            foreach (var child in m_childStates)
            {
                child.Initialize(machine, hasTopState);
            }
        }

        protected abstract void OnInitialize(out IState entrySubState, out IStateBase[] subStates);

        internal sealed override void Shutdown()
        {
            foreach (var child in m_childStates)
            {
                child?.Shutdown();
            }

            m_childStates = null;
            base.Shutdown();
        }

#if UNITY
        public sealed override void DrawGUI(StateMachine.GUISettings settings, bool isActive)
        {
            using (StateGUI(this, settings, isActive))
            {
                using (new GUILayout.HorizontalScope())
                {
                    foreach (var child in m_childStates)
                        child.DrawGUI(settings, isActive && child == m_activeSubState);
                }
            }
        }
#endif
    }


    public abstract class OrthogonalState<TActor> : OrthogonalState<TActor, IParentState> where TActor : class
    {
    }

    public abstract class OrthogonalState<TActor, TParent> : State<TActor, TParent>, IParentState where TParent : IParentState where TActor : class
    {
        private class OrthogonalBranch : IHasTopState
        {
            public State topState { get; set; }
            public IState rootState { get; set; }
        }

        private OrthogonalBranch[] m_branches;

        public int ChildCount => m_branches.Length;

#if UNITY
        public sealed override void DrawGUI(StateMachine.GUISettings settings, bool isActive)
        {
            using (StateGUI(this, settings, isActive))
            {
                using (new GUILayout.HorizontalScope())
                {
                    foreach (var child in m_branches)
                        child.rootState.state.DrawGUI(settings, isActive);
                }
            }
        }
#endif

        internal sealed override void EnterBegin()
        {
            base.EnterBegin();
        }

        internal sealed override void EnterEnd()
        {
            base.EnterEnd();
            foreach (var subState in m_branches)
                subState.rootState.StateEnter();
        }

        internal sealed override void Exit()
        {
            foreach (var subState in m_branches)
                subState.rootState.state.Exit();
            OnExit();
            base.Exit();
        }

        internal override void Initialize(StateMachine machine, IHasTopState hasTopState)
        {
            base.Initialize(machine, hasTopState);

            OnInitialize(out var childStates);
            
            if (childStates == null || Array.IndexOf(childStates, null) != -1)
            {
                throw new StateMachine.InitializationException("ChildStates of OrthogonalState cannot be null");
            }
            
            m_branches = new OrthogonalBranch[childStates.Length];

            for (int i = 0; i < childStates.Length; ++i)
            {
                var child = childStates[i];
                var childState = child.state;
                
                if (childState.parentState != null)
                {
                    throw new StateMachine.InitializationException("State already has a parent");
                }
                
                childState.parentState = this;
                var branch = new OrthogonalBranch();
                branch.rootState = child;
                m_branches[i] = branch;
            }

            foreach(var branch in m_branches)
            {
                branch.rootState.state.Initialize(machine, branch);
            }
        }

        protected abstract void OnInitialize(out IState[] childStates);

        internal sealed override void Shutdown()
        {
            if (m_branches != null)
            {
                for (int i = 0; i < m_branches.Length; ++i)
                {
                    m_branches[i]?.topState?.Shutdown();
                }
            }

            m_branches = null;
            base.Shutdown();
        }

        internal sealed override void ReceiveMessage<TReceiver>(Handler<TReceiver> handler)
        {
            var anyBranchConsumed = false;
            foreach (var branch in m_branches)
            {
                stateMachine.m_messageConsumed = false;
                branch.topState.ReceiveMessage(handler);
                anyBranchConsumed |= stateMachine.m_messageConsumed;
            }

            if (!anyBranchConsumed)
                base.ReceiveMessage(handler);
        }

        internal sealed override void ReceiveMessage<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg)
        {
            var anyBranchConsumed = false;
            foreach (var branch in m_branches)
            {
                stateMachine.m_messageConsumed = false;
                branch.topState.ReceiveMessage(handler, arg);
                anyBranchConsumed |= stateMachine.m_messageConsumed;
            }

            if (!anyBranchConsumed)
                base.ReceiveMessage(handler, arg);
        }
    }


}