using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Reflection;

namespace SeweralIdeas.StateMachines
{

    [AttributeUsage(AttributeTargets.Field)]
    public class ShowField : Attribute { }

    public class StateDebugInfo
    {
        private readonly static ConcurrentDictionary<Type, StateDebugInfo> s_infoDict = new ConcurrentDictionary<Type, StateDebugInfo>();

        public readonly Type stateType;

        public struct Field
        {
            public FieldInfo fieldInfo;
            public bool show;
        }

        private readonly Field[] m_fields;

        private StateDebugInfo(Type type)
        {
            stateType = type;

            var list = new List<Field>();

            while (type != null)
            {
                var infos = type.GetFields(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly | BindingFlags.Instance);
                foreach (var info in infos)
                {
                    list.Add(new Field()
                    {
                        fieldInfo = info,
                        show = info.GetCustomAttribute<ShowField>() != null
                    });
                }
                type = type.BaseType;
            }
            m_fields = list.ToArray();
        }

        public Field this[int index] => m_fields[index];

        public int Count => m_fields.Length;


        public static StateDebugInfo Get(Type type)
        {
            if (!s_infoDict.TryGetValue(type, out var info))
            {
                info = new StateDebugInfo(type);
                return s_infoDict.GetOrAdd(type, info);
            }

            return info;
        }

    }
}
