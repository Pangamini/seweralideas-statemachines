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
    public interface IStateBase { State State { get; } }

    /// <summary>
    /// A state that can be the target of a transition without an argument
    /// (<c>TransitTo(someState)</c>). <see cref="Enter"/> has a default empty body via a
    /// C# default-interface-method, so concrete states only need to define it when they have
    /// entry-time work to do.
    /// </summary>
    public interface IState : IStateBase { void Enter() {} }

    /// <summary>
    /// A state that can be the target of a transition with a typed argument
    /// (<c>TransitTo(someState, arg)</c>). The <see cref="Enter(TArg)"/> implementation
    /// receives the argument before <c>OnEnter</c> runs. A single state class may implement
    /// multiple <see cref="IState{TArg}"/> with different <typeparamref name="TArg"/> types
    /// to accept different transition payloads.
    /// </summary>
    public interface IState<in TArg> : IStateBase { void Enter(TArg arg); }

    public static class StateExtensions
    {
        internal static void StateEnter(this IState state)
        {
            state.State.EnterBegin();
            state.Enter();
            state.State.EnterEnd();
        }

        internal static void StateEnter<TArg>(this IState<TArg> state, TArg arg)
        {
            state.State.EnterBegin();
            state.Enter(arg);
            state.State.EnterEnd();
        }
    }

    /// <summary>
    /// Non-generic abstract base for every state in the machine. User states inherit from
    /// <see cref="State{TActor}"/> or <see cref="State{TActor, TParent}"/>; this base hosts
    /// the shared infrastructure (parent link, message dispatch, lifecycle hooks).
    /// </summary>
    public abstract class State : IStateBase, StateMachine.ITransition
    {
        State IStateBase.State => this;

        internal State() { }

        // Parent link. Null for the root state; set during init for every other state.
        protected State? ParentState { get; private set; }

        // The state machine this state belongs to. null!-initialized; set in Initialize and cleared in Shutdown.
        public StateMachine StateMachine { get; private set; } = null!;

        public string Name => GetType().Name;

        protected void TransitTo(IState destination) => StateMachine.TransitTo(destination);
        protected void TransitTo<TArg>(IState<TArg> destination, TArg arg) => StateMachine.TransitTo(destination, arg);

        public void PropagateMessage() => StateMachine._messageConsumed = false;

        private State?   _activeSubState;
        private IState?  _entrySubState;
        private State[]? _childStates;

        public int ChildCount => _childStates?.Length ?? 0;
        public State GetChild(int index) => _childStates![index];
        internal State? ActiveSubState => _activeSubState;

        /// <summary>
        /// Override to declare child states for a composite state. Leaves should not override
        /// this method (the default declares no children).
        /// </summary>
        /// <param name="entrySubState">The child to enter by default when this state is entered.
        /// Set to <c>null</c> to defer the entry decision to runtime — typically by calling
        /// <c>TransitTo</c> from inside <see cref="IState.Enter"/>.</param>
        /// <param name="subStates">Add each child state to this list. The list is reused across
        /// the init pass — only add to it, do not assume it's empty.</param>
        protected virtual void DeclareChildren(out IState? entrySubState, List<IStateBase> subStates)
        {
            entrySubState = null;
        }

        /// <summary>
        /// Override for setup that runs after the entire sub-tree has been initialized. Safe to
        /// reference <c>Actor</c>, sibling states, child states, etc. Called bottom-up — child
        /// states' <c>OnInitialize</c> runs before their parent's.
        /// </summary>
        protected virtual void OnInitialize() { }
        protected virtual void OnEnter() { }
        protected virtual void OnExit() { }
        protected virtual void OnShutdown() { }

        internal struct BuildContext
        {
            public readonly StateMachine     Machine;
            public readonly List<IStateBase> StateList;
            
            public BuildContext(StateMachine machine, List<IStateBase> stateList)
            {
                Machine = machine;
                StateList = stateList;
            }
        }
        
        internal virtual void Build(BuildContext ctx)
        {
            StateMachine = ctx.Machine;

            ctx.StateList.Clear();
            DeclareChildren(out _entrySubState, ctx.StateList);

            // Auto-add entry sub-state if the user didn't list it explicitly.
            if (_entrySubState != null && !ctx.StateList.Contains(_entrySubState))
                ctx.StateList.Add(_entrySubState);

            foreach (var s in ctx.StateList)
            {
                if (s is null)
                    throw new StateMachine.InitializationException($"ChildStates of {GetType()} cannot be null");
            }
            
            if (ctx.StateList.Count > 0)
            {
                _childStates = new State[ctx.StateList.Count];
                for (int i = 0; i < ctx.StateList.Count; i++)
                {
                    var child = ctx.StateList[i].State;
                    
                    if (child.ParentState != null)
                        throw new StateMachine.InitializationException(
                            $"Cannot add state {child.GetType()} as a child of {GetType()}; it already has a parent.");
                    
                    child.ParentState = this;
                    _childStates[i] = child;
                }
                ctx.StateList.Clear();

                foreach (var child in _childStates)
                    child.Build(ctx);
            }
            else
            {
                _childStates = Array.Empty<State>();
            }
        }
        
        internal void Initialize()
        {
            foreach (var child in _childStates!)
                child.Initialize();
            
            OnInitialize();
        }

        internal virtual void Shutdown()
        {
            if (_childStates != null)
            {
                foreach (var child in _childStates)
                    child.Shutdown();
            }
            OnShutdown();
            
            _activeSubState = null;
        }

        internal void EnterBegin()
        {
            StateMachine.SetTopState(this);
            _activeSubState = _entrySubState?.State;

            if (StateMachine.Logging.HasFlag(StateMachine.LogFlags.EnterExit))
                StateMachine.WriteLine($"{StateMachine.Name} entering {Name}");
        }

        internal void EnterEnd()
        {
            OnEnter();
            _entrySubState?.StateEnter();
        }

        internal void Exit()
        {
            _activeSubState?.Exit();
            OnExit();

            if (StateMachine.Logging.HasFlag(StateMachine.LogFlags.EnterExit))
                StateMachine.WriteLine($"{StateMachine.Name} exiting {Name}");

            if (ParentState != null)
                StateMachine.SetTopState(ParentState);
            else
                StateMachine.SetTopState(this);   // root exiting: stays as top (machine is going Offline)
        }

        // === Transition resolution ===
        // Implements StateMachine.ITransition. Walks via the message bubble; the first state on the
        // active chain whose child is the transition target claims it.

        void StateMachine.ITransition.TransitTo(IState target)
        {
            var targetState = target.State;
            if (targetState.ParentState == this)
            {
                _activeSubState?.Exit();
                _activeSubState = targetState;
                target.StateEnter();
                return;
            }
            PropagateMessage();
        }

        void StateMachine.ITransition.TransitTo<TArg>(IState<TArg> target, TArg arg)
        {
            var targetState = target.State;
            if (targetState.ParentState == this)
            {
                _activeSubState?.Exit();
                _activeSubState = targetState;
                target.StateEnter(arg);
                return;
            }
            PropagateMessage();
        }

        // === Message dispatch (non-virtual; single implementation works for all states) ===

        internal void ReceiveMessage<TReceiver>(Handler<TReceiver> handler) where TReceiver : class
        {
            var iter = this;
            var sm = StateMachine;
            while (iter != null)
            {
                if (iter is TReceiver receiver)
                {
                    sm._messageConsumed = true;
#if UNITY_PROFILING
                    Profiler.BeginSample(iter.GetType().FullName);
#endif
                    handler(receiver);
#if UNITY_PROFILING
                    Profiler.EndSample();
#endif
                    if (sm._messageConsumed) return;
                }
                iter = iter.ParentState;
            }
        }

        internal void ReceiveMessage<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg) where TReceiver : class
        {
            var iter = this;
            var sm = StateMachine;
            while (iter != null)
            {
                if (iter is TReceiver receiver)
                {
                    sm._messageConsumed = true;
#if UNITY_PROFILING
                    Profiler.BeginSample(iter.GetType().FullName);
#endif
                    handler(receiver, arg);
#if UNITY_PROFILING
                    Profiler.EndSample();
#endif
                    if (sm._messageConsumed) return;
                }
                iter = iter.ParentState;
            }
        }

        internal void ReceiveMessage<TReceiver, TResult>(Func<TReceiver, TResult> handler, out TResult result) where TReceiver : class
        {
            var iter = this;
            var sm = StateMachine;
            while (iter != null)
            {
                if (iter is TReceiver receiver)
                {
                    sm._messageConsumed = true;
#if UNITY_PROFILING
                    Profiler.BeginSample(iter.GetType().FullName);
#endif
                    var value = handler(receiver);
#if UNITY_PROFILING
                    Profiler.EndSample();
#endif
                    if (sm._messageConsumed)
                    {
                        result = value;
                        return;
                    }
                }
                iter = iter.ParentState;
            }
            result = default!;
        }

        internal void ReceiveMessage<TReceiver, TArg, TResult>(Func<TReceiver, TArg, TResult> handler, TArg arg, out TResult result) where TReceiver : class
        {
            var iter = this;
            var sm = StateMachine;
            while (iter != null)
            {
                if (iter is TReceiver receiver)
                {
                    sm._messageConsumed = true;
#if UNITY_PROFILING
                    Profiler.BeginSample(iter.GetType().FullName);
#endif
                    var value = handler(receiver, arg);
#if UNITY_PROFILING
                    Profiler.EndSample();
#endif
                    if (sm._messageConsumed)
                    {
                        result = value;
                        return;
                    }
                }
                iter = iter.ParentState;
            }
            result = default!;
        }

        public static implicit operator bool(State? state) => state != null;
    }

    /// <summary>
    /// Generic state with a typed actor. Use this as the base for the root state of a machine,
    /// or for any state whose users don't need a typed parent reference.
    /// </summary>
    public abstract class State<TActor> : State<TActor, State> where TActor : class { }

    /// <summary>
    /// Generic state with a typed actor and typed parent. Use this when child states want to
    /// reach back to sibling references via <c>Parent.SomeSibling</c>.
    /// </summary>
    public abstract class State<TActor, TParent> : State where TActor : class where TParent : State
    {
        private TActor? _actor = null!;

        public TActor Actor
        {
            get
            {
                AssertBuilt();
                return _actor;
            }
        }

        public TParent Parent => (TParent)ParentState!;

        [System.Diagnostics.Conditional("DEBUG")]
        private void AssertBuilt()
        {
            if (((object?)StateMachine) is null)
                throw new InvalidOperationException(
                    $"Cannot access Actor on state {GetType().Name}: state machine is not initialized " +
                    "(either Initialize has not been called, or Shutdown has run). " +
                    "States must not access Actor from their constructors — use OnInitialize instead.");
        }
        
        internal override void Build(BuildContext ctx)
        {
            _actor = ctx.Machine.Actor as TActor;
            base.Build(ctx);
        }
    }
}
