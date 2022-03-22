#if UNITY_5_3_OR_NEWER
#define UNITY
using UnityEngine;
#endif

using System.Collections.Generic;
using System;
using Debug = System.Diagnostics.Debug;


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
    public interface IState<TArg> : IStateBase { void Enter(TArg arg); };

    internal static class StateExtensions
    {
        public static void StateEnter(this IState state)
        {
            state.state.EnterBegin();
            state.Enter();
            state.state.EnterEnd();
        }

        public static void StateEnter<Targ>(this IState<Targ> state, Targ arg)
        {
            state.state.EnterBegin();
            state.Enter(arg);
            state.state.EnterEnd();
        }
    }

    public abstract class State : IStateBase
    {
        State IStateBase.state { get { return this; } }

#if UNITY
        public abstract void DrawGUI(StateMachine.GUISettings settings, bool isActive);
        protected virtual void OnGUI() { }

        public static StateGUIScope StateGUI(State state, StateMachine.GUISettings settings, bool isActive)
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

        public struct StateGUIScope : IDisposable
        {
            public State state;
            public StateMachine.GUISettings settings;
            public bool isActive;

            public void Initialize()
            {
                var origColor = GUI.color;
                GUI.color = settings.GetColor(isActive);
                GUILayout.BeginVertical(state.name, "Window");
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

        protected abstract State parentState { get; }

        public string name { get { return GetType().Name; } }

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
            var state = this;
            var propagateUntil = m_hasTopState.propagateUntil;
            var sm = stateMachine;

            while (true)
            {
                if (state is TReceiver receiver)
                {
                    sm.m_messageConsumed = true;
                    handler(receiver);
                    if (sm.m_messageConsumed)
                        return;
                }

                if (state == propagateUntil) break;
                state = state.parentState;
            }
        }

        internal virtual void ReceiveMessage<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg)
        {
            var state = this;
            var propagateUntil = m_hasTopState.propagateUntil;
            var sm = stateMachine;

            while (true)
            {
                if (state is TReceiver receiver)
                {
                    sm.m_messageConsumed = true;
                    handler(receiver, arg);
                    if (sm.m_messageConsumed)
                        return;
                }

                if (state == propagateUntil) break;
                state = state.parentState;
            }
        }

        internal virtual void Initialize(StateMachine machine, IHasTopState _hasTopState)
        {
            m_hasTopState = _hasTopState;
            stateMachine = machine;
            OnInitialize();
        }

        internal virtual void Shutdown()
        {
            OnShutdown();
            m_hasTopState = null;
            stateMachine = null;
        }

        protected virtual void OnInitialize() { }
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

            if (m_hasTopState.propagateUntil == this)
                m_hasTopState.topState = m_hasTopState.propagateUntil;
            else
                m_hasTopState.topState = parentState;
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
        internal override void Initialize(StateMachine machine, IHasTopState _hasTopState)
        {
            actor = machine.actor as TActor;
            base.Initialize(machine, _hasTopState);
        }


        private TParent m_parent;
        public TParent parent
        {
            get { return m_parent; }
            set
            {
                if (stateMachine != null)
                    throw new System.InvalidOperationException("Cannot set parent of a state that's been initialized");
                m_parent?.RemoveChild(this);
                m_parent = value;
                m_parent?.AddChild(this);
            }
        }

        override protected State parentState { get { return parent?.state; } }
    }

    public interface IParentState : IStateBase
    {
        void AddChild(State child);
        void RemoveChild(State child);
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

        internal override sealed void Initialize(StateMachine machine, IHasTopState _hasTopState)
        {
            base.Initialize(machine, _hasTopState);
        }

        internal override sealed void Shutdown()
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

    public class HierarchicalState<TActor> : HierarchicalState<TActor, IParentState> where TActor : class { }

    public class HierarchicalState<TActor, TParent> : State<TActor, TParent>, IParentState, StateMachine.ITransition where TParent : IParentState where TActor : class
    {
        private State m_activeSubState;
        public IState entrySubState { get; set; }

        private readonly List<State> m_childStates = new List<State>();

        public int ChildCount => m_childStates.Count;
        public State GetChild(int index) => m_childStates[index];

        void IParentState.AddChild(State child)
        {
            m_childStates.Add(child);
        }

        void IParentState.RemoveChild(State child)
        {
            m_childStates.Remove(child);
        }

        void StateMachine.ITransition.TransitTo(IState state)
        {
            if (/*state != m_activeSubState && */m_childStates.Contains(state.state))
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
            if (/*state != m_activeSubState && */m_childStates.Contains(state.state))
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
            m_activeSubState = entrySubState?.state;
        }

        internal sealed override void EnterEnd()
        {
            base.EnterEnd();
            entrySubState?.StateEnter();
        }

        internal sealed override void Exit()
        {
            m_activeSubState?.Exit();
            OnExit();
            base.Exit();
        }

        internal override sealed void Initialize(StateMachine machine, IHasTopState _hasTopState)
        {
            base.Initialize(machine, _hasTopState);
            foreach (var child in m_childStates)
            {
                child.Initialize(machine, _hasTopState);
            }
        }

        internal override sealed void Shutdown()
        {
            foreach (var child in m_childStates)
            {
                child.Shutdown();
            }
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


    public class OrthogonalState<TActor> : OrthogonalState<TActor, IParentState> where TActor : class { }

    public class OrthogonalState<TActor, TParent> : State<TActor, TParent>, IParentState where TParent : IParentState where TActor : class
    {
        private class OrthogonalBranch : IHasTopState
        {
            public State topState { get; set; }
            public State propagateUntil { get; set; }
        }

        private OrthogonalBranch[] m_branches;
        private List<IState> m_childStates = new List<IState>();

        public int ChildCount => m_childStates.Count;
        public IState GetChild(int index) => m_childStates[index];

        void IParentState.AddChild(State child)
        {
            if (!(child is IState istate))
                throw new System.ArgumentException("OrthogonalState's children must implement IState");
            m_childStates.Add(istate);
        }

        void IParentState.RemoveChild(State child)
        {
            if (!(child is IState istate))
                throw new System.ArgumentException("OrthogonalState's children must implement IState");
            m_childStates.Remove(istate);
        }

#if UNITY
        public sealed override void DrawGUI(StateMachine.GUISettings settings, bool isActive)
        {
            using (StateGUI(this, settings, isActive))
            {
                using (new GUILayout.HorizontalScope())
                {
                    foreach (var child in m_childStates)
                        child.state.DrawGUI(settings, isActive);
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
            foreach (var substate in m_childStates)
                substate.StateEnter();
        }

        internal sealed override void Exit()
        {
            foreach (var substate in m_childStates)
                substate.state.Exit();
            OnExit();
            base.Exit();
        }

        internal override void Initialize(StateMachine machine, IHasTopState _hasTopState)
        {
            base.Initialize(machine, _hasTopState);

            m_branches = new OrthogonalBranch[m_childStates.Count];

            for (int i = 0; i < m_childStates.Count; ++i)
            {
                var branch = new OrthogonalBranch();
                var child = m_childStates[i];
                branch.propagateUntil = child.state;
                m_branches[i] = branch;
                child.state.Initialize(machine, branch);
            }
        }


        internal override sealed void Shutdown()
        {
            for (int i = 0; i < m_branches.Length; ++i)
            {
                m_branches[i].topState.Shutdown();
            }

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