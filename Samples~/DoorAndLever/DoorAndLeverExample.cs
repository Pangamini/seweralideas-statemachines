using System;
using System.Collections.Generic;
using SeweralIdeas.StateMachines;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

public class DoorAndLeverExample : MonoBehaviour
{
    [SerializeField] private Image _doorFill;
    [SerializeField] private Button _triggerButton;
    [SerializeField] private Button _leverSwitch;
    [SerializeField] private GameObject _leverIconOn;
    [SerializeField] private GameObject _leverIconOff;

    // Two parallel state machines composed at the actor level — what used to be an
    // OrthogonalState root is now explicit composition. Each machine has its own lifetime,
    // its own dispatch queue, and the actor routes incoming events to the relevant machine.
    private readonly StateMachine _leverMachine;
    private readonly StateMachine _doorMachine;

    private readonly InGameStateMachineOverlay _overlay = new();

    private interface IDontHandleThis { void Unhandled(); }
    private interface IUpdate { void Update(float deltaTime); }
    private interface IOnClickLever { void OnClickLever(); }
    private interface IOnClickButton { void OnClickButton(); }
    private interface ISetDoorDestination { void SetDoorDestination(bool shouldOpen); }

    private static readonly Handler<IUpdate, float>         msg_update             = (receiver, deltaTime) => receiver.Update(deltaTime);
    private static readonly Handler<IDontHandleThis>        msg_dontHandleThis     = receiver => receiver.Unhandled();
    private static readonly Handler<IOnClickLever>          msg_onClickLever       = receiver => receiver.OnClickLever();
    private static readonly Handler<IOnClickButton>         msg_onClickButton      = receiver => receiver.OnClickButton();
    private static readonly Handler<ISetDoorDestination, bool> msg_setDoorDestination = (receiver, shouldOpen) => receiver.SetDoorDestination(shouldOpen);

    private float _doorPosition;
    private float DoorPosition
    {
        get => _doorPosition;
        set
        {
            _doorPosition = value;
            _doorFill.fillAmount = 1 - value;
        }
    }

    protected DoorAndLeverExample()
    {
        _leverMachine = new("Lever", this, new State_LeverRoot(), Debug.Log);
        _doorMachine = new("Door", this,  new State_DoorRoot(),  Debug.Log);
    }

    void Awake()
    {
        _leverMachine.Logging = StateMachine.LogFlags.EnterExit;
        _doorMachine.Logging  = StateMachine.LogFlags.EnterExit;
        _triggerButton.onClick.AddListener(() => _leverMachine.SendMessage(msg_onClickButton));
        _leverSwitch.onClick.AddListener(() => _leverMachine.SendMessage(msg_onClickLever));
    }

    private void OnEnable()
    {
        try
        {
            Profiler.BeginSample("DoorAndLeverExample.OnEnable", this);
            _leverMachine.Initialize();
            _doorMachine.Initialize();
        }
        catch (Exception)
        {
            enabled = false;
            throw;
        }
        finally
        {
            Profiler.EndSample();
        }
    }

    private void OnDisable()
    {
        _leverMachine.Shutdown();
        _doorMachine.Shutdown();
    }

    private void Update()
    {
        // Update goes to the door (the moving states care). Probe message fans out to both.
        _doorMachine.SendMessage(msg_update, Time.deltaTime);
        _leverMachine.SendMessage(msg_dontHandleThis);
        _doorMachine.SendMessage(msg_dontHandleThis);
    }

    private void OnGUI()
    {
        var rect = new Rect(32, Screen.height * 0.5f, Screen.width - 64, Screen.height * 0.5f - 32);
        GUILayout.BeginArea(rect);
        _overlay.Draw(_leverMachine);
        _overlay.Draw(_doorMachine);
        GUILayout.EndArea();
    }

    // === Lever machine ===
    private class State_LeverRoot : State<DoorAndLeverExample>, IState
    {
        private readonly State_SwitchedOn  _switchedOn  = new();
        private readonly State_SwitchedOff _switchedOff = new();

        protected override void OnEnter()
        {
            Actor._leverIconOn.SetActive(false);
            Actor._leverIconOff.SetActive(false);
        }

