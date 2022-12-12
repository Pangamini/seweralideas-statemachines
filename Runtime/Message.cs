using System;

namespace SeweralIdeas.StateMachines
{
    internal abstract class Message
    {
        public abstract void Destroy();
        public abstract void Dispatch(State state);
        public abstract object GetHandler();

        public abstract string ReceiverName { get; }
        
        internal virtual void Reset()
        {
        }
    }

    internal class Message<TReceiver> : Message where TReceiver : class
    {
        private static MessagePool<Message<TReceiver>> s_pool = new MessagePool<Message<TReceiver>>();
        public static Message<TReceiver> Create(Handler<TReceiver> handler)
        {
            var message = s_pool.Take();
            message.handler = handler;
            return message;
        }

        public override void Destroy()
        {
            s_pool.Return(this);
        }

        public override void Dispatch(State state)
        {
            state.ReceiveMessage(handler);
        }

        public override string ToString()
        {
            return $"Message {typeof(TReceiver).Name}()";
        }

        public override string ReceiverName => typeof(TReceiver).Name;

        public Handler<TReceiver> handler { get; private set; }
        public override object GetHandler() => handler;
    }

    internal class Message<TReceiver, TArg> : Message where TReceiver : class
    {
        private static MessagePool<Message<TReceiver, TArg>> s_pool = new MessagePool<Message<TReceiver, TArg>>();
        public static Message<TReceiver, TArg> Create(Handler<TReceiver, TArg> handler, TArg arg)
        {
            var message = s_pool.Take();
            message.handler = handler;
            message.arg0 = arg;
            return message;
        }

        public override void Destroy()
        {
            s_pool.Return(this);
        }

        public override void Dispatch(State state)
        {
            state.ReceiveMessage(handler, arg0);
        }

        public override string ToString()
        {
            return $"Message {typeof(TReceiver).Name}({arg0})";
        }

        public override string ReceiverName => typeof(TReceiver).Name;
        
        public Handler<TReceiver, TArg> handler { get; private set; }
        public TArg arg0 { get; private set; }
        public override object GetHandler() => handler;
    }
}