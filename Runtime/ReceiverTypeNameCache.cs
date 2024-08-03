using System;
using System.Collections.Concurrent;
namespace SeweralIdeas.StateMachines
{
    public static class ReceiverTypeNameCache
    {
        private static readonly ConcurrentDictionary<Type, string> s_cache = new();
        
        public static string GetName(Type type)
        {
            if(s_cache.TryGetValue(type, out string name))
                return name;

            name = type.Name;
            s_cache.TryAdd(type, name);
            return name;
        }
    }
}
