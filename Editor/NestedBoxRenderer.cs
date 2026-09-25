#nullable enable

using UnityEngine;

namespace SeweralIdeas.StateMachines.Editor
{
    /// <summary>
    /// Visitor that renders each state as a vertical box titled by name, with children laid
    /// out horizontally inside it. Shared by <see cref="StateMachineWindow"/> and the
    /// inspector-preview editors.
    /// </summary>
    internal sealed class NestedBoxRenderer : IStateVisitor
    {
        private static readonly GUILayoutOption[] ExpandHeight = { GUILayout.ExpandHeight(true) };

        public Color ActiveColor;
        public Color InactiveColor;

        public void BeginState(State state, int depth, bool isActive, bool hasChildren)
        {
            var prev = GUI.color;
            GUI.color = isActive ? ActiveColor : InactiveColor;
            GUILayout.BeginVertical(state.Name, GUI.skin.window, ExpandHeight);
            GUI.color = prev;

            if (hasChildren)
                GUILayout.BeginHorizontal();
        }

        public void EndState(State state, int depth, bool isActive, bool hasChildren)
        {
            if (hasChildren)
                GUILayout.EndHorizontal();
            GUILayout.EndVertical();
        }
    }
}
