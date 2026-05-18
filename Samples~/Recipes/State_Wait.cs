using SeweralIdeas.StateMachines;
using static SeweralIdeas.StateMachines.Recipes.CommonMessages;

namespace SeweralIdeas.StateMachines.Recipes
{
    /// <summary>Sent to a waiting state to extend or shorten its remaining time.</summary>
    public interface IExtendWait : IStateBase { void Extend(float seconds); }

    /// <summary>Sent to a waiting state to cancel it early.</summary>
    public interface ICancelWait : IStateBase { void Cancel(); }

    public static class WaitMessages
    {
        public static readonly Handler<IExtendWait, float> msg_extendWait = (receiver, seconds) => receiver.Extend(seconds);
        public static readonly Handler<ICancelWait>        msg_cancelWait = receiver => receiver.Cancel();
    }

    /// <summary>
    /// A reusable abstract state that counts down a timer and invokes <see cref="OnElapsed"/>
    /// when it reaches zero. Behaviors supported out of the box:
    /// <list type="bullet">
    ///   <item><description>Entry-time duration via <see cref="IState{TArg}.Enter"/>:
    ///   <c>TransitTo(myWaitState, seconds: 2.5f)</c>.</description></item>
    ///   <item><description>External extension/shortening via <see cref="IExtendWait"/>:
    ///   <c>machine.SendMessage(WaitMessages.msg_extendWait, 0.5f)</c>.</description></item>
    ///   <item><description>Cancellation via <see cref="ICancelWait"/>:
    ///   <c>machine.SendMessage(WaitMessages.msg_cancelWait)</c>.</description></item>
    /// </list>
    /// Drives off <see cref="CommonMessages.ITick"/>, so callers must pump
    /// <c>CommonMessages.msg_tick</c> on the state machine (typically from FixedUpdate or
    /// Update) for the timer to advance. Subclasses can implement additional receiver
    /// interfaces on top — the wait behavior keeps working underneath.
    /// </summary>
    public abstract class State_Wait<TActor, TParent>
        : SimpleState<TActor, TParent>, IState<float>, ITick, IExtendWait, ICancelWait
        where TActor  : class
        where TParent : IParentState
    {
        protected float _timeLeft;

        void IState<float>.Enter(float seconds) => _timeLeft = seconds;

        void IExtendWait.Extend(float seconds) => _timeLeft += seconds;
        void ICancelWait.Cancel()              => OnCancelled();

        void ITick.Tick(float dt)
        {
            _timeLeft -= dt;
            if (_timeLeft <= 0f)
                OnElapsed();
        }

        /// <summary>Called when the timer reaches zero naturally.</summary>
        protected abstract void OnElapsed();

        /// <summary>Called when the timer is cancelled via <see cref="ICancelWait"/>.
        /// Default behaviour falls through to <see cref="OnElapsed"/>; override to make
        /// cancellation a distinct path (e.g. abort back to idle without firing side effects).</summary>
        protected virtual void OnCancelled() => OnElapsed();
    }
}
