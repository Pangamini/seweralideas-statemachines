#nullable enable

using System;
using System.Collections.Concurrent;

namespace SeweralIdeas.StateMachines
{
    public static class ReceiverTypeNameCache
    {
        private static readonly ConcurrentDictionary<Type, string> Cache = new();

        public static string GetName(Type type)
        {
            if(Cache.TryGetValue(type, out string? name))
                return name;

            name = type.Name;
            Cache.TryAdd(type, name);
            return name;
        }
    }
}
