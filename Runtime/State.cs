#nullable enable

#if UNITY_5_3_OR_NEWER && DEBUG
#define UNITY_PROFILING
#endif

#if UNITY_PROFILING
using UnityEngine.Profiling;
#endif

using System;
using System.Collections.Generic;

namespace SeweralIdeas.StateMachines
{
    /// <summary>
    /// Marker base implemented by every state. Useful as a generic constraint when you want
    /// "any state" without committing to a specific entry shape (<see cref="IState"/> /
    /// <see cref="IState{TArg}"/>).
    /// </summary>
    public interface IStateBase { State state { get; } };

    /// <summary>
    /// A state that can be the target of a transition without an argument
    /// (<c>TransitTo(someState)</c>). <see cref="Enter"/> has a default empty body via a
    /// default-interface-method, so concrete states only need to define it when they have
    /// entry-time work to do; an explicit empty <c>void IState.Enter() { }</c> stub is
    /// unnecessary. A state may implement both <see cref="IState"/> and one or more
    /// <see cref="IState{TArg}"/> if it is reachable via either kind of transition.
    /// </summary>
    public interface IState : IStateBase { void Enter() {} };

    /// <summary>
    /// A state that can be the target of a transition with a typed argument
    /// (<c>TransitTo(someState, arg)</c>). The <see cref="Enter(TArg)"/> implementation
    /// receives the argument before <c>OnEnter</c> runs. A single state class may implement
    /// multiple <see cref="IState{TArg}"/> with different <typeparamref name="TArg"/> types
    /// to accept different transition payloads.
    /// </summary>
    /// <typeparam name="TArg">Type of the transition argument</typeparam>
    public interface IState<in TArg> : IStateBase { void Enter(TArg arg); };

    /// <summary>
    /// Marker implemented by composite states (<see cref="HierarchicalState{TActor}"/> and
    /// <see cref="OrthogonalState{TActor}"/>). Used as the <c>TParent</c> generic constraint
    /// on <c>SimpleState&lt;TActor, TParent&gt;</c> and friends, giving child states a
    /// strongly-typed <c>parent</c> reference.
    /// </summary>
    public interface IParentState : IStateBase { }

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

        internal State() { }

        protected static bool Contains<T>(T[] states, T state) where T : class
        {
            for (int i = 0; i < states.Length; ++i)
            {
                if (ReferenceEquals(states[i], state))
                    return true;
            }

            return false;
        }

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
            var propagateUntil = _hasTopState.rootState;
            var sm = stateMachine;

