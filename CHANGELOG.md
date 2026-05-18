# Changelog
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