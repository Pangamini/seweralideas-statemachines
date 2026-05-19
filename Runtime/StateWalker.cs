#nullable enable

using System;

namespace SeweralIdeas.StateMachines
{
    /// <summary>
    /// Controls which states a <see cref="StateWalker"/> visits.
    /// </summary>
    public enum WalkMode
    {
        /// <summary>Visit every state in the tree, regardless of activity.</summary>
        AllStates,
        /// <summary>Visit only states currently on the active chain — the root, then recursively
        /// each composite state's currently-active sub-state, until reaching the active leaf.</summary>
        ActiveOnly,
    }

    /// <summary>
    /// Recursive visitor for a state machine's tree. Use with <see cref="StateMachine.Visit"/>
    /// when you need explicit begin/end boundaries per state (for example, to recreate a
    /// nested-box layout in IMGUI). For flat depth-first iteration use <see cref="StateMachine.Walk"/>
    /// instead.
    ///
    /// Visit order, for each visited state:
    /// 1. <see cref="BeginState"/> is called.
    /// 2. Each child of the state is visited recursively.
    /// 3. <see cref="EndState"/> is called.
    /// </summary>
    public interface IStateVisitor
    {
        void BeginState(State state, int depth, bool isActive, bool hasChildren);
        void EndState(State state, int depth, bool isActive, bool hasChildren);
    }

    /// <summary>
    /// A single state yielded by <see cref="StateWalker"/>. Carries the state itself plus the
    /// minimal context an external renderer or inspector needs without having to recurse.
    /// </summary>
    public readonly struct StateNode
    {
        public State State { get; }
        /// <summary>Nesting depth. Root state is 0.</summary>
        public int Depth { get; }
        /// <summary>True when this state is currently on the machine's active chain.</summary>
        public bool IsActive { get; }
        /// <summary>True when this state has at least one child to descend into under the current
        /// <see cref="WalkMode"/>. Useful for inspectors deciding whether to draw a disclosure
        /// arrow without having to look at the state itself.</summary>
        public bool HasChildren { get; }

        internal StateNode(State state, int depth, bool isActive, bool hasChildren)
        {
            State = state;
            Depth = depth;
            IsActive = isActive;
            HasChildren = hasChildren;
        }
    }

    /// <summary>
    /// Allocation-aware enumerable over a <see cref="StateMachine"/>'s state tree. Returned by
    /// <see cref="StateMachine.Walk"/>. Designed for <c>foreach</c>:
    ///
    /// <code>
    /// foreach (var node in machine.Walk(WalkMode.AllStates))
    /// {
    ///     // node.State, node.Depth, node.IsActive, node.HasChildren
    /// }
    /// </code>
    ///
    /// The enumerator is a struct, so per-MoveNext iteration is allocation-free. A single
    /// small <see cref="StateNode"/>-stack array is allocated on first MoveNext (resized only
    /// for deeply nested trees).
    /// </summary>
    public readonly struct StateWalker
    {
        private readonly StateMachine _machine;
        private readonly WalkMode _mode;

        internal StateWalker(StateMachine machine, WalkMode mode)
        {
            _machine = machine;
            _mode = mode;
        }

        public Enumerator GetEnumerator() => new(_machine, _mode);

        public struct Enumerator
        {
            private readonly StateMachine _machine;
            private readonly WalkMode _mode;
            private Frame[]? _stack;
            private int _stackTop;
            private StateNode _current;
            private bool _started;

            private struct Frame
            {
                public State state;
                public int depth;
                public int childIndex;  // -1 = haven't emitted self yet; >=0 = next child to visit
                public bool isActive;
            }

            internal Enumerator(StateMachine machine, WalkMode mode)
            {
                _machine = machine;
                _mode = mode;
                _stack = null;
                _stackTop = -1;
                _current = default;
                _started = false;
            }

            public StateNode Current => _current;

            public bool MoveNext()
            {
                if (!_started)
                {
                    _started = true;
                    if (!_machine.IsInitialized)
                        return false;
                    _stack = new Frame[16];
                    _stack[0] = new Frame
                    {
                        state = _machine.RootState,
                        depth = 0,
                        childIndex = -1,
                        isActive = true,
                    };
                    _stackTop = 0;
                }

                while (_stackTop >= 0)
                {
                    ref Frame top = ref _stack![_stackTop];

                    if (top.childIndex == -1)
                    {
                        _current = new StateNode(top.state, top.depth, top.isActive, HasChildrenIn(top.state));
                        top.childIndex = 0;
                        return true;
                    }

                    if (TryGetChild(top.state, top.childIndex, out State? child, out bool childActive))
                    {
                        bool active = top.isActive && childActive;
                        top.childIndex++;
                        Push(child, top.depth + 1, active);
                        continue;
                    }

                    _stackTop--;
                }

                return false;
            }

            private bool HasChildrenIn(State state)
            {
                return _mode == WalkMode.AllStates
                    ? state.ChildCount > 0
                    : state.ActiveSubState != null;
            }

            private bool TryGetChild(State state, int index, out State child, out bool isActive)
            {
                if (_mode == WalkMode.AllStates)
                {
                    if (index < state.ChildCount)
                    {
                        child = state.GetChild(index);
                        isActive = ReferenceEquals(state.ActiveSubState, child);
                        return true;
                    }
                }
                else
                {
                    // ActiveOnly: at most one child (the active sub-state)
                    if (index == 0 && state.ActiveSubState != null)
                    {
                        child = state.ActiveSubState;
                        isActive = true;
                        return true;
                    }
                }
                child = null!;
                isActive = false;
                return false;
            }

            private void Push(State state, int depth, bool isActive)
            {
                _stackTop++;
                if (_stackTop >= _stack!.Length)
                {
                    var grown = new Frame[_stack.Length * 2];
                    Array.Copy(_stack, grown, _stack.Length);
                    _stack = grown;
                }
                _stack[_stackTop] = new Frame
                {
                    state = state,
                    depth = depth,
                    childIndex = -1,
                    isActive = isActive,
                };
            }
        }
    }
}
