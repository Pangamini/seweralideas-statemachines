# SeweralIdeas.StateMachines

Hierarchical finite state machines for C#, with first-class Unity support.

## Overview

`SeweralIdeas.StateMachines` is a runtime library for building hierarchical state machines (HFSMs) in C#. It is built around three ideas:

1. **Real state graphs are nested.** A `Game` state contains `Menu` / `Playing` / `Paused`; `Playing` itself contains `Idle` / `Walking` / `InDialogue`. Modeling that flat leads to combinatorial blow-up. `HierarchicalState` (one child active at a time) and `OrthogonalState` (multiple children active in parallel) are first-class.
2. **Behaviors compose like ordinary classes.** A "flees from threats" or "walks along a path" state is a building block. State classes here are generic over their actor and parent, so you can write abstract base states and reuse them across actors.
3. **State scopes everything.** Subscribe to events in `OnEnter`, unsubscribe in `OnExit`. Within the machine, communication happens through typed *messages* that bubble from the active leaf to the root, hitting any state along the way that implements the matching receiver interface.

The runtime has no Unity dependency. A Unity package layout is included so it can be consumed via UPM.

## At a glance

```csharp
using System.Collections.Generic;
using SeweralIdeas.StateMachines;

public class Game
{
    // 1. Define a receiver interface for any message your states should handle.
    interface ITick : IStateBase { void Tick(float dt); }

    // 2. Wrap each dispatch in a static Handler delegate.
    // The first parameter is the receiver (the state implementing ITick).
    static readonly Handler<ITick, float> msg_tick = (receiver, dt) => receiver.Tick(dt);

    readonly StateMachine _machine = new("Game", new State_Root());

    public void Start()          => _machine.Initialize(this);
    public void Tick(float dt)   => _machine.SendMessage(msg_tick, dt);
    public void Stop()           => _machine.Shutdown();

    // 3. Define states.
    class State_Root : HierarchicalState<Game>, IState
    {
        readonly State_Idle _idle = new();
        protected override void OnInitialize(out IState entrySubState, List<IStateBase> subStates)
        {
            entrySubState = _idle;
            subStates.Add(_idle);
        }
    }

    class State_Idle : SimpleState<Game, State_Root>, IState, ITick
    {
        void IState.Enter() { /* per-entry setup */ }
        void ITick.Tick(float dt) { /* per-tick work */ }
    }
}
```

That is the whole shape of every state machine in this library. Everything else is composition.

## Installation

### Unity (UPM)

Add the package via **Window → Package Manager → + → Add package from git URL...** and paste the repository URL, or edit `Packages/manifest.json` directly:

```json
{
  "dependencies": {
    "com.seweralideas.statemachines": "https://github.com/<owner>/seweralideas-statemachines.git"
  }
}
```

The **Door and Lever** sample can be imported from the package's page in Package Manager.

### Plain .NET

There is no NuGet package. The runtime is a small set of files under `Runtime/`. Drop them into your project, or reference the assembly produced by the Unity package. All Unity-only code is gated behind `#if UNITY_5_3_OR_NEWER`, so the same sources build outside Unity.

## Core concepts

| Concept | What it is |
|---|---|
| **`StateMachine`** | The container. Holds the root state, an `actor`, and a message queue. You `Initialize` it, send messages, and `Shutdown`. |
| **`actor`** | Any object you pass to `Initialize`. States typed `SimpleState<TActor>` / `HierarchicalState<TActor>` expose it as a strongly-typed `actor` property. It is the bridge between the machine and the world it controls. |
| **Root state** | The state at the top of the hierarchy. There is exactly one. |
| **`HierarchicalState`** | Has child states; exactly one is active at a time. Transitions inside the children move the "active" pointer. |
| **`OrthogonalState`** | Has parallel child branches, all active at once. Each branch maintains its own active-state pointer and dispatches messages independently; all branches share the machine's single message and transition queues. |
| **`SimpleState`** | A leaf. |
| **Receiver interface** | An interface *you* define, e.g. `interface ITick { void Tick(float dt); }`. Any state can implement zero or more of these. |
| **`Handler<TReceiver>` / `Handler<TReceiver, TArg>`** | A delegate that dispatches one specific receiver interface. Conventionally a `static readonly` field per message. |
| **Message bubbling** | `machine.SendMessage(handler, arg)` walks from the active leaf upward through its parents. The first state that implements `TReceiver` consumes the message. The state can opt to keep it bubbling by calling `PropagateMessage()`. |
| **Transition** | `TransitTo(targetState)` (or `TransitTo(target, arg)`). The transition bubbles up like a message until a `HierarchicalState` that contains the target as a child claims it; that parent exits its current child and enters the target. |

