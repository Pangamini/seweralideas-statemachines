# Changelog
## [0.4.0]

Major refactor. Three big shifts:

1. **Single state class.** `SimpleState<>`, `HierarchicalState<>`, and `OrthogonalState<>` collapse into one `State<TActor>` / `State<TActor, TParent>`. Composites declare children via a virtual `DeclareChildren`; leaves don't override anything. Orthogonal regions are gone — model parallel concurrent state by composing multiple `StateMachine` instances.
2. **Topology and actor are set once at construction.** `StateMachine`'s constructor takes the actor and walks the root tree immediately — builds all parent/child links, sets each state's `StateMachine` reference, and binds each typed `_actor`. `Initialize()` / `Shutdown()` become parameterless lifecycle methods that just enter/exit the root and run `OnInitialize` / `OnShutdown` bottom-up. Re-initialization across a `Shutdown` is cheap — no allocations, no `DeclareChildren` replay.
3. **Public surface modernized.** Properties get PascalCased; the active-state set is now always a linear leaf-to-root path; queries pick up `FuncHandler` overloads that return a value.

### Added
- `FuncHandler<TReceiver, TResult>` / `FuncHandler<TReceiver, TArg, TResult>` delegates — mirror `Handler<>` for return-value dispatch.
- `SendMessageNow<TReceiver, TResult>(FuncHandler<TReceiver, TResult>, out TResult)` and the `<TArg, TResult>` variant — query overloads that capture a return value from the first active state implementing `TReceiver`. Generics infer from the handler.
- `TrySendMessageNow<TReceiver, TResult>(FuncHandler<TReceiver, TResult>, out TResult)` and the `<TArg, TResult>` variant — Try-shaped (no throw, no side effects on failure).
- `State.ChildCount` / `State.GetChild(int)` made public — useful for inspectors and tooling.

### Changed
- **Single state class.** `SimpleState<>`, `SimpleState<,>`, `HierarchicalState<>`, `HierarchicalState<,>` collapse into `State<TActor>` and `State<TActor, TParent>`. User states inherit one of these directly. No more decision about "do I have children".
- **`StateMachine` ctor takes the actor; `Initialize` / `Shutdown` are parameterless.** Topology and actor are bound once at construction (an internal `Build` phase walks the root tree, calls `DeclareChildren` on each state, sets parent/child links, and binds each typed `_actor`). `Initialize()` / `Shutdown()` are repeatable lifecycle methods — call them as many times as you like; topology and actor survive across cycles.
- `OnInitialize(out IState, List<IStateBase>)` (the old hierarchical-children declaration) is now `DeclareChildren(out IState? entrySubState, List<IStateBase> subStates)`. It's `virtual` with a default of "no children", so leaves don't have to override anything. Now called from `StateMachine`'s ctor (during Build), not from `Initialize`.
- `OnInitialize()` (no-args, post-build hook) keeps the same name. It now runs once per `Initialize()` call, bottom-up — paired with `OnShutdown()` which runs bottom-up on `Shutdown()`. Use these for setup that should be active only while the machine is running (per-entry work still belongs in `OnEnter` / `OnExit`).
- `StateMachine.Actor` is now a non-nullable `public readonly object` field set in the ctor; previously a mutable `object?` property set from `Initialize(object)`.
- Public properties renamed to PascalCase:
  - `IStateBase.state` → `IStateBase.State`
  - `State.name` → `State.Name`
  - `State.stateMachine` → `State.StateMachine`
  - `State<TActor, TParent>.actor` → `State<TActor, TParent>.Actor`
  - `State<TActor, TParent>.parent` → `State<TActor, TParent>.Parent`
  - `StateMachine.actor` → `StateMachine.Actor`
  - `StateMachine.logFlags` (public mutable field) → `StateMachine.Logging` (property; the `LogFlags` enum type kept its name, so the property got a distinct one)
- `where TParent : IParentState` constraint becomes `where TParent : State`.
- Transition resolution: pointer compare (`target.parentState == this`) replaces the `Contains(_childStates, target)` array scan.
- `State.ReceiveMessage` is now non-virtual — a single concrete implementation walks the active chain via parent links for all states.
- `Walk()` and `Visit()` internals simplify: depth-first traversal with no per-orthogonal-branch fan-out. `Walk(WalkMode.ActiveOnly)` is a linear chain walk.
- Door and Lever sample rewritten as composition — the actor owns two `StateMachine` instances (`_leverMachine`, `_doorMachine`) and routes events to the relevant one. Lever's button-handlers fan out to the door machine explicitly.

