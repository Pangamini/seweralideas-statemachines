using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;
using SeweralIdeas.StateMachines;
using UnityEngine.Events;

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

    private interface IUpdate { void Update(float deltaTime); }
    private interface IOnClickLever { void OnClickLever(); }
    private interface IOnClickButton { void OnClickButton(); }
    private interface ISetDoorDestination { void SetDoorDestination(bool shouldOpen); }

    private static readonly Handler<IUpdate, float> msg_update = (IUpdate handler, float deltaTime) => handler.Update(deltaTime);
    private static readonly Handler<IOnClickLever> msg_onClickLever = (IOnClickLever handler) => handler.OnClickLever();
    private static readonly Handler<IOnClickButton> msg_onClickButton = (IOnClickButton handler) => handler.OnClickButton();
    private static readonly Handler<ISetDoorDestination, bool> msg_setDoorDestination = (ISetDoorDestination handler, bool shouldOpen) => handler.SetDoorDestination(shouldOpen);

    private float m_doorPosition;


    public float DoorPosition
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
        m_stateMachine = new StateMachine("DoorAndLever", new State_Root());
        m_triggerButton.onClick.AddListener( () => m_stateMachine.SendMessage(msg_onClickButton));
        m_leverSwitch.onClick.AddListener(OnClickLever);
    }

    private void OnClickLever()
    {
         m_stateMachine.SendMessage(msg_onClickLever);
    }

    private void OnEnable()
    {
        m_stateMachine.Initialize(this);
    }

    private void OnDisable()
    {
        m_stateMachine.Shutdown();
    }

    private void Update()
    {
        m_stateMachine.SendMessage(msg_update, Time.deltaTime);
    }

    private class State_Root : OrthogonalState<DoorAndLeverExample>, IState
    {
        protected override void OnInitialize()
        {
           _ = new State_LeverRoot() { parent = this };
           _ = new State_DoorRoot() { parent = this };
        }

        void IState.Enter()
        {
            
        }

        private class State_LeverRoot : HierarchicalState<DoorAndLeverExample, State_Root>, IState
        {
            private State_SwitchedOn m_state_switchedOn;
            private State_SwitchedOff m_state_switchedOff;

            protected override void OnEnter()
            {
                actor.m_leverIconOn.SetActive(false);
                actor.m_leverIconOff.SetActive(false);
            }

            protected override void OnInitialize()
            {
                m_state_switchedOn = new State_SwitchedOn() { parent = this };
                m_state_switchedOff = new State_SwitchedOff() { parent = this };
                entrySubState = m_state_switchedOn;
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
        
        private class State_DoorRoot : HierarchicalState<DoorAndLeverExample, State_Root>, IState
        {
            private IState m_state_open;
            private IState m_state_closed;
            private IState m_state_opening;
            private IState m_state_closing;

            protected override void OnInitialize()
            {
                m_state_open = new State_Open() { parent = this };
                m_state_closed = new State_Closed() { parent = this };
                m_state_opening = new State_Opening(m_state_open) { parent = this };
                m_state_closing = new State_Closing(m_state_closed) { parent = this };
                entrySubState = m_state_closed;
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
                    
                    TransitTo(parent.m_state_closing);
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
                    
                    TransitTo(parent.m_state_opening);
                }
            }

            private abstract class State_DoorMoving : SimpleState<DoorAndLeverExample, State_DoorRoot>, IState, IUpdate
            {
                private IState m_transitTo;
                private float m_targetPositiom;

                public virtual float Speed => 0.5f;
                
                protected State_DoorMoving(float targetPositiom, IState transitTo)
                {
                    m_targetPositiom = targetPositiom;
                    m_transitTo = transitTo;
                }

                void IState.Enter() {}

                void IUpdate.Update(float deltaTime)
                {
                    PropagateMessage();
                    actor.DoorPosition = Mathf.MoveTowards(actor.DoorPosition, m_targetPositiom, deltaTime * Speed);
                    if (actor.DoorPosition == m_targetPositiom)
                    {
                        TransitTo(m_transitTo);
                    }
                }

            }

            private class State_Opening : State_DoorMoving, ISetDoorDestination
            {
                private float m_speed;
                public override float Speed => m_speed;

                protected override void OnEnter()
                {
                    base.OnEnter();
                    m_speed = 0.2f;
                }

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
                        TransitTo(parent.m_state_closing);
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
