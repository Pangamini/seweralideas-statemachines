#nullable enable

namespace SeweralIdeas.StateMachines
{
    public delegate void Handler<TReceiver>(TReceiver receiver);
    public delegate void Handler<TReceiver, TArg>(TReceiver receiver, TArg arg);

    public delegate TResult FuncHandler<TReceiver, TResult>(TReceiver receiver);
    public delegate TResult FuncHandler<TReceiver, TArg, TResult>(TReceiver receiver, TArg arg);
}