## Tutorial: building a traffic light

This tutorial builds a traffic light in five steps, introducing one feature at a time. Each step extends the previous one.

### Step 1 — three states and a tick

A traffic light cycles Red → Green → Yellow → Red. We need a state for each color and a `Tick` message to drive the timers.

```csharp
using System.Collections.Generic;
using SeweralIdeas.StateMachines;

public class TrafficLight
{
    interface ITick : IStateBase { void Tick(float dt); }
    static readonly Handler<ITick, float> msg_tick = (receiver, dt) => receiver.Tick(dt);

    readonly StateMachine _machine;

    public TrafficLight()
    {
        _machine = new StateMachine("TrafficLight", new State_Root());
    }

    public void Start()        => _machine.Initialize(this);
    public void Stop()         => _machine.Shutdown();
    public void Tick(float dt) => _machine.SendMessage(msg_tick, dt);

    class State_Root : HierarchicalState<TrafficLight>, IState
    {
        public readonly State_Red    Red    = new();
        public readonly State_Green  Green  = new();
        public readonly State_Yellow Yellow = new();

        protected override void OnInitialize(out IState entrySubState, List<IStateBase> subStates)
        {
            entrySubState = Red;
            subStates.Add(Red);
            subStates.Add(Green);
            subStates.Add(Yellow);
        }
    }

    abstract class State_Timed : SimpleState<TrafficLight, State_Root>, IState, ITick
    {
        protected float _timeLeft;
        protected abstract float  Duration  { get; }
        protected abstract IState NextState { get; }

        void IState.Enter() => _timeLeft = Duration;

        void ITick.Tick(float dt)
        {
            _timeLeft -= dt;
            if (_timeLeft <= 0f)
                TransitTo(NextState);
        }
    }

    class State_Red    : State_Timed { protected override float Duration => 5f; protected override IState NextState => parent.Green;  }
    class State_Green  : State_Timed { protected override float Duration => 5f; protected override IState NextState => parent.Yellow; }
    class State_Yellow : State_Timed { protected override float Duration => 1f; protected override IState NextState => parent.Red;    }
}
```

Five things to notice:

- `State_Timed` is an **abstract reusable state**. It implements `ITick` once; the three concrete colors only differ in duration and next state. This kind of composition is the main reason every state class is generic over `TActor` and `TParent`.
- Leaves reach siblings as `parent.Green`, `parent.Yellow`, etc. The `parent` property is typed `State_Root` because of the `SimpleState<TrafficLight, State_Root>` declaration.
- `TransitTo(NextState)` bubbles up to `State_Root`, which finds the target in its children list and switches.
- `Tick` is implemented once on `State_Timed`. Each color inherits the behavior — no per-color duplication.
- `Initialize(this)` sets the actor, so anywhere inside a state `actor.SomeMethod()` calls the owning `TrafficLight`.

### Step 2 — power off

A real traffic light can be powered off. Wrap the cycle in a higher-level state.

```csharp
interface IPowerSwitch : IStateBase { void SetPower(bool on); }
static readonly Handler<IPowerSwitch, bool> msg_power = (receiver, on) => receiver.SetPower(on);

class State_Root : HierarchicalState<TrafficLight>, IState, IPowerSwitch
{
    public readonly State_Off       Off       = new();
    public readonly State_Operating Operating = new();

    protected override void OnInitialize(out IState entrySubState, List<IStateBase> subStates)
    {
        entrySubState = Off;
        subStates.Add(Off);
        subStates.Add(Operating);
    }

    void IPowerSwitch.SetPower(bool on) => TransitTo(on ? (IState)Operating : Off);
}

class State_Off : SimpleState<TrafficLight, State_Root>, IState
{
    void IState.Enter() { /* darken bulbs */ }
}

class State_Operating : HierarchicalState<TrafficLight, State_Root>, IState
{
    public readonly State_Red    Red    = new();
    public readonly State_Green  Green  = new();
    public readonly State_Yellow Yellow = new();

    protected override void OnInitialize(out IState entrySubState, List<IStateBase> subStates)
    {
        entrySubState = Red;
        subStates.Add(Red);
        subStates.Add(Green);
        subStates.Add(Yellow);
    }
}
```

