#nullable enable

using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace SeweralIdeas.StateMachines.Editor
{
    /// <summary>
    /// Editor window that lists the state machines on the selected GameObject and renders
    /// each one as nested boxes (recreating the package's original IMGUI layout) using
    /// <see cref="StateMachine.Visit"/>.
    /// </summary>
    public class StateMachineWindow : EditorWindow
    {
        [SerializeField] private bool _lockSelection;
        [SerializeField] private GameObject? _selectedGameObject;
        [SerializeField] private Color _activeColor = Color.yellow;
        [SerializeField] private Color _inactiveColor = new(0.6f, 0.6f, 0.6f, 1f);
        [SerializeField] private bool _showSettings;

        [NonSerialized] private readonly List<StateMachineMemberScan.MachineEntry> _machines = new();
        [NonSerialized] private GUIContent[] _machineOptions = Array.Empty<GUIContent>();
        [NonSerialized] private int _selectedMachineIndex;
        [NonSerialized] private GameObject? _scannedGameObject;
        [NonSerialized] private GUIStyle? _toolbarButtonStyle;
        [NonSerialized] private NestedBoxRenderer? _renderer;

        [MenuItem("Window/Analysis/StateMachine Debugger")]
        private static void Init()
        {
            var window = CreateWindow<StateMachineWindow>("StateMachine");
            window.Show();
        }

        protected void OnEnable()
        {
            EditorApplication.playModeStateChanged += OnPlayStateChanged;
            Selection.selectionChanged += RefreshSelection;
            RefreshSelection();
        }

        protected void OnDisable()
        {
            EditorApplication.playModeStateChanged -= OnPlayStateChanged;
            Selection.selectionChanged -= RefreshSelection;
        }

        private void OnPlayStateChanged(PlayModeStateChange change)
        {
            if (change is PlayModeStateChange.EnteredEditMode or PlayModeStateChange.EnteredPlayMode)
                RefreshSelection();
        }

        private void RefreshSelection()
        {
            if (!_lockSelection && Selection.activeGameObject != null)
                _selectedGameObject = Selection.activeGameObject;

            if (_scannedGameObject == _selectedGameObject)
                return;

            _scannedGameObject = _selectedGameObject;
            _machines.Clear();

            if (_selectedGameObject != null)
            {
                foreach (var component in _selectedGameObject.GetComponents<MonoBehaviour>())
                {
                    if (component != null)
                        StateMachineMemberScan.FindStateMachines(component, _machines);
                }
            }

            _machineOptions = new GUIContent[_machines.Count];
            for (int i = 0; i < _machines.Count; ++i)
                _machineOptions[i] = new GUIContent(_machines[i].DisplayName);

            if (_selectedMachineIndex >= _machines.Count)
                _selectedMachineIndex = 0;

            Repaint();
        }

        private void OnGUI()
        {
            _toolbarButtonStyle ??= "toolbarbutton";

            using (new GUILayout.HorizontalScope("Toolbar"))
            {
                if (_machineOptions.Length > 0)
                    _selectedMachineIndex = EditorGUILayout.Popup(_selectedMachineIndex, _machineOptions, GUILayout.Width(220));
                else
                    GUILayout.Label("(no state machines found)", _toolbarButtonStyle, GUILayout.Width(220));

                _showSettings = GUILayout.Toggle(_showSettings, "Settings", _toolbarButtonStyle);

                EditorGUI.BeginChangeCheck();
                _lockSelection = GUILayout.Toggle(_lockSelection, "Lock", _toolbarButtonStyle);
                if (EditorGUI.EndChangeCheck())
                    RefreshSelection();

                GUILayout.FlexibleSpace();
            }

            if (_showSettings)
            {
                using (new GUILayout.VerticalScope(GUI.skin.box))
                {
                    _activeColor = EditorGUILayout.ColorField("Active color", _activeColor);
                    _inactiveColor = EditorGUILayout.ColorField("Inactive color", _inactiveColor);
                }
            }

            if (_selectedMachineIndex < 0 || _selectedMachineIndex >= _machines.Count)
                return;

            var entry = _machines[_selectedMachineIndex];
            var machine = entry.GetMachine();
            if (machine == null)
            {
                EditorGUILayout.HelpBox("Field/property returned null.", MessageType.Info);
                return;
            }

            if (!machine.IsInitialized)
            {
                EditorGUILayout.HelpBox("StateMachine is not initialized.", MessageType.Info);
                return;
            }

            _renderer ??= new NestedBoxRenderer();
            _renderer.ActiveColor = _activeColor;
            _renderer.InactiveColor = _inactiveColor;
            machine.Visit(_renderer);
            Repaint();
        }
    }
}
