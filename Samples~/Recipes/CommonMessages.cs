using SeweralIdeas.StateMachines;

namespace SeweralIdeas.StateMachines.Recipes
{
    /// <summary>
    /// A reusable set of cross-cutting messages and their handlers. Drop into your project
    /// (or use as inspiration), then <c>using static SeweralIdeas.StateMachines.Recipes.CommonMessages;</c>
    /// in every state file to share a single set of tick/update messages across all your state machines.
    ///
    /// Both handlers call <see cref="State.PropagateMessage"/> from the handler side, so each
    /// tick/update broadcasts to <em>every</em> state that implements the receiver — useful for
    /// periodic work that more than one state needs to react to. To consume the message at a
    /// single state instead, omit the propagation call by defining your own handler.
    /// </summary>
    public static class CommonMessages
    {
        public interface ITick   : IStateBase { void Tick(float deltaTime); }
        public interface IUpdate : IStateBase { void Update(float deltaTime); }

        public static readonly Handler<ITick, float> msg_tick = (receiver, dt) =>
        {
            receiver.Tick(dt);
            receiver.state.PropagateMessage();
        };

        public static readonly Handler<IUpdate, float> msg_update = (receiver, dt) =>
        {
            receiver.Update(dt);
            receiver.state.PropagateMessage();
        };
    }
}
