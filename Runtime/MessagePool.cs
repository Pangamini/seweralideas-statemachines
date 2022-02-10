using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SeweralIdeas.StateMachines
{
    abstract public class StackPoolBase
    {
        abstract public System.Type GetElementType();
        abstract public int pooledCount { get; }

#if DEBUG
        private static HashSet<System.WeakReference> s_allStackPools = new HashSet<System.WeakReference>();

        private System.WeakReference m_weakRef;
        public StackPoolBase()
        {
            lock (s_allStackPools)
            {
                m_weakRef = new System.WeakReference(this);
                s_allStackPools.Add(m_weakRef);
            }
        }

        public static void VisitAllPools(System.Action<StackPoolBase> visitor)
        {
            lock (s_allStackPools)
            {
                foreach (var weakRef in s_allStackPools)
                {
                    var pool = weakRef.Target as StackPoolBase;
                    if (pool != null)
                        visitor(pool);
                }
            }
        }

        ~StackPoolBase()
        {
            lock (s_allStackPools)
            {
                s_allStackPools.Remove(m_weakRef);
            }
        }
#endif
    }

    /*
    General usage stack-like pool. Use to emulate stack allocation of non-value type objects.
    DO NOT keep the reference to popped object after it's been pushed back to the pool.
    */

    abstract public class StackPool<T> : StackPoolBase where T : class
    {
        private ConcurrentBag<T> m_bag = new ConcurrentBag<T>();

        public override int pooledCount
        { get { return m_bag.Count; } }

        override public System.Type GetElementType()
        {
            return typeof(T);
        }

        public T Take()
        {
            if (!m_bag.TryTake(out T obj))
                obj = Alloc();
            Prepare(obj);
            return obj;
        }

        public void Return(T obj)
        {
            Finalize(obj);
            m_bag.Add(obj);
        }

        protected abstract T Alloc();

        /////////////////////////////////////////////////////////////////////
        /// called when object returns to the pool
        /// Clear the object here or in Prepare
        /// Optionally release the object internal lock

        protected virtual void Finalize(T obj) { }

        /////////////////////////////////////////////////////////////////////
        /// called before object is returned by Pop()
        /// clear the object here or in Finalize 
        /// Optionally occupy the object's internal lock

        protected virtual void Prepare(T obj) { }
    }

    public class BasicStackPool<T> : StackPool<T> where T : class, new()
    {
        protected override T Alloc()
        {
            return new T();
        }

        /// <summary>
        /// Used to ensure that AOT version exists when using IL2CPP
        /// </summary>
#if UNITY_EDITOR || UNITY_STANDALONE
        [UnityEngine.Scripting.Preserve]
#endif
        public static void InitializeType()
        {

        }
    }
}