#nullable enable

namespace SeweralIdeas.StateMachines
{
    public delegate void Handler<TReceiver>(TReceiver receiver);
    public delegate void Handler<TReceiver, TArg>(TReceiver receiver, TArg arg);
}