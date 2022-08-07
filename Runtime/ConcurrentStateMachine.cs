using System;
using System.Collections.Concurrent;
using System.Threading;

namespace SeweralIdeas.StateMachines
{
    public class ConcurrentStateMachine
    {
        private readonly StateMachine m_machine;
        private readonly ReaderWriterLockSlim m_lock = new ReaderWriterLockSlim(LockRecursionPolicy.NoRecursion);
        private readonly ConcurrentQueue<Message> m_messageQueue = new ConcurrentQueue<Message>();
        private readonly Action m_onMessagesAvailable;
        private int m_hasMessages = 0;

        public ConcurrentStateMachine(string name, IState rootState, Action<string> debugLog,
            Action onMessagesAvailable, StateMachine.LogFlags logFlags = StateMachine.LogFlags.None)
        {
            m_lock.EnterWriteLock();
            try
            {
                m_onMessagesAvailable = onMessagesAvailable;
                m_machine = new StateMachine(name, rootState, debugLog);
                m_machine.logFlags = logFlags;
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        public void SendMessage<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg) where TReceiver : class
        {
            var msg = Message<TReceiver, TArg>.Create(handler, arg);
            m_messageQueue.Enqueue(msg);
            SetHasMessages();
        }

        public void SendMessage<TReceiver>(Handler<TReceiver> handler) where TReceiver : class
        {
            var msg = Message<TReceiver>.Create(handler);
            m_messageQueue.Enqueue(msg);
            SetHasMessages();
        }

        private void SetHasMessages()
        {
            bool didHaveMessagesAlready = Interlocked.Exchange(ref m_hasMessages, 1) > 0;
            if (!didHaveMessagesAlready)
            {
                m_onMessagesAvailable?.Invoke();
            }
        }

        public void Initialize(object actor)
        {
            m_lock.EnterWriteLock();
            try
            {
                m_machine.Initialize(actor);
            }
            finally
            {
                m_lock.ExitWriteLock();
                SetHasMessages();
            }
        }

        public void Shutdown()
        {
            m_lock.EnterWriteLock();
            try
            {
                m_machine.Shutdown();
            }
            finally
            {
                m_lock.ExitWriteLock();
            }
        }

        public bool HandleMessages(int maxCount = -1)
        {
            m_lock.EnterWriteLock();
            try
            {
                while (m_machine.IsInitialized)
                {
                    switch (maxCount)
                    {
                        case 0:
                            // return true if there are any more messages in the queue
                            return m_messageQueue.Count > 0;
                        case > 0:
                            // decrement maxCount and continue handling
                            maxCount--;
                            break;
                    }

                    if (!m_messageQueue.TryDequeue(out Message msg))
                    {
                        // if there are no more messages in the queue, return false
                        return false;
                    }

                    m_machine.SendMessage(msg);
                }

                return false; // this happens when the stateMachine shuts down
            }
            finally
            {
                m_lock.ExitWriteLock();

                // first, set m_hasMessages to 0 so other threads' SendMessage can start invoking the event
                Interlocked.Exchange(ref m_hasMessages, 0);

                // If I can see more messages (from my thread, that's fine)
                if (m_messageQueue.Count > 0)
                {
                    SetHasMessages();
                }

            }
        }
    }
}