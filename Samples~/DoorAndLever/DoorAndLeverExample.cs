using System;
using System.Collections.Generic;
using SeweralIdeas.StateMachines;
using UnityEngine;
using UnityEngine.Profiling;
using UnityEngine.UI;

public class DoorAndLeverExample : MonoBehaviour
{
    [SerializeField] private Image m_doorFill;
    [SerializeField] private Button m_triggerButton;
    [SerializeField] private Button m_leverSwitch;
    [SerializeField] private GameObject m_leverIconOn;
    [SerializeField] private GameObject m_leverIconOff;
    
    [Header("GUI")]
    [SerializeField] private RectTransform m_guiRect;
    
    [SerializeField] private StateMachine.GUISettings m_guiSettings;
    private StateMachine m_stateMachine;

    private interface IDontHandleThis { void Unhandled(); }
    private interface IUpdate { void Update(float deltaTime); }
    private interface IOnClickLever { void OnClickLever(); }
    private interface IOnClickButton { void OnClickButton(); }
    private interface ISetDoorDestination { void SetDoorDestination(bool shouldOpen); }

    private static readonly Handler<IUpdate, float> msg_update = (handler, deltaTime) => handler.Update(deltaTime);
    private static readonly Handler<IDontHandleThis> msg_dontHandleThis = (handler) => handler.Unhandled();
    private static readonly Handler<IOnClickLever> msg_onClickLever = handler => handler.OnClickLever();
    private static readonly Handler<IOnClickButton> msg_onClickButton = handler => handler.OnClickButton();
    private static readonly Handler<ISetDoorDestination, bool> msg_setDoorDestination = (handler, shouldOpen) => handler.SetDoorDestination(shouldOpen);

    private float m_doorPosition;


    private float DoorPosition
    {
        get => m_doorPosition;
        set
        {
            m_doorPosition = value;
            m_doorFill.fillAmount =  1 - value;
        }
    }
    
    void Awake()
    {
        m_stateMachine = new StateMachine("DoorAndLever", new State_Root(), Debug.Log);
        m_stateMachine.logFlags = StateMachine.LogFlags.EnterExit;
        m_triggerButton.onClick.AddListener( () => m_stateMachine.SendMessage(msg_onClickButton));
        m_leverSwitch.onClick.AddListener(OnClickLever);
    }

    private void OnClickLever()
    {
         m_stateMachine.SendMessage(msg_onClickLever);
    }

    private void OnEnable()
    {
        try
        {
            Profiler.BeginSample("DoorAndLeverExample.OnEnable", this);
            m_stateMachine.Initialize(this);
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
        m_stateMachine.Shutdown();
    }

    private void Update()
    {
        m_stateMachine.SendMessage(msg_update, Time.deltaTime);
        m_stateMachine.SendMessage(msg_dontHandleThis);
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
            private readonly State_SwitchedOn m_state_switchedOn = new State_SwitchedOn();
            private readonly State_SwitchedOff m_state_switchedOff = new State_SwitchedOff();

            protected override void OnEnter()
            {
                actor.m_leverIconOn.SetActive(false);
                actor.m_leverIconOff.SetActive(false);
            }

            protected override void OnInitialize(out IState entrySubState, List<IStateBase> subStates)
            {
                entrySubState = m_state_switchedOn;
                subStates.Add(m_state_switchedOn);
                subStates.Add(m_state_switchedOff);
            }

            private class State_SwitchedOn : SimpleState<DoorAndLeverExample, State_LeverRoot>, IState, IOnClickLever, IOnClickButton
            {
                void IState.Enter() => actor.m_leverIconOn.SetActive(true);
                protected override void OnExit() => actor.m_leverIconOn.SetActive(false);
                void IOnClickLever.OnClickLever() => TransitTo(parent.m_state_switchedOff);
                void IOnClickButton.OnClickButton() => stateMachine.SendMessage(msg_setDoorDestination, true);
            }
            
            private class State_SwitchedOff : SimpleState<DoorAndLeverExample, State_LeverRoot>, IState, IOnClickLever, IOnClickButton
            {
                void IState.Enter() => actor.m_leverIconOff.SetActive(true);
                protected override void OnExit() => actor.m_leverIconOff.SetActive(false);
                void IOnClickLever.OnClickLever() => TransitTo(parent.m_state_switchedOn);
                void IOnClickButton.OnClickButton() => stateMachine.SendMessage(msg_setDoorDestination, false);
            }

            void IState.Enter() {}
        }

        private class State_DoorRoot : HierarchicalState<DoorAndLeverExample>, IState
        {
            private IState m_state_open;
            private IState m_state_closed;
            private IState<float> m_state_opening;
            private IState<float> m_state_closing;

            public State_DoorRoot()
            {
                m_state_open    = new State_Open();
                m_state_closed  = new State_Closed();
                m_state_opening = new State_Opening(m_state_open);
                m_state_closing = new State_Closing(m_state_closed);
            }
            
            protected override void OnInitialize(out IState entrySubState, List<IStateBase> stateBases)
            {
                entrySubState = m_state_closed;
                stateBases.Add(m_state_open);
                stateBases.Add(m_state_closed);
                stateBases.Add(m_state_opening);
                stateBases.Add(m_state_closing);
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
                    
                    TransitTo(parent.m_state_closing, 0.5f);
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
                    
                    TransitTo(parent.m_state_opening, 0.2f);
                }
            }

            private abstract class State_DoorMoving : SimpleState<DoorAndLeverExample, State_DoorRoot>, IState<float>, IUpdate
            {
                private IState m_transitTo;
                private float m_targetPositiom;

                [ShowField]
                protected float m_speed;
                protected State_DoorMoving(float targetPositiom, IState transitTo)
                {
                    m_targetPositiom = targetPositiom;
                    m_transitTo = transitTo;
                }

                void IState<float>.Enter(float speed)
                {
                    m_speed = speed;
                }

                void IUpdate.Update(float deltaTime)
                {
                    PropagateMessage();
                    actor.DoorPosition = Mathf.MoveTowards(actor.DoorPosition, m_targetPositiom, deltaTime * m_speed);
                    if (Math.Abs(actor.DoorPosition - m_targetPositiom) < float.Epsilon)
                    {
                        TransitTo(m_transitTo);
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
                        m_speed += 0.2f;
                    }
                    else
                    {
                        TransitTo(parent.m_state_closing, 0.5f);
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
        Rect rect = new Rect(32, Screen.height * 0.5f, Screen.width - 64, Screen.height * 0.5f - 32);
        GUILayout.BeginArea(rect);
        m_stateMachine.OnGUI(m_guiSettings);
        GUILayout.EndArea();
    }
}
