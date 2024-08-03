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

        [SerializeField] private bool m_lockSelection = false;
        [SerializeField] private GameObject m_selectedGameObject;
        [SerializeField] private FieldRecord m_selectedField;
        [SerializeField] private StateMachine.GUISettings m_guiSettings = new StateMachine.GUISettings();
        
        [System.NonSerialized] private List<(FieldRecord record, MachineGetter getter)> m_machines = new List<(FieldRecord record, MachineGetter getter)>();
        [System.NonSerialized] private GUIContent[] m_machineOptions = System.Array.Empty<GUIContent>();

        private delegate object MachineGetter(object obj);
        private GameObject m_scannedGameObject;
        private StateMachine m_selectedMachine;
        private int m_selectedMachineIndex;
        private bool m_showSettings;

        private GUIStyle m_toolbarButtonStyle;

        [MenuItem("Window/Analysis/StateMachine Debugger")]
        static void Init()
        {
            var window = CreateWindow<StateMachineWindow>("StateMachine");
            window.m_guiSettings = new StateMachine.GUISettings() { stateColor_active = Color.yellow, stateColor_normal = Color.gray };
            window.Show();
        }
        
        private void OnGUI()
        {
            m_toolbarButtonStyle ??= "toolbarbutton";
                
            using (new GUILayout.HorizontalScope("Toolbar"))
            {
                m_selectedMachineIndex = EditorGUILayout.Popup(m_selectedMachineIndex, m_machineOptions, GUILayout.Width(200));
                
                m_showSettings = GUILayout.Toggle(m_showSettings, "Settings", m_toolbarButtonStyle);
                EditorGUI.BeginChangeCheck();
                m_lockSelection = GUILayout.Toggle(m_lockSelection, "Lock", m_toolbarButtonStyle);
                if (EditorGUI.EndChangeCheck())
                    RefreshSelectedGameobject();
                GUILayout.FlexibleSpace();
            }

            if (m_showSettings)
            {
                GUILayout.BeginVertical("Settings", GUI.skin.box);
                m_guiSettings.fieldsMode = (StateMachine.GUISettings.FieldsMode)EditorGUILayout.EnumPopup("show fields", m_guiSettings.fieldsMode);
                m_guiSettings.stateColor_normal = EditorGUILayout.ColorField("normal color", m_guiSettings.stateColor_normal);
                m_guiSettings.stateColor_active = EditorGUILayout.ColorField("active color", m_guiSettings.stateColor_active);
                GUILayout.EndVertical();
            }

            if (m_selectedMachineIndex >= 0 && m_selectedMachineIndex < m_machines.Count)
            {
                var item = m_machines[m_selectedMachineIndex];
                m_selectedMachine = (StateMachine)item.getter.Invoke(item.record.obj);
                m_selectedField = item.record;
            }
            else
            {
                m_selectedMachine = null;
                m_selectedField = default;
            }

            if (m_selectedMachine != null)
            {
                m_selectedMachine.OnGUI(m_guiSettings);
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
            if (!m_lockSelection && Selection.activeGameObject)
                m_selectedGameObject = Selection.activeGameObject;

            if (m_scannedGameObject == m_selectedGameObject) return;

            m_machines.Clear();
            m_machineOptions = System.Array.Empty<GUIContent>();
            
            if (m_selectedGameObject == null)
                return;

            var scripts = m_selectedGameObject.GetComponents<MonoBehaviour>();
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
                            m_machines.Add(new System.ValueTuple<FieldRecord, MachineGetter>(record, machineGetter));
                            //m_machineOptions.Add(new GUIContent(record.ToString()));
                        }

                        else if (member.GetCustomAttribute<HasStateMachine>() != null)
                        {
                            FindStateMachines(machineGetter(obj));
                        }
                    }

                }
                type = type.BaseType;
            }

            m_machineOptions = new GUIContent[m_machines.Count];
            for (int i = 0; i < m_machineOptions.Length; ++i)
            {
                m_machineOptions[i] = new GUIContent(m_machines[i].record.ToString());
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