            while (true)
            {
                if (iterState is TReceiver receiver)
                {
                    sm._messageConsumed = true;
#if UNITY_PROFILING
                    Profiler.BeginSample(GetType().FullName);
#endif
                    handler(receiver);
#if UNITY_PROFILING
                    Profiler.EndSample();
#endif
                    if (sm._messageConsumed)
                        return;
                }

                if (iterState == propagateUntil) break;
                iterState = iterState.parentState.state;
            }
        }

        internal virtual void ReceiveMessage<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg)
        {
            var iterState = this;
            var propagateUntil = _hasTopState.rootState;
            var sm = stateMachine;

            while (true)
            {
                if (iterState is TReceiver receiver)
                {
                    sm._messageConsumed = true;
#if UNITY_PROFILING
                    Profiler.BeginSample(GetType().FullName);
#endif
                    handler(receiver, arg);
#if UNITY_PROFILING
                    Profiler.EndSample();
#endif
                    if (sm._messageConsumed)
                        return;
                }

                if (iterState == propagateUntil) break;
                iterState = iterState.parentState.state;
            }
        }

        internal virtual void Initialize(in StateMachine.InitContext context, IHasTopState hasTopState)
        {
            _hasTopState = hasTopState;
            stateMachine = context.stateMachine;
            //OnInitialize();
        }

        internal virtual void Shutdown()
        {
            OnShutdown();
            _hasTopState = null!;
            stateMachine = null!;
            parentState = null!;
        }

        protected virtual void OnShutdown() { }

        internal virtual void EnterBegin()
        {
            _hasTopState.topState = this;

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

            if (_hasTopState.rootState == this)
                _hasTopState.topState = _hasTopState.rootState.state;
            else
                _hasTopState.topState = parentState.state;
        }

        protected virtual void OnEnter() { }
        protected virtual void OnExit() { }

        public StateMachine stateMachine { get; private set; } = null!;
        private IHasTopState _hasTopState = null!;

        public static implicit operator bool(State? state)
        {
            return state != null;
        }

        public void PropagateMessage()
        {
            stateMachine._messageConsumed = false;
        }

        // Walk-protocol overrides. Override in HierarchicalState and OrthogonalState; SimpleState
        // inherits the no-children defaults below. Used by StateMachine.Walk().
        internal virtual int WalkChildCount(WalkMode mode) => 0;
        internal virtual State WalkChild(int index, WalkMode mode)
            => throw new InvalidOperationException($"State {GetType().Name} has no walk children");
        internal virtual bool WalkIsChildActive(State child) => false;
    }

    public abstract class State<TActor, TParent> : State where TParent : IParentState where TActor : class
    {
        private TActor _actor = null!;
        public TActor actor
        {
            get
            {
                AssertInitialized();
                return _actor;
            }
            private set => _actor = value;
        }

        [System.Diagnostics.Conditional("DEBUG")]
        private void AssertInitialized()
        {
            if (((object?)stateMachine) is null)
                throw new InvalidOperationException(
                    $"Cannot access actor on state {GetType().Name}: state machine is not initialized " +
                    "(either Initialize has not been called, or Shutdown has run). " +
                    "States must not access actor from their constructors — use OnInitialize instead.");
        }

        internal State()
        {
        }

        internal override void Initialize(in StateMachine.InitContext context, IHasTopState hasTopState)
        {
            actor = (context.stateMachine.actor as TActor)!;
            base.Initialize(context, hasTopState);
        }


        private TParent _parent = default!;
        public TParent parent => _parent;

        internal override IParentState parentState
        {
            get => _parent;
            set => _parent = (TParent)value;
        }
    }

    public class SimpleState<TActor> : SimpleState<TActor, IParentState> where TActor : class { }

    public class SimpleState<TActor, TParent> : State<TActor, TParent> where TParent : IParentState where TActor : class
    {

        internal override sealed void EnterBegin()
        {
            base.EnterBegin();
        }

        internal override sealed void EnterEnd()
        {
            base.EnterEnd();
        }

        internal override sealed void Exit()
        {
            OnExit();
            base.Exit();
        }

        internal override sealed void Initialize(in StateMachine.InitContext context, IHasTopState hasTopState)
        {
            base.Initialize(context, hasTopState);
            OnInitialize();
        }
        
        protected virtual void OnInitialize() { }

        internal override sealed void Shutdown()
        {
            base.Shutdown();
        }
    }

    public abstract class HierarchicalState<TActor> : HierarchicalState<TActor, IParentState> where TActor : class
    {
    }

    public abstract class HierarchicalState<TActor, TParent> : State<TActor, TParent>, IParentState, StateMachine.ITransition where TParent : IParentState where TActor : class
    {
        private State? _activeSubState;
        private IState? _entrySubState;

        private State[]? _childStates = null;

        public int ChildCount => _childStates!.Length;
        public State GetChild(int index) => _childStates![index];

        void StateMachine.ITransition.TransitTo(IState state)
        {
            if (Contains(_childStates!, state.state))
            {
                _activeSubState?.Exit();
                _activeSubState = state.state;
                state.StateEnter();
                return;
            }
            PropagateMessage();
        }

        void StateMachine.ITransition.TransitTo<TArg>(IState<TArg> state, TArg arg)
        {
            if (Contains(_childStates!, state.state))
            {
                _activeSubState?.Exit();
                _activeSubState = state.state;
                state.StateEnter(arg);
                return;
            }
            PropagateMessage();
        }


        internal override sealed void EnterBegin()
        {
            base.EnterBegin();
            _activeSubState = _entrySubState?.state;
        }

        internal override sealed void EnterEnd()
        {
            base.EnterEnd();
            _entrySubState?.StateEnter();
        }

        internal override sealed void Exit()
        {
            _activeSubState?.Exit();
            OnExit();
            base.Exit();
        }

        internal override sealed void Initialize(in StateMachine.InitContext context, IHasTopState hasTopState)
        {
            base.Initialize(context, hasTopState);
            var childStates = context.iBaseStates;
            OnInitialize(out _entrySubState, childStates);

            foreach (var s in childStates)
            {
                if (s is null)
                    throw new StateMachine.InitializationException($"ChildStates of {GetType()} cannot be null");
            }

            bool addEntrySubState = _entrySubState != null && !childStates.Contains(_entrySubState);
            if (addEntrySubState)
            {
                childStates.Add(_entrySubState!);
            }

            _childStates = new State[childStates.Count];

            for (int i = 0; i < childStates.Count; ++i)
            {
                var child = childStates[i].state;

                if (child.parentState != null)
                {
                    throw new StateMachine.InitializationException($"Cannot add state {child.GetType()} as a child of {GetType()}. already has a parent");
                }

                child.parentState = this;
                _childStates[i] = child;
            }

            childStates.Clear();

            foreach (var child in _childStates)
            {
                child.Initialize(context, hasTopState);
            }
        }

        /// <summary>
        /// Declare this hierarchical state's child states.
        /// </summary>
        /// <param name="entrySubState">The child to enter by default. Set to <c>null</c> to
        /// defer the entry decision to runtime, then call <c>TransitTo(...)</c> from inside
        /// <see cref="IState.Enter"/> to pick a child based on context.</param>
        /// <param name="subStates">Add every child state to this list.</param>
        protected abstract void OnInitialize(out IState? entrySubState, List<IStateBase> subStates);

        internal override sealed void Shutdown()
        {
            foreach (var child in _childStates!)
            {
                child.Shutdown();
            }

            _childStates = null!;
            base.Shutdown();
        }

        internal override sealed int WalkChildCount(WalkMode mode)
        {
            if (_childStates is null)
                return 0;
            if (mode == WalkMode.ActiveOnly)
                return _activeSubState != null ? 1 : 0;
            return _childStates.Length;
        }

        internal override sealed State WalkChild(int index, WalkMode mode)
        {
            if (mode == WalkMode.ActiveOnly)
                return _activeSubState!;
            return _childStates![index];
        }

        internal override sealed bool WalkIsChildActive(State child) => ReferenceEquals(_activeSubState, child);
    }


    public abstract class OrthogonalState<TActor> : OrthogonalState<TActor, IParentState> where TActor : class
    {
    }

    public abstract class OrthogonalState<TActor, TParent> : State<TActor, TParent>, IParentState where TParent : IParentState where TActor : class
    {
        private class OrthogonalBranch : IHasTopState
        {
            public State? topState { get; set; }
            public IState rootState { get; set; } = null!;
        }

        private OrthogonalBranch[]? _branches = null;

        public int ChildCount => _branches!.Length;

        internal override sealed void EnterBegin()
        {
            base.EnterBegin();
        }

        internal override sealed void EnterEnd()
        {
            base.EnterEnd();
            foreach (var subState in _branches!)
                subState.rootState.StateEnter();
        }

        internal override sealed void Exit()
        {
            foreach (var subState in _branches!)
                subState.rootState.state.Exit();
            OnExit();
            base.Exit();
        }

        internal override void Initialize(in StateMachine.InitContext context, IHasTopState hasTopState)
        {
            base.Initialize(context, hasTopState);
            var childStates = context.iStates;
            OnInitialize(childStates);

            foreach (var s in childStates)
            {
                if (s is null)
                    throw new StateMachine.InitializationException("ChildStates of OrthogonalState cannot be null");
            }

            _branches = new OrthogonalBranch[childStates.Count];

            for (int i = 0; i < childStates.Count; ++i)
            {
                var child = childStates[i];
                var childState = child.state;

                if (childState.parentState != null)
                {
                    throw new StateMachine.InitializationException("State already has a parent");
                }

                childState.parentState = this;
                var branch = new OrthogonalBranch { rootState = child };
                _branches[i] = branch;
            }

            childStates.Clear();

            foreach(var branch in _branches)
            {
                branch.rootState.state.Initialize(context, branch);
            }
        }

        /// <summary>
        /// Declare this orthogonal state's parallel child branches. All listed children run
        /// at the same time, each with its own active-state pointer.
        /// </summary>
        protected abstract void OnInitialize(List<IState> subStates);

        internal override sealed void Shutdown()
        {
            if (_branches != null)
            {
                for (int i = 0; i < _branches.Length; ++i)
                {
                    _branches[i].topState?.Shutdown();
                }
            }

            _branches = null!;
            base.Shutdown();
        }

        internal override sealed int WalkChildCount(WalkMode mode) => _branches?.Length ?? 0;

        internal override sealed State WalkChild(int index, WalkMode mode)
            => _branches![index].rootState.state;

        // Every branch of an active orthogonal state is itself active; the active descent within
        // each branch is handled recursively by that branch's own WalkChildCount/WalkChild.
        internal override sealed bool WalkIsChildActive(State child) => true;

        internal override sealed void ReceiveMessage<TReceiver>(Handler<TReceiver> handler)
        {
            var anyBranchConsumed = false;
            foreach (var branch in _branches!)
            {
                stateMachine._messageConsumed = false;
                branch.topState!.ReceiveMessage(handler);
                anyBranchConsumed |= stateMachine._messageConsumed;
            }

            if (!anyBranchConsumed)
                base.ReceiveMessage(handler);
        }

        internal override sealed void ReceiveMessage<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg)
        {
            var anyBranchConsumed = false;
            foreach (var branch in _branches!)
            {
                stateMachine._messageConsumed = false;
                branch.topState!.ReceiveMessage(handler, arg);
                anyBranchConsumed |= stateMachine._messageConsumed;
            }

            if (!anyBranchConsumed)
                base.ReceiveMessage(handler, arg);
        }
    }


}