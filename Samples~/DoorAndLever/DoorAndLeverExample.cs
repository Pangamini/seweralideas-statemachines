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

    private StateMachine _stateMachine;
    private readonly InGameStateMachineOverlay _overlay = new();

    private interface IDontHandleThis { void Unhandled(); }
    private interface IUpdate { void Update(float deltaTime); }
    private interface IOnClickLever { void OnClickLever(); }
    private interface IOnClickButton { void OnClickButton(); }
    private interface ISetDoorDestination { void SetDoorDestination(bool shouldOpen); }

    private static readonly Handler<IUpdate, float> msg_update = (receiver, deltaTime) => receiver.Update(deltaTime);
    private static readonly Handler<IDontHandleThis> msg_dontHandleThis = receiver => receiver.Unhandled();
    private static readonly Handler<IOnClickLever> msg_onClickLever = receiver => receiver.OnClickLever();
    private static readonly Handler<IOnClickButton> msg_onClickButton = receiver => receiver.OnClickButton();
    private static readonly Handler<ISetDoorDestination, bool> msg_setDoorDestination = (receiver, shouldOpen) => receiver.SetDoorDestination(shouldOpen);

    private float _doorPosition;


    private float DoorPosition
    {
        get => _doorPosition;
        set
        {
            _doorPosition = value;
            _doorFill.fillAmount =  1 - value;
        }
    }
    
    void Awake()
    {
        _stateMachine = new StateMachine("DoorAndLever", new State_Root(), Debug.Log);
        _stateMachine.logFlags = StateMachine.LogFlags.EnterExit;
        _triggerButton.onClick.AddListener( () => _stateMachine.SendMessage(msg_onClickButton));
        _leverSwitch.onClick.AddListener(OnClickLever);
    }

    private void OnClickLever()
    {
         _stateMachine.SendMessage(msg_onClickLever);
    }

    private void OnEnable()
    {
        try
        {
            Profiler.BeginSample("DoorAndLeverExample.OnEnable", this);
            _stateMachine.Initialize(this);
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
        _stateMachine.Shutdown();
    }

    private void Update()
    {
        _stateMachine.SendMessage(msg_update, Time.deltaTime);
        _stateMachine.SendMessage(msg_dontHandleThis);
    }

    private class State_Root : OrthogonalState<DoorAndLeverExample>, IState
    {
        protected override void OnInitialize(List<IState> subStates)
        {
            subStates.Add(new State_LeverRoot());
            subStates.Add(new State_DoorRoot());
        }

        void IState.Enter()
        {
            
        }

        private class State_LeverRoot : HierarchicalState<DoorAndLeverExample, State_Root>, IState
        {
            private readonly State_SwitchedOn _state_switchedOn = new State_SwitchedOn();
            private readonly State_SwitchedOff _state_switchedOff = new State_SwitchedOff();

            protected override void OnEnter()
            {
                actor._leverIconOn.SetActive(false);
                actor._leverIconOff.SetActive(false);
            }

            protected override void OnInitialize(out IState entrySubState, List<IStateBase> subStates)
            {
                entrySubState = _state_switchedOn;
                subStates.Add(_state_switchedOn);
                subStates.Add(_state_switchedOff);
            }

            private class State_SwitchedOn : SimpleState<DoorAndLeverExample, State_LeverRoot>, IState, IOnClickLever, IOnClickButton
            {
                void IState.Enter() => actor._leverIconOn.SetActive(true);
                protected override void OnExit() => actor._leverIconOn.SetActive(false);
                void IOnClickLever.OnClickLever() => TransitTo(parent._state_switchedOff);
                void IOnClickButton.OnClickButton() => stateMachine.SendMessage(msg_setDoorDestination, true);
            }
            
            private class State_SwitchedOff : SimpleState<DoorAndLeverExample, State_LeverRoot>, IState, IOnClickLever, IOnClickButton
            {
                void IState.Enter() => actor._leverIconOff.SetActive(true);
                protected override void OnExit() => actor._leverIconOff.SetActive(false);
                void IOnClickLever.OnClickLever() => TransitTo(parent._state_switchedOn);
                void IOnClickButton.OnClickButton() => stateMachine.SendMessage(msg_setDoorDestination, false);
            }

            void IState.Enter() {}
        }

        private class State_DoorRoot : HierarchicalState<DoorAndLeverExample>, IState
        {
            private IState _state_open;
            private IState _state_closed;
            private IState<float> _state_opening;
            private IState<float> _state_closing;

            public State_DoorRoot()
            {
                _state_open    = new State_Open();
                _state_closed  = new State_Closed();
                _state_opening = new State_Opening(_state_open);
                _state_closing = new State_Closing(_state_closed);
            }
            
            protected override void OnInitialize(out IState entrySubState, List<IStateBase> stateBases)
            {
                entrySubState = _state_closed;
                stateBases.Add(_state_open);
                stateBases.Add(_state_closed);
                stateBases.Add(_state_opening);
                stateBases.Add(_state_closing);
            }

            private class State_Open : SimpleState<DoorAndLeverExample, State_DoorRoot>, IState, ISetDoorDestination
            {
                void IState.Enter()
                {
                    actor.DoorPosition = 1;
                }

                void ISetDoorDestination.SetDoorDestination(bool shouldOpen)
                {
                    if (shouldOpen)
                        return;
                    
                    TransitTo(parent._state_closing, 0.5f);
                }
            }

            private class State_Closed : SimpleState<DoorAndLeverExample, State_DoorRoot>, IState, ISetDoorDestination
            {
                void IState.Enter()
                {
                    actor.DoorPosition = 0;
                }
                
                void ISetDoorDestination.SetDoorDestination(bool shouldOpen)
                {
                    if (!shouldOpen)
                        return;
                    
                    TransitTo(parent._state_opening, 0.2f);
                }
            }

            private abstract class State_DoorMoving : SimpleState<DoorAndLeverExample, State_DoorRoot>, IState<float>, IUpdate
            {
                private IState _transitTo;
                private float _targetPositiom;

                protected float _speed;
                protected State_DoorMoving(float targetPositiom, IState transitTo)
                {
                    _targetPositiom = targetPositiom;
                    _transitTo = transitTo;
                }

                void IState<float>.Enter(float speed)
                {
                    _speed = speed;
                }

                void IUpdate.Update(float deltaTime)
                {
                    PropagateMessage();
                    actor.DoorPosition = Mathf.MoveTowards(actor.DoorPosition, _targetPositiom, deltaTime * _speed);
                    if (Math.Abs(actor.DoorPosition - _targetPositiom) < float.Epsilon)
                    {
                        TransitTo(_transitTo);
                    }
                }

            }

            private class State_Opening : State_DoorMoving, ISetDoorDestination
            {
                public State_Opening(IState transitTo) : base(1, transitTo)
                {
                }

                void ISetDoorDestination.SetDoorDestination(bool shouldOpen)
                {
                    if (shouldOpen)
                    {
                        _speed += 0.2f;
                    }
                    else
                    {
                        TransitTo(parent._state_closing, 0.5f);
                    }
                }
            }

            private class State_Closing : State_DoorMoving
            {
                public State_Closing(IState transitTo) : base(0, transitTo)
                {
                }
            }

            void IState.Enter() {}
        }
    }

    private void OnGUI()
    {
        var rect = new Rect(32, Screen.height * 0.5f, Screen.width - 64, Screen.height * 0.5f - 32);
        GUILayout.BeginArea(rect);
        _overlay.Draw(_stateMachine);
        GUILayout.EndArea();
    }
}
