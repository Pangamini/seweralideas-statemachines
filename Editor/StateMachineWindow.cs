#if UNITY_EDITOR
using System.Collections.Generic;
using UnityEngine;
using UnityEditor;
using System.Reflection;

namespace SeweralIdeas.StateMachines.Editor
{
    public class StateMachineWindow : EditorWindow
    {
        [System.Serializable]
        private struct FieldRecord
        {
            [SerializeField] public string fieldName;
            [SerializeField] public object obj;

            public override string ToString()
            {
                return string.Format("{0}.{1}", obj.GetType().Name, fieldName);
            }
        }

        [SerializeField] private bool _lockSelection = false;
        [SerializeField] private GameObject _selectedGameObject;
        [SerializeField] private FieldRecord _selectedField;
        [SerializeField] private StateMachine.GUISettings _guiSettings = new StateMachine.GUISettings();
        
        [System.NonSerialized] private List<(FieldRecord record, MachineGetter getter)> _machines = new List<(FieldRecord record, MachineGetter getter)>();
        [System.NonSerialized] private GUIContent[] _machineOptions = System.Array.Empty<GUIContent>();

        private delegate object MachineGetter(object obj);
        private GameObject _scannedGameObject;
        private StateMachine _selectedMachine;
        private int _selectedMachineIndex;
        private bool _showSettings;

        private GUIStyle _toolbarButtonStyle;

        [MenuItem("Window/Analysis/StateMachine Debugger")]
        static void Init()
        {
            var window = CreateWindow<StateMachineWindow>("StateMachine");
            window._guiSettings = new StateMachine.GUISettings() { stateColor_active = Color.yellow, stateColor_normal = Color.gray };
            window.Show();
        }
        
        private void OnGUI()
        {
            _toolbarButtonStyle ??= "toolbarbutton";
                
            using (new GUILayout.HorizontalScope("Toolbar"))
            {
                _selectedMachineIndex = EditorGUILayout.Popup(_selectedMachineIndex, _machineOptions, GUILayout.Width(200));
                
                _showSettings = GUILayout.Toggle(_showSettings, "Settings", _toolbarButtonStyle);
                EditorGUI.BeginChangeCheck();
                _lockSelection = GUILayout.Toggle(_lockSelection, "Lock", _toolbarButtonStyle);
                if (EditorGUI.EndChangeCheck())
                    RefreshSelectedGameobject();
                GUILayout.FlexibleSpace();
            }

            if (_showSettings)
            {
                GUILayout.BeginVertical("Settings", GUI.skin.box);
                _guiSettings.fieldsMode = (StateMachine.GUISettings.FieldsMode)EditorGUILayout.EnumPopup("show fields", _guiSettings.fieldsMode);
                _guiSettings.stateColor_normal = EditorGUILayout.ColorField("normal color", _guiSettings.stateColor_normal);
                _guiSettings.stateColor_active = EditorGUILayout.ColorField("active color", _guiSettings.stateColor_active);
                GUILayout.EndVertical();
            }

            if (_selectedMachineIndex >= 0 && _selectedMachineIndex < _machines.Count)
            {
                var item = _machines[_selectedMachineIndex];
                _selectedMachine = (StateMachine)item.getter.Invoke(item.record.obj);
                _selectedField = item.record;
            }
            else
            {
                _selectedMachine = null;
                _selectedField = default;
            }

            if (_selectedMachine != null)
            {
                _selectedMachine.OnGUI(_guiSettings);
                Repaint();
            }
        }

        private void PlayStateChanged(PlayModeStateChange change)
        {
            if (change == PlayModeStateChange.EnteredEditMode || change == PlayModeStateChange.EnteredPlayMode)
                RefreshSelectedGameobject();
        }

        private void RefreshSelectedGameobject()
        {
            if (!_lockSelection && Selection.activeGameObject)
                _selectedGameObject = Selection.activeGameObject;

            if (_scannedGameObject == _selectedGameObject) return;

            _machines.Clear();
            _machineOptions = System.Array.Empty<GUIContent>();
            
            if (_selectedGameObject == null)
                return;

            var scripts = _selectedGameObject.GetComponents<MonoBehaviour>();
            foreach (var script in scripts)
            {
                FindStateMachines(script);
            }
            
            Repaint();
        }

        private void FindStateMachines(object obj)
        {
            if (obj == null)
                return; 

            var type = obj.GetType();
            while (type != null && type != typeof(MonoBehaviour))
            {
                var members = type.GetMembers(BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance | BindingFlags.DeclaredOnly);
                foreach (var member in members)
                {
                    MachineGetter machineGetter = null;
                    System.Type memberType = null;

                    if (member is FieldInfo)
                    {
                        var field = (FieldInfo)member;
                        memberType = field.FieldType;
                        machineGetter = field.GetValue;
                    }
                    else if (member is PropertyInfo)
                    {
                        var prop = (PropertyInfo)member;
                        memberType = prop.PropertyType;
                        machineGetter = prop.GetValue;
                    }

                    if (machineGetter != null)
                    {
                        if (typeof(StateMachine).IsAssignableFrom(memberType))
                        {
                            var name = member.Name;
                            var record = new FieldRecord() { obj = obj, fieldName = name };
                            _machines.Add(new System.ValueTuple<FieldRecord, MachineGetter>(record, machineGetter));
                            //_machineOptions.Add(new GUIContent(record.ToString()));
                        }

                        else if (member.GetCustomAttribute<HasStateMachine>() != null)
                        {
                            FindStateMachines(machineGetter(obj));
                        }
                    }

                }
                type = type.BaseType;
            }

            _machineOptions = new GUIContent[_machines.Count];
            for (int i = 0; i < _machineOptions.Length; ++i)
            {
                _machineOptions[i] = new GUIContent(_machines[i].record.ToString());
            }
        }

        protected void OnEnable()
        {
            EditorApplication.playModeStateChanged += PlayStateChanged;
            Selection.selectionChanged += RefreshSelectedGameobject;
            RefreshSelectedGameobject();
        }

        protected void OnDisavle()
        {
            EditorApplication.playModeStateChanged -= PlayStateChanged;
            Selection.selectionChanged -= RefreshSelectedGameobject;
        }
    }
}
#endif