The colors (`State_Red` / `State_Green` / `State_Yellow`) now live under `State_Operating`. Their declared parent therefore changes from `State_Root` to `State_Operating`:

```csharp
abstract class State_Timed : SimpleState<TrafficLight, State_Operating>, IState, ITick { ... }
```

The structure now looks like:

```
State_Root   (Hierarchical)
├── State_Off            (Simple)
└── State_Operating  (Hierarchical)
    ├── State_Red        (Simple)
    ├── State_Green      (Simple)
    └── State_Yellow     (Simple)
```

`msg_power` with `on = false` bubbles from whichever color is active, past `State_Operating` (which doesn't implement `IPowerSwitch`), up to `State_Root`, which transitions to `Off`. Exiting `Operating` automatically exits whichever color was active first.

### Step 3 — a pedestrian light in parallel

A pedestrian crossing has its own light that runs *alongside* the vehicle light, not under it. That's an **orthogonal** composition.

```csharp
class State_Crossing : OrthogonalState<TrafficLight>, IState
{
    public readonly State_Root    Vehicles    = new();
    public readonly State_PedestrianLight Pedestrians = new();

    protected override void OnInitialize(List<IState> subStates)
    {
        subStates.Add(Vehicles);
        subStates.Add(Pedestrians);
    }
}
```

Update the constructor to use the new root:

```csharp
_machine = new StateMachine("TrafficLight", new State_Crossing());
```

Notice the signature difference between the two composite kinds:

- `HierarchicalState.OnInitialize(out IState entrySubState, List<IStateBase> subStates)` — exactly one entry child.
- `OrthogonalState.OnInitialize(List<IState> subStates)` — every child runs.

Messages sent to the machine reach **both** branches independently. Transitions inside `Vehicles` do not affect `Pedestrians`, and vice versa. Each branch has its own active leaf and is dispatched to separately.

`State_PedestrianLight` is just another hierarchical sub-tree, with its own `Walk` / `Wait` children — same shape as `State_Operating`.

### Step 4 — a button press message

Pedestrians press a button to request a faster cycle. Define the message:

```csharp
interface IPedestrianButton : IStateBase { void OnPress(); }
static readonly Handler<IPedestrianButton> msg_press = receiver => receiver.OnPress();

public void PressButton() => _machine.SendMessage(msg_press);
```

Only `Green` should react — when pressed during a green, clip the remaining time:

```csharp
class State_Green : State_Timed, IPedestrianButton
{
    protected override float Duration => 5f;
    protected override IState NextState => parent.Yellow;

    void IPedestrianButton.OnPress()
    {
        if (_timeLeft > 1f)
            _timeLeft = 1f;
    }
}
```

What happens at dispatch time:

1. `SendMessage` walks up from the active leaf of each orthogonal branch.
2. In the vehicle branch, if `Green` is active, its handler fires and the message is consumed.
3. If `Red` or `Yellow` is active, no state in the chain implements `IPedestrianButton` — the press is silently ignored, which is exactly what we want.
4. In the pedestrian branch, the same walk happens independently; if any pedestrian state implements `IPedestrianButton` it can react too.

If you want a state to handle a message *and* let it keep bubbling, call `PropagateMessage()` at the end of the handler.

### Step 5 — passing arguments into a transition

An emergency override should jump immediately to Yellow with an extra-long fade — say, 10 seconds — before returning to Red. We need a way to enter `Yellow` with a custom duration.

A state can implement `IState<TArg>` in addition to (or instead of) `IState`. The runtime calls whichever `Enter` overload matches the transition:

```csharp
class State_Yellow : State_Timed, IState<float>
{
    void IState<float>.Enter(float duration) => _timeLeft = duration;

    protected override float  Duration  => 1f;             // used when entered with no arg
    protected override IState NextState => parent.Red;
}
```

Both `IState.Enter()` (inherited from `State_Timed`) and `IState<float>.Enter(float)` (defined here) are now valid entry paths:

```csharp
TransitTo(Yellow);          // calls IState.Enter()         — _timeLeft = Duration = 1s
TransitTo(Yellow, 10f);     // calls IState<float>.Enter(10) — _timeLeft = 10s
```

Define the emergency message and route it through `State_Operating`:

```csharp
interface IEmergency : IStateBase { void OnEmergency(); }
static readonly Handler<IEmergency> msg_emergency = receiver => receiver.OnEmergency();

class State_Operating : HierarchicalState<TrafficLight, State_Root>, IState, IEmergency
{
    public readonly State_Red    Red    = new();
    public readonly State_Green  Green  = new();
    public readonly State_Yellow Yellow = new();

    protected override void OnInitialize(out IState entrySubState, List<IStateBase> subStates)
    {
        entrySubState = Red;
        subStates.Add(Red);
        subStates.Add(Green);
        subStates.Add(Yellow);
    }

    void IEmergency.OnEmergency() => TransitTo(Yellow, 10f);
}
```

Wherever the cycle is — Red, Green, or Yellow — `_machine.SendMessage(msg_emergency)` bubbles up to `State_Operating`, which jumps to a 10-second Yellow.

That's the full toolkit: leaf states, hierarchical composition, orthogonal composition, message bubbling with optional propagation, and transitions with typed arguments. The patterns section below covers idioms for scaling these up.

## Patterns cookbook

### Centralize cross-cutting messages

When messages are shared across many actors (tick, update, hit, save), keep them in one place:

```csharp
public static class CommonMessages
{
    public interface ITick   : IStateBase { void Tick(float dt); }
    public interface IUpdate : IStateBase { void Update(float dt); }

    public static readonly Handler<ITick, float> msg_tick = (receiver, dt) =>
    {
        receiver.Tick(dt);
        receiver.state.PropagateMessage();   // ticks broadcast by default
    };

    public static readonly Handler<IUpdate, float> msg_update = (receiver, dt) =>
    {
        receiver.Update(dt);
        receiver.state.PropagateMessage();
    };
}
```

Then `using static CommonMessages;` in every state file. The handlers call `PropagateMessage()` from the *handler side*, so ticks broadcast to every state that implements `ITick` rather than stopping at the first one — which is usually what you want for periodic work.

### Tick-down state with extension, cancellation, and message handling

A timer that just transitions when it runs out is a five-line state. A *real* timer state has more responsibilities: an entry-time arg sets the duration; other messages extend or shorten it; a cancel message stops it early; subclasses may need to react to additional messages while it's running. Express it once as a reusable abstract state, building on `CommonMessages.ITick` from the previous recipe:

```csharp
public abstract class State_Wait<TActor, TParent>
    : SimpleState<TActor, TParent>, IState<float>, ITick
    where TActor  : class
    where TParent : IParentState
{
    protected float _timeLeft;

    void IState<float>.Enter(float seconds) => _timeLeft = seconds;

    void ITick.Tick(float dt)
    {
        _timeLeft -= dt;
        if (_timeLeft <= 0f)
        {
            _timeLeft = float.PositiveInfinity;
            OnElapsed();
        }
    }

    protected abstract void OnElapsed();
}
```

Concrete use — one line per behavior, all the timer mechanics inherited:

```csharp
class State_Reloading : State_Wait<Player, State_Combat>
{
    protected override void OnElapsed()   => TransitTo(parent.Idle);
}

// Enter it with a duration:
TransitTo(parent.Reloading, 2.5f);
```

A subclass can also add its own receiver interfaces — `State_Reloading` could implement `IOnHit` to abort early — and the timer behavior keeps working underneath. The library ships no built-in wait state because it has no time concept: every project's tick source and clock are different. The 25 lines above transplant cleanly into any of them.

### Reusable abstract states

Generic state classes inherit cleanly across actors. Define behavior once, parameterize on the actor and parent:

```csharp
public interface IHasWalker { Walker Walker { get; } }

public abstract class State_WalkOnPath<TActor, TParent> : SimpleState<TActor, TParent>, IState
    where TActor  : class, IHasWalker
    where TParent : IParentState
{
    protected abstract Vector2 GetNextStep();
    void IState.Enter() { /* set initial destination, etc. */ }
    // ...
}

// Concrete use:
class State_WalkToTree : State_WalkOnPath<Lumberjack, State_DoingFine>
{
    protected override Vector2 GetNextStep() { /* tree-finding logic */ }
}
```

The `TParent` generic parameter is what makes `parent.SomeSibling` strongly typed inside the abstract base.

### Re-enter the same state with new args

```csharp
class State_Dialogue : SimpleState<Controller, State_Game>, IState<Dialogue>
{
    Dialogue _dialogue;
    void IState<Dialogue>.Enter(Dialogue dialogue) => _dialogue = dialogue;

    void OnActiveDialogueChanged(Dialogue next)
    {
        if (next != null && next != _dialogue)
            TransitTo(this, next);   // exit and re-enter with the new dialogue
    }
}
```

### Cross-machine bridging

Different objects can own their own state machines. They talk to each other through ordinary public methods that internally `SendMessage`:

```csharp
public class GameController
{
    interface IGoToScene { void GoToScene(string name); }
    static readonly Handler<IGoToScene, string> msg_go = (receiver, name) => receiver.GoToScene(name);
    readonly StateMachine _machine = new("GameController", new State_Root());

    public void GoToScene(string name) => _machine.SendMessage(msg_go, name);
}
```

A message handler in one machine can call `otherController.GoToScene(...)`, which enqueues the message into the other machine. There is no global event bus; the call graph is explicit.

### Subscribe in `OnEnter`, unsubscribe in `OnExit`

State lifetime is a natural scope for event subscriptions:

```csharp
class State_World : HierarchicalState<Controller, State_Game>, IState
{
    protected override void OnEnter()
    {
        base.OnEnter();
        actor.GameSystem.ActiveSceneObjectChanged += OnActiveSceneObjectChanged;
        OnActiveSceneObjectChanged(actor.GameSystem.ActiveSceneObject);
    }

    protected override void OnExit()
    {
        actor.GameSystem.ActiveSceneObjectChanged -= OnActiveSceneObjectChanged;
        base.OnExit();
    }
}
```

When the machine moves out of `State_World` for *any* reason — including a parent transition higher up — the subscription is dropped. Forgotten unsubscriptions are a frequent source of bugs in event-driven systems; scoping them to states eliminates the category.

### One state, multiple `IState<T>` shapes

A state can be a transition target for multiple distinct payloads:

```csharp
class State_Teleporting : SimpleState<Walker, State_Enabled>,
    IState<(Vector3 destination, bool checkPath)>,
    IState<(Area area,            bool checkPath)>
{
    void IState<(Vector3, bool)>.Enter((Vector3 dest, bool check) args) { /* ... */ }
    void IState<(Area,    bool)>.Enter((Area area,    bool check) args) { /* ... */ }
}
```

Each `TransitTo(state, ...)` resolves to the matching `Enter` overload at compile time.

### Decide entry at runtime

When which child to enter depends on runtime conditions, leave `entrySubState` null and decide in `Enter`:

```csharp
class State_Root : HierarchicalState<GameRoot>, IState
{
    readonly State_MainMenu _mainMenu = new();
    readonly State_Ingame   _ingame   = new();

    protected override void OnInitialize(out IState entrySubState, List<IStateBase> subStates)
    {
        entrySubState = null;   // decided in Enter()
        subStates.Add(_mainMenu);
        subStates.Add(_ingame);
    }

    void IState.Enter()
        => TransitTo(actor.HasActiveSave ? (IState)_ingame : _mainMenu);
}
```

### Bridge external events into the machine

Subscribe in `OnEnter` and forward the callback as a message. This keeps the heavy work inside the state machine's dispatch model rather than running synchronously inside a foreign event:

```csharp
class State_Root : HierarchicalState<Controller>, IState, IGameStateChanged
{
    static readonly Handler<IGameStateChanged, World.State> msg_gameStateChanged =
        (receiver, newState) => receiver.OnGameStateChanged(newState);

    protected override void OnEnter()
    {
        base.OnEnter();
        actor.World.GameState.Changed += OnGameStateChanged;
    }

    protected override void OnExit()
    {
        actor.World.GameState.Changed -= OnGameStateChanged;
        base.OnExit();
    }

    void OnGameStateChanged(World.State newState)
        => stateMachine.SendMessage(msg_gameStateChanged, newState);

    void IGameStateChanged.OnGameStateChanged(World.State newState) { /* react */ }
}
```

## Reference

### Lifecycle

```csharp
var machine = new StateMachine(name, rootState, debugLog: null);
machine.Initialize(actor);
machine.SendMessage(handler, arg);
machine.Shutdown();
```

| Member | Behavior |
|---|---|
| `new StateMachine(string name, IState rootState, Action<string> debugLog = null)` | Constructs the machine. `debugLog` receives diagnostic strings (defaults to `Console.WriteLine`). |
| `Initialize(object actor)` | Sets the actor, traverses the state tree calling each state's initializer, then enters the root. Must be called when `InitializationState == Offline`. If initialization throws, the machine attempts to shut down the partially-built tree and rethrows. If shutdown also throws, both are wrapped in an `AggregateException`. |
| `Shutdown()` | Exits the active states bottom-up and resets the tree. Requires `IsInitialized`. |
| `IsInitialized` | True while `InitializationState` is `Initialized` or `ShuttingDown`. |
| `InitializationState` | One of `Offline`, `Initializing`, `Initialized`, `ShuttingDown`. |
| `Name` | The display name passed to the constructor. |
| `actor` | The object passed to `Initialize`. |
| `logFlags` | Bit field of `LogFlags`. Set to `LogFlags.EnterExit` to log every state enter/exit via `debugLog`. |
| `WriteLine(string)` | Convenience that calls the configured `debugLog`. |

### State base classes

| Class | Use for |
|---|---|
| `SimpleState<TActor>` | Leaf state, parented under any parent. |
| `SimpleState<TActor, TParent>` | Leaf state with a strongly-typed `parent`. |
| `HierarchicalState<TActor>` | Composite state with one active child. |
| `HierarchicalState<TActor, TParent>` | Same, with a typed parent. |
| `OrthogonalState<TActor>` | Composite state with all children active in parallel. |
| `OrthogonalState<TActor, TParent>` | Same, with a typed parent. |

`TActor` is the actor class (`class`-constrained). `TParent` must implement `IParentState`. All `HierarchicalState` and `OrthogonalState` instances are themselves `IParentState`.

### State interfaces

| Interface | Purpose |
|---|---|
| `IStateBase` | Marker base for any state. Useful as a constraint when you want any state, not a specific entry shape. |
| `IState` | State that can be transitioned to without an argument. Declares `Enter()` with a default empty body — concrete states only need to define it when they have entry-time work to do. |
| `IState<TArg>` | State that can be transitioned to with an argument. Defines `Enter(TArg arg)`. A state can implement both, and can implement multiple `IState<T>` for different `T`. |
| `IParentState` | Marker for composite states. Implemented by `HierarchicalState` and `OrthogonalState`. |

A state typically implements `IState` (or `IState<T>`) **and** any number of custom receiver interfaces.

### State lifecycle hooks

| Hook | When |
|---|---|
| `IState.Enter()` / `IState<TArg>.Enter(TArg)` | Called every time the state becomes active. Receives the transition argument, if any. |
| `OnEnter()` (virtual) | Called after `Enter`. Use it for setup unrelated to the entry argument. |
| `OnExit()` (virtual) | Called when the state is left. |
| `OnInitialize()` (on `SimpleState`, virtual) | Called once during machine `Initialize`, before any state is entered. Use it for one-time setup that depends on `actor`. |
| `OnInitialize(out IState entrySubState, List<IStateBase> subStates)` (on `HierarchicalState`, abstract) | Declare child states and the default entry. Set `entrySubState = null` to decide entry at runtime. |
| `OnInitialize(List<IState> subStates)` (on `OrthogonalState`, abstract) | Declare the parallel branches. |
| `OnShutdown()` (virtual) | Called during machine shutdown. Reverse of `OnInitialize`. |

### Messages and dispatch

Define a receiver interface and a `Handler` delegate:

```csharp
public interface IHit { void OnHit(int damage); }
static readonly Handler<IHit, int> msg_hit = (receiver, dmg) => receiver.OnHit(dmg);
```

Send a message:

```csharp
machine.SendMessage(msg_hit, 10);
```

What happens:

1. Dispatch starts at the active *top state* (the deepest currently-active state).
2. The walk goes upward through parents, stopping at the root.
3. The first state that implements `IHit` has its handler invoked. After that the message is *consumed*.
4. A handler can call `PropagateMessage()` to mark the message un-consumed; bubbling then continues past this state.
5. In an `OrthogonalState`, each branch receives the message independently. The orthogonal state itself only receives the message if no branch consumed it.

If `SendMessage` is called while a previous dispatch is still in progress (for example, from inside a state's handler), the new message is **queued** and drained after the current one returns. Transitions are queued in a separate priority queue and are always processed before regular messages.

### Transitions

From inside a state:

```csharp
TransitTo(someState);
TransitTo(someState, arg);
```

`someState` must be a state inside the same machine; a debug assert checks this.

A transition behaves almost identically to a regular message:

1. It bubbles up from the current top state.
2. The first `HierarchicalState` whose child list contains the target claims it. That parent exits its current child and enters the target.
3. If no parent contains the target, the transition propagates past the root and has no effect.

The common case — transitioning to a sibling under the same hierarchical parent — is the simplest, and the most heavily optimized.

### `LogFlags`

```csharp
machine.logFlags = StateMachine.LogFlags.EnterExit;
```

`EnterExit` writes a line for every state entered and exited. More flags may be added in future versions.

### Exceptions

`StateMachine.InitializationException` is thrown when the state graph declared in `OnInitialize` is invalid — typically because a child state is `null` or already has a parent (i.e. the same instance was added twice). The exception is thrown from inside `Initialize`; the machine then attempts to shut down the partial tree and re-throws.

## ConcurrentStateMachine

`ConcurrentStateMachine` wraps `StateMachine` for use across threads. The underlying machine is **not** thread-safe; the wrapper enforces single-threaded execution while letting any thread enqueue messages.

### When to use it

- Producer threads (network, audio, file I/O) need to feed events into a machine that runs on a main / owner thread.
- A worker thread runs the machine itself, and other threads post work to it.

### API

```csharp
var concurrent = new ConcurrentStateMachine(
    name:                 "Network",
    rootState:            new State_Root(),
    debugLog:             Console.WriteLine,
    onMessagesAvailable:  () => mainThread.Wake(),
    logFlags:             StateMachine.LogFlags.None);

concurrent.Initialize(actor);

// from any thread:
concurrent.SendMessage(msg_packetReceived, packet);

// on the owner thread:
concurrent.HandleMessages();              // drain everything
concurrent.HandleMessages(maxCount: 32);  // drain up to N

concurrent.Shutdown();
```

### Behavior

- `SendMessage` is non-blocking and safe from any thread. It enqueues the message and, if the queue was empty, invokes `onMessagesAvailable` so the owner thread can wake up and call `HandleMessages`.
- `HandleMessages` drains the queue on the calling thread. Pass `-1` (the default) to drain everything, `0` to only check whether messages remain (returns `true` if so), or a positive number to cap work per call.
- `Initialize` and `Shutdown` take an internal write lock. They are safe to call concurrently with `SendMessage`, but block until any in-flight `HandleMessages` calls complete.
- `onMessagesAvailable` is called *at most once per idle-to-busy transition* — it fires only when the queue transitions from empty to non-empty, not on every message. Pair it with whatever wake mechanism your owner thread uses (a `ManualResetEventSlim`, a semaphore, a poll).

### Producer / consumer example

```csharp
using System.Threading;
using SeweralIdeas.StateMachines;

class NetworkBridge
{
    interface IPacket { void OnPacket(byte[] data); }
    static readonly Handler<IPacket, byte[]> msg_packet = (receiver, data) => receiver.OnPacket(data);

    readonly ManualResetEventSlim _wake = new(false);
    readonly ConcurrentStateMachine _machine;

    public NetworkBridge()
    {
        _machine = new ConcurrentStateMachine(
            "Network", new State_Root(), System.Console.WriteLine,
            onMessagesAvailable: () => _wake.Set());
    }

    // Called from any thread.
    public void Receive(byte[] data) => _machine.SendMessage(msg_packet, data);

    // Owner thread loop.
    public void Run(CancellationToken ct)
    {
        _machine.Initialize(this);
        try
        {
            while (!ct.IsCancellationRequested)
            {
                _wake.Wait(ct);
                _wake.Reset();
                _machine.HandleMessages();
            }
        }
        finally
        {
            _machine.Shutdown();
        }
    }
}
```

## Samples

The package ships with a **Door and Lever** sample (Unity). Import it from Package Manager → Samples. It demonstrates an orthogonal root with two parallel hierarchical branches, transitions with arguments, and message propagation for shared per-frame ticks.

## License

See [`LICENSE.md`](LICENSE.md).