### Removed
- `OrthogonalState<TActor>` and `OrthogonalState<TActor, TParent>` — use composition (an outer state owning multiple `StateMachine` instances, fanning out messages explicitly) for parallel concurrent state.
- `OrthogonalBranch` (internal).
- `IParentState` marker interface — no longer needed (every `State` can be a parent).
- `IHasTopState` internal interface — only `StateMachine` tracks the top state now.
- `SimpleState<TActor>`, `SimpleState<TActor, TParent>`, `HierarchicalState<TActor>`, `HierarchicalState<TActor, TParent>` — merged into `State<>`.
- `State._hasTopState` field — states reference `StateMachine` directly.
- `State.WalkChildCount`, `State.WalkChild`, `State.WalkIsChildActive` virtuals — replaced by direct field access via `ChildCount` / `GetChild` / `ActiveSubState`.
- `Contains` static helper — no longer used after the transition-resolution simplification.

### Migration notes from 0.3.x

**State class renames** — project-wide find/replace:

- `SimpleState<` → `State<`
- `HierarchicalState<` → `State<`
- `OrthogonalState<...>` — rewrite as composition (see below).
- `IParentState` — drop. If used as a `TParent` constraint, change to `State`.

**Property renames** — also project-wide:

- `.actor` → `.Actor`
- `.parent` → `.Parent`
- `.stateMachine` → `.StateMachine`
- `.name` → `.Name`   *(on a `State`; Unity's `GameObject.name` is unaffected)*
- `.state` → `.State`   *(on `IStateBase`; this is the "downcast to State" property)*
- `.logFlags` → `.Logging`

**`OnInitialize` for composite states** is now `DeclareChildren`:

```csharp
// Before
protected override void OnInitialize(out IState entrySubState, List<IStateBase> subStates) { ... }

// After
protected override void DeclareChildren(out IState? entrySubState, List<IStateBase> subStates) { ... }
```

The no-args `OnInitialize()` keeps the same signature, but is now paired with `OnShutdown()` and runs once per `Initialize()` call (not just once per machine lifetime).

**Construction and lifecycle** — actor moves to the ctor; `Initialize` / `Shutdown` are parameterless:

```csharp
// Before
var machine = new StateMachine("name", new State_Root());
machine.Initialize(this);   // actor passed here
// ...
machine.Shutdown();

// After
var machine = new StateMachine("name", this, new State_Root());   // actor passed here
machine.Initialize();
// ...
machine.Shutdown();
machine.Initialize();   // cheap — topology and actor survive Shutdown
```

Because the actor is now a ctor argument, MonoBehaviours that previously held the machine as a `readonly` field initializer need to move construction into a constructor or `Awake` — `this` isn't available in field initializers.

**Orthogonal regions** are gone. Migrate to composition: an outer state owns multiple `StateMachine` instances, fans out messages explicitly. The Door and Lever sample shows the new pattern.

```csharp
// Before
class State_Root : OrthogonalState<Actor>, IState
{
    protected override void OnInitialize(List<IState> subStates)
    {
        subStates.Add(new State_LeverRoot());
        subStates.Add(new State_DoorRoot());
    }
}

// After: actor owns two state machines, routes messages explicitly
class Actor : MonoBehaviour
{
    private readonly StateMachine _lever;
    private readonly StateMachine _door;

    protected Actor()
    {
        _lever = new("Lever", this, new State_LeverRoot());
        _door  = new("Door",  this, new State_DoorRoot());
    }

    void OnEnable()  { _lever.Initialize(); _door.Initialize(); }
    void OnDisable() { _lever.Shutdown();   _door.Shutdown(); }

    public void OnClick() => _lever.SendMessage(msg_onClick);   // route to whichever machine cares
}
```

For "fan a message out to every machine" patterns, write the fan-out explicitly at the call site — it's three lines per message and unambiguous.

## [0.3.0]
### Added
- `StateMachine.Walk(WalkMode)` — allocation-aware struct enumerator over the state tree.
- `StateMachine.Visit(IStateVisitor)` — recursive visitor with `BeginState` / `EndState` boundaries per state, for renderers that need explicit nesting (e.g. the nested-box IMGUI layout).
- `StateNode` struct, `WalkMode` enum (`AllStates` / `ActiveOnly`), and `IStateVisitor` interface in `SeweralIdeas.StateMachines`.
- Debug-only warning when `TransitTo(target)` bubbles past the root without being claimed by any hierarchical parent.
- DEBUG-only assert on `State<TActor>.actor` getter when accessed before `Initialize` (catches the field-initializer footgun loudly).
- New `Door and Lever` sample file `InGameStateMachineOverlay.cs` — reusable IMGUI overlay built on `IStateVisitor`, available for any project that wants the runtime debug visual back.
- New `Recipes` sample with `CommonMessages` (shared `ITick` / `IUpdate`) and `State_Wait<TActor, TParent>` (reusable timer state with extend/cancel).
- XML doc comments on `IStateBase`, `IState`, `IState<TArg>`, `IParentState` clarifying when each is needed and that `IState.Enter()` ships with a C# default-interface-method body (an empty default), so no need for empty stubs.

### Changed
- Runtime is now `#nullable enable` across every file. Reference types are explicitly annotated.
- `HierarchicalState.OnInitialize` abstract signature: `out IState entrySubState` → `out IState? entrySubState`. Existing overrides continue to compile; setting `null` to defer entry decision is now explicit at the type level.
- `StateMachine(name, rootState, debugLog)` — `Action<string>? debugLog` (was `Action<string>`).
- `StateMachine.actor` property type: `object?` (was `object`).
- Private field naming convention: `m_xxx` → `_xxx` (`_camelCase`).
- Lambda parameter convention: name the first parameter `receiver` instead of `handler` — the `Handler<>` delegate is the dispatch shim, the parameter is the receiver state. Samples and README updated; no API impact.
- `StateMachineWindow` rewritten to consume `Visit()` for the nested-box layout. Settings now expose active/inactive colors.
- README rewritten with overview, tutorial, cookbook, reference, and ConcurrentStateMachine sections.

### Removed
- `StateMachine.OnGUI(GUISettings)` and `StateMachine.GUISettings`. Runtime is now Unity-free apart from optional `UnityEngine.Profiling` hooks gated by `UNITY_PROFILING`.
- Per-state `DrawGUI` overrides on `State` / `SimpleState` / `HierarchicalState` / `OrthogonalState`.
- `[ShowField]` attribute and the `StateDebugInfo` reflection cache.
- `[HasStateMachine]` attribute. Editor window discovery now scans MonoBehaviour fields directly without recursion.

### Fixed
- Pooled `Message<TReceiver>` / `Message<TReceiver, TArg>` instances no longer keep the handler delegate or `arg0` rooted between checkouts (`Reset()` clears them).
- Duplicate `Debug.Assert` in `StateMachine.TransitTo<TArg>` removed.

### Migration notes
- **Empty `void IState.Enter() { }` stubs** can be deleted — `IState.Enter()` has a default empty body via a C# default-interface-method.
- **`OnGUI(GUISettings)`** is gone. For in-game debug overlay: copy `Samples~/DoorAndLever/InGameStateMachineOverlay.cs` (it's ~25 lines, built on `IStateVisitor`). For the editor: the `Window → Analysis → StateMachine Debugger` still works; configure colors via its **Settings** toolbar toggle.
- **`[HasStateMachine]` recursion** is gone. If you relied on nested state-machine discovery via that attribute, surface the inner machines via a direct field/property on the MonoBehaviour.
- **`[ShowField]`** is gone. State-field display in the debugger is not currently replaced; if you need it, walk the machine yourself via `Visit()` or `Walk()` and emit fields with your own renderer.
- **`m_` → `_`** rename only affects the package's own private fields. User code is untouched.

## [0.2.3]
### Added
- StateMachine messages show up in the Unity profiler (per-message receiver-type samples).
- Editor profiler sampler.
- Assert when a `TransitTo` target state is not part of the StateMachine instance.
- `IState.Enter()` now ships with a default empty body (via a C# default-interface-method) — concrete states no longer need an empty `void IState.Enter() { }` stub.

### Changed
- `StateMachine.Initialize` exception handling: when initialization throws, the machine now attempts to shut down the partial tree and rethrows the original exception. If shutdown also throws, both are wrapped in an `AggregateException`.
- `ConcurrentStateMachine`'s internal lock is now reentrant — allows nested calls from the same thread without deadlocking.
- Runtime asmdef files renamed to `SeweralIdeas.StateMachines` and `SeweralIdeas.StateMachines.Editor`.
- Removed stray `using UnityEditor` from `Runtime/State.cs` (compile cleanup; runtime no longer accidentally pulls in editor types).
- Package author display name updated.
- Misc small cleanups and a debug-assert that no longer requires Unity's `Debug`.

## [0.2.2]
### Added
- ConcurrentStateMachine wrapper for StateMachine
## [0.2.1]
### Removed
- StateMachine multi-threaded modes
  - StateMachines are no longer thread-safe!
  - Thread-safe stateMachine might be included as a wrapper later For now, you have to handle the thread-safety yourself, eg. by using locks.
## [0.2.0]
### Changed
- Hierarchical and Orthogonal states must now list their subStates during initialization.
## [0.1.2]
### Added
- State field values can now be displayed in StateMachineWindow
    - Options include all fields, or just those marked witb [ShowField] attribute

## [0.1.1]
### Fixed
- Bug related to exiting an orthogonal state