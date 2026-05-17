using System.Collections.Concurrent;
using System.Collections.Generic;

namespace SeweralIdeas.StateMachines
{
    internal class MessagePool<T> where T : Message, new()
    {
        private ConcurrentBag<T> _bag = new ConcurrentBag<T>();

        public T Take()
        {
            if (!_bag.TryTake(out T obj))
                obj = new T();

            return obj;
        }

        public void Return(T obj)
        {
            obj.Reset();
            _bag.Add(obj);
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