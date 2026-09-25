#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SeweralIdeas.StateMachines.Editor
{
    /// <summary>
    /// Renders a component's <see cref="StateMachine"/> member(s) as an inspector preview
    /// (<see cref="UnityEditor.Editor.OnInteractivePreviewGUI"/>). Plain composable helper —
    /// not an <see cref="UnityEditor.Editor"/> subclass — so it can be added to any existing
    /// editor as a field regardless of what it already inherits from. See
    /// <see cref="StateMachinePreviewEditor"/> for a ready-made base class covering the case
    /// where the component has no other custom editor logic.
    /// </summary>
    public sealed class StateMachinePreviewGUI
    {
        private static readonly Color ActiveColor = Color.yellow;
        private static readonly Color InactiveColor = new(0.6f, 0.6f, 0.6f, 1f);

        private readonly List<StateMachineMemberScan.MachineEntry> _machines = new();
        private GUIContent[] _machineOptions = Array.Empty<GUIContent>();
        private int _selectedIndex;
        private Vector2 _scroll;

        /// <summary>Call from the owning editor's <c>OnEnable</c>.</summary>
        public void Scan(MonoBehaviour target)
        {
            _machines.Clear();
            StateMachineMemberScan.FindStateMachines(target, _machines);

            _machineOptions = new GUIContent[_machines.Count];
            for (int i = 0; i < _machines.Count; ++i)
                _machineOptions[i] = new GUIContent(_machines[i].DisplayName);

            if (_selectedIndex >= _machines.Count)
                _selectedIndex = 0;
        }

        public bool HasPreviewGUI => _machines.Count > 0;

        public bool RequiresConstantRepaint => _machines.Count > 0 && EditorApplication.isPlaying;

        public void OnPreviewSettings()
        {
            if (_machineOptions.Length > 1)
                _selectedIndex = EditorGUILayout.Popup(_selectedIndex, _machineOptions, EditorStyles.toolbarPopup, GUILayout.Width(140));
        }

        public void OnInteractivePreviewGUI(Rect rect, GUIStyle background)
        {
            // Deliberately not GUILayout: OnInteractivePreviewGUI isn't run through the
            // normal Layout+Repaint cycle GUILayout's auto-sizing depends on, so GUILayout
            // content here silently renders nothing. See StateMachineTreeLayout.
            if (_selectedIndex < 0 || _selectedIndex >= _machines.Count)
            {
                EditorGUI.HelpBox(rect, "No StateMachine found.", MessageType.Info);
                return;
            }

            var machine = _machines[_selectedIndex].GetMachine();
            if (machine == null)
            {
                EditorGUI.HelpBox(rect, "Field/property returned null.", MessageType.Info);
                return;
            }

            if (!machine.IsInitialized)
            {
                EditorGUI.HelpBox(rect, "StateMachine is not initialized.", MessageType.Info);
                return;
            }

            Vector2 contentSize = StateMachineTreeLayout.Measure(machine, out var sizes);
            var viewRect = new Rect(0f, 0f, Mathf.Max(contentSize.x, rect.width), Mathf.Max(contentSize.y, rect.height));
            _scroll = GUI.BeginScrollView(rect, _scroll, viewRect);
            StateMachineTreeLayout.Draw(machine, sizes, Vector2.zero, ActiveColor, InactiveColor);
            GUI.EndScrollView();
        }
    }
}
