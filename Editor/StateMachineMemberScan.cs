#nullable enable

using System;
using System.Collections.Generic;
using System.Reflection;
using UnityEngine;

namespace SeweralIdeas.StateMachines.Editor
{
    /// <summary>
    /// Reflects over a <see cref="MonoBehaviour"/>'s type hierarchy (up to but excluding
    /// <see cref="MonoBehaviour"/> itself) to find every field/property whose type is a
    /// <see cref="StateMachine"/>. Shared by <see cref="StateMachineWindow"/> and the
    /// inspector-preview editors.
    /// </summary>
    internal static class StateMachineMemberScan
    {
        public readonly struct MachineEntry
        {
            private readonly object _component;
            private readonly Func<object, object?> _getter;
            public string DisplayName { get; }

            public MachineEntry(object component, string memberName, Func<object, object?> getter)
            {
                _component = component;
                _getter = getter;
                DisplayName = $"{component.GetType().Name}.{memberName}";
            }

            public StateMachine? GetMachine() => _getter(_component) as StateMachine;
        }

        private static readonly Dictionary<Type, MemberEntry[]> TypeCache = new();

        private readonly struct MemberEntry
        {
            public readonly string Name;
            public readonly Func<object, object?> Getter;

            public MemberEntry(string name, Func<object, object?> getter)
            {
                Name = name;
                Getter = getter;
            }
        }

        public static void FindStateMachines(MonoBehaviour component, List<MachineEntry> output)
        {
            foreach (var member in GetMembersForType(component.GetType()))
                output.Add(new MachineEntry(component, member.Name, member.Getter));
        }

        private static MemberEntry[] GetMembersForType(Type componentType)
        {
            if (TypeCache.TryGetValue(componentType, out var cached))
                return cached;

            var members = new List<MemberEntry>();
            const BindingFlags flags = BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly;

            var type = componentType;
            while (type != null && type != typeof(MonoBehaviour))
            {
                foreach (var member in type.GetMembers(flags))
                {
                    Type? memberType = null;
                    Func<object, object?>? getter = null;

                    if (member is FieldInfo field)
                    {
                        memberType = field.FieldType;
                        getter = field.GetValue;
                    }
                    else if (member is PropertyInfo prop && prop.GetMethod != null && prop.GetIndexParameters().Length == 0)
                    {
                        memberType = prop.PropertyType;
                        getter = prop.GetValue;
                    }

                    if (getter != null && memberType != null && typeof(StateMachine).IsAssignableFrom(memberType))
                    {
                        members.Add(new MemberEntry(member.Name, getter));
                    }
                }
                type = type.BaseType;
            }

            var result = members.ToArray();
            TypeCache[componentType] = result;
            return result;
        }
    }
}
