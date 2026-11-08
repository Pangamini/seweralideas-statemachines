#nullable enable

using System;
using System.Collections.Generic;
using System.Runtime.ExceptionServices;

namespace SeweralIdeas.StateMachines
{
    /// <summary>
    /// Messages for a <see cref="StateMachine"/> that are received now and handled later, in an order of priority. The owner of the
    /// machine calls <see cref="Flush"/> at the point of its update where they should be handled.
    /// <para>
    /// <see cref="StateMachine.SendMessage{TReceiver}(Handler{TReceiver})"/> acts at once, in whatever part of the update the sender
    /// happens to be in. Where several senders act on the same state, who wins is then a matter of who runs first. Posting to an inbox
    /// instead only records the message; the owner flushes it at one place, before it ticks the machine, with the more important
    /// messages first, so the outcome is the same whatever order the senders ran in.
    /// </para>
    /// <para>
    /// The inbox is not part of the machine: an owner creates one next to it, and the machine does not know about it. Nothing is
    /// handled unless <see cref="Flush"/> is called. A message posted after the flush is handled by the next one: the order only
    /// holds among the messages that were posted before it.
    /// </para>
    /// </summary>
    /// <typeparam name="TPriority">
    /// The priority of a message, usually an enum of the machine's owner. Compared with the comparer given to the constructor, or
    /// the default one: the greater value is handled first, and messages of equal priority in the order they were posted.
    /// </typeparam>
    public class DeferredInbox<TPriority>
    {
        private readonly struct Entry
        {
            public readonly TPriority Priority;
            public readonly Message   Message;

            public Entry(TPriority priority, Message message)
            {
                Priority = priority;
                Message  = message;
            }
        }

        private readonly IComparer<TPriority> _comparer;

        /// <summary>An inbox that orders messages by the default comparer of <typeparamref name="TPriority"/>.</summary>
        public DeferredInbox() : this(null)
        {
        }

        /// <summary>
        /// An inbox that orders messages by <paramref name="comparer"/>: what it ranks greater is handled first. Null for the default
        /// comparer of <typeparamref name="TPriority"/>.
        /// </summary>
        public DeferredInbox(IComparer<TPriority>? comparer)
        {
            _comparer = comparer ?? Comparer<TPriority>.Default;
        }

        // Kept sorted: the first is the next to be handled. Insertion is from the back, which keeps equal priorities in posting order.
        private List<Entry> _pending = new();
        // The messages being sent by a flush: _pending and this swap at its start, so messages posted meanwhile have an empty list to go to.
        private List<Entry> _flushing = new();
        private bool        _isFlushing;

        public int  Count      => _pending.Count;
        public bool HasPending => _pending.Count > 0;

        /// <summary>Records a message to be handled by the next <see cref="Flush"/>.</summary>
        public void Post<TReceiver>(Handler<TReceiver> handler, TPriority priority) where TReceiver : class
            => Insert(new Entry(priority, Message<TReceiver>.Create(handler)));

        /// <inheritdoc cref="Post{TReceiver}(Handler{TReceiver}, TPriority)"/>
        public void Post<TReceiver, TArg>(Handler<TReceiver, TArg> handler, TArg arg, TPriority priority) where TReceiver : class
            => Insert(new Entry(priority, Message<TReceiver, TArg>.Create(handler, arg)));

        private void Insert(Entry entry)
        {
            int index = _pending.Count;
            while(index > 0 && _comparer.Compare(_pending[index - 1].Priority, entry.Priority) < 0)
                index--;
            _pending.Insert(index, entry);
        }

        /// <summary>
        /// Sends the messages posted so far to the machine, the most important first, and empties the inbox. Each is handled as if
        /// it were sent with <c>SendMessage</c> at this point. A message that the machine's current state does not handle is dropped,
        /// as it would be by <c>SendMessage</c>. Messages posted while the flush runs (by the handlers) wait for the next one.
        /// <para>
        /// A machine that is not running has nobody to handle them: they are dropped.
        /// </para>
        /// <para>
        /// If a handler throws, the rest are still sent, and the first exception is thrown after all of them.
        /// </para>
        /// </summary>
        public void Flush(StateMachine machine)
        {
            // A flush from inside a handler of the flush: what is posted waits for the next one, as for any other post during it
            if(_isFlushing || _pending.Count == 0)
                return;

            if(machine.InitializationState != StateMachine.InitState.Initialized)
            {
                Clear();
                return;
            }

            // _flushing is empty here (a flush always leaves it so): messages posted by the handlers go to the list that was in it
            (_pending, _flushing) = (_flushing, _pending);

            _isFlushing = true;
            ExceptionDispatchInfo? firstException = null;
            try
            {
                foreach(Entry entry in _flushing)
                {
                    try
                    {
                        machine.SendMessage(entry.Message); // the machine takes the message over: destroys it once it was handled
                    }
                    catch(Exception e)
                    {
                        firstException ??= ExceptionDispatchInfo.Capture(e);
                    }
                }
            }
            finally
            {
                _flushing.Clear();
                _isFlushing = false;
            }

            firstException?.Throw();
        }

        /// <summary>Drops the messages posted so far, without handling them. For when the machine's owner is reset or destroyed.</summary>
        public void Clear()
        {
            foreach(Entry entry in _pending)
                entry.Message.Destroy();
            _pending.Clear();
        }
    }

    /// <summary>A <see cref="DeferredInbox{TPriority}"/> that prioritizes messages with plain numbers.</summary>
    public sealed class DeferredInbox : DeferredInbox<int>
    {
        public DeferredInbox()
        {
        }

        public DeferredInbox(IComparer<int>? comparer) : base(comparer)
        {
        }
    }
}