        protected override void DeclareChildren(out IState? entrySubState, List<IStateBase> subStates)
        {
            entrySubState = _switchedOn;
            subStates.Add(_switchedOn);
            subStates.Add(_switchedOff);
        }

        private class State_SwitchedOn : State<DoorAndLeverExample, State_LeverRoot>, IState, IOnClickLever, IOnClickButton
        {
            void IState.Enter()                   => Actor._leverIconOn.SetActive(true);
            protected override void OnExit()      => Actor._leverIconOn.SetActive(false);
            void IOnClickLever.OnClickLever()     => TransitTo(Parent._switchedOff);
            void IOnClickButton.OnClickButton()   => Actor._doorMachine.SendMessage(msg_setDoorDestination, true);
        }

        private class State_SwitchedOff : State<DoorAndLeverExample, State_LeverRoot>, IState, IOnClickLever, IOnClickButton
        {
            void IState.Enter()                   => Actor._leverIconOff.SetActive(true);
            protected override void OnExit()      => Actor._leverIconOff.SetActive(false);
            void IOnClickLever.OnClickLever()     => TransitTo(Parent._switchedOn);
            void IOnClickButton.OnClickButton()   => Actor._doorMachine.SendMessage(msg_setDoorDestination, false);
        }
    }

    // === Door machine ===
    private class State_DoorRoot : State<DoorAndLeverExample>, IState
    {
        private readonly IState         _state_open;
        private readonly IState         _state_closed;
        private readonly IState<float>  _state_opening;
        private readonly IState<float>  _state_closing;

        public State_DoorRoot()
        {
            _state_open    = new State_Open();
            _state_closed  = new State_Closed();
            _state_opening = new State_Opening(_state_open);
            _state_closing = new State_Closing(_state_closed);
        }

        protected override void DeclareChildren(out IState? entrySubState, List<IStateBase> subStates)
        {
            entrySubState = _state_closed;
            subStates.Add(_state_open);
            subStates.Add(_state_closed);
            subStates.Add(_state_opening);
            subStates.Add(_state_closing);
        }

        private class State_Open : State<DoorAndLeverExample, State_DoorRoot>, IState, ISetDoorDestination
        {
            void IState.Enter() => Actor.DoorPosition = 1;
            void ISetDoorDestination.SetDoorDestination(bool shouldOpen)
            {
                if (shouldOpen) return;
                TransitTo(Parent._state_closing, 0.5f);
            }
        }

        private class State_Closed : State<DoorAndLeverExample, State_DoorRoot>, IState, ISetDoorDestination
        {
            void IState.Enter() => Actor.DoorPosition = 0;
            void ISetDoorDestination.SetDoorDestination(bool shouldOpen)
            {
                if (!shouldOpen) return;
                TransitTo(Parent._state_opening, 0.2f);
            }
        }

        private abstract class State_DoorMoving : State<DoorAndLeverExample, State_DoorRoot>, IState<float>, IUpdate
        {
            private readonly IState _transitTo;
            private readonly float _targetPosition;

            protected float _speed;
            protected State_DoorMoving(float targetPosition, IState transitTo)
            {
                _targetPosition = targetPosition;
                _transitTo = transitTo;
            }

            void IState<float>.Enter(float speed) => _speed = speed;

            void IUpdate.Update(float deltaTime)
            {
                PropagateMessage();
                Actor.DoorPosition = Mathf.MoveTowards(Actor.DoorPosition, _targetPosition, deltaTime * _speed);
                if (Math.Abs(Actor.DoorPosition - _targetPosition) < float.Epsilon)
                    TransitTo(_transitTo);
            }
        }

        private class State_Opening : State_DoorMoving, ISetDoorDestination
        {
            public State_Opening(IState transitTo) : base(1, transitTo) { }
            void ISetDoorDestination.SetDoorDestination(bool shouldOpen)
            {
                if (shouldOpen) _speed += 0.2f;
                else            TransitTo(Parent._state_closing, 0.5f);
            }
        }

        private class State_Closing : State_DoorMoving
        {
            public State_Closing(IState transitTo) : base(0, transitTo) { }
        }
    }
}
