#nullable enable

namespace SeweralIdeas.StateMachines
{
    internal abstract class Message
    {
        public abstract void Destroy();
        public abstract void Dispatch(State state);
        public abstract object GetHandler();

        public abstract string ReceiverName { get; }

        internal abstract void Reset();
    }

    internal class Message<TReceiver> : Message where TReceiver : class
    {
        private static readonly MessagePool<Message<TReceiver>> Pool = new();
        public static Message<TReceiver> Create(Handler<TReceiver> handler)
        {
            var message = Pool.Take();
            message.Handler = handler;
            return message;
        }

        public override void Destroy()
        {
            Pool.Return(this);
        }

        public override void Dispatch(State state)
        {
            state.ReceiveMessage(Handler);
        }

        public override string ToString()
        {
            return $"Message {typeof(TReceiver).Name}()";
        }

        public override string ReceiverName => typeof(TReceiver).Name;

        public Handler<TReceiver> Handler { get; private set; } = null!;
        public override object GetHandler() => Handler;

        internal override void Reset()
        {
            // intentionally cleared so the pooled instance does not root the captured delegate;
            // the next Create call reassigns before any read
            Handler = null!;
        }
    }

    internal class Message<TReceiver, TArg> : Message where TReceiver : class
    {
        private static readonly MessagePool<Message<TReceiver, TArg>> Pool = new();
        public static Message<TReceiver, TArg> Create(Handler<TReceiver, TArg> handler, TArg arg)
        {
            var message = Pool.Take();
            message.Handler = handler;
            message.Arg0 = arg;
            return message;
        }

        public override void Destroy()
        {
            Pool.Return(this);
        }

        public override void Dispatch(State state)
        {
            state.ReceiveMessage(Handler, Arg0);
        }

        public override string ToString()
        {
            return $"Message {typeof(TReceiver).Name}({Arg0})";
        }

        public override string ReceiverName => typeof(TReceiver).Name;

        public Handler<TReceiver, TArg> Handler { get; private set; } = null!;
        public TArg Arg0 { get; private set; } = default!;
        public override object GetHandler() => Handler;

        internal override void Reset()
        {
            // intentionally cleared so the pooled instance does not root the captured delegate
            // or arg reference; the next Create call reassigns both before any read
            Handler = null!;
            Arg0 = default!;
        }
    }
}