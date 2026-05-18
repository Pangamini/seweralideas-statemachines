#nullable enable

using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SeweralIdeas.StateMachines
{
    public class ConcurrentStateMachine
    {
        private readonly StateMachine _machine;
        private readonly ReaderWriterLockSlim _lock = new(LockRecursionPolicy.SupportsRecursion);
        private readonly ConcurrentQueue<Message> _messageQueue = new();
        private readonly Action? _onMessagesAvailable;
        private int _hasMessages;

        public ConcurrentStateMachine(string name, IState rootState, Action<string>? debugLog,
            Action? onMessagesAvailable, StateMachine.LogFlags logFlags = StateMachine.LogFlags.None)
        {
            _lock.EnterWriteLock();
            try
            {
                _onMessagesAvailable = onMessagesAvailable;
                _machine = new StateMachine(name, rootState, debugLog);
                _machine.logFlags = logFlags;
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public void SendMessage<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg) where TReceiver : class
        {
            var msg = Message<TReceiver, TArg>.Create(handler, arg);
            _messageQueue.Enqueue(msg);
            SetHasMessages();
        }

        public void SendMessage<TReceiver>(Handler<TReceiver> handler) where TReceiver : class
        {
            var msg = Message<TReceiver>.Create(handler);
            _messageQueue.Enqueue(msg);
            SetHasMessages();
        }

        private void SetHasMessages()
        {
            bool didHaveMessagesAlready = Interlocked.Exchange(ref _hasMessages, 1) > 0;
            if (!didHaveMessagesAlready)
            {
                _onMessagesAvailable?.Invoke();
            }
        }

        public void Initialize(object actor)
        {
            _lock.EnterWriteLock();
            try
            {
                _machine.Initialize(actor);
            }
            finally
            {
                _lock.ExitWriteLock();
                SetHasMessages();
            }
        }

        public void Shutdown()
        {
            _lock.EnterWriteLock();
            try
            {
                _machine.Shutdown();
            }
            finally
            {
                _lock.ExitWriteLock();
            }
        }

        public bool HandleMessages(int maxCount = -1)
        {
            _lock.EnterWriteLock();
            try
            {
                while (_machine.IsInitialized)
                {
                    switch (maxCount)
                    {
                        case 0:
                            // return true if there are any more messages in the queue
                            return _messageQueue.Count > 0;
                        case > 0:
                            // decrement maxCount and continue handling
                            maxCount--;
                            break;
                    }

                    if (!_messageQueue.TryDequeue(out Message? msg))
                    {
                        // if there are no more messages in the queue, return false
                        return false;
                    }

                    _machine.SendMessage(msg);
                }

                return false; // this happens when the stateMachine shuts down
            }
            finally
            {
                _lock.ExitWriteLock();

                // first, set _hasMessages to 0 so other threads' SendMessage can start invoking the event
                Interlocked.Exchange(ref _hasMessages, 0);

                // If I can see more messages (from my thread, that's fine)
                if (_messageQueue.Count > 0)
                {
                    SetHasMessages();
                }

            }
        }
    }
}