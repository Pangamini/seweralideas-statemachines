#nullable enable

using UnityEditor;
using UnityEngine;

namespace SeweralIdeas.StateMachines.Editor
{
    /// <summary>
    /// Ready-made base editor for a component whose only extra need is the state machine
    /// inspector preview (see <see cref="StateMachinePreviewGUI"/>). Inherit from this when
    /// the component has no other custom editor logic; otherwise add a
    /// <see cref="StateMachinePreviewGUI"/> field directly to your existing editor and
    /// delegate the same overrides. Abstract so it can't accidentally register itself as an
    /// editor for anything — give each concrete subclass its own <c>[CustomEditor(typeof(X))]</c>.
    /// </summary>
    public abstract class StateMachinePreviewEditor : UnityEditor.Editor
    {
        private readonly StateMachinePreviewGUI _preview = new();

        protected virtual void OnEnable() => _preview.Scan((MonoBehaviour)target);

        public override void OnInspectorGUI() => DrawDefaultInspector();

        public override bool HasPreviewGUI() => _preview.HasPreviewGUI;

        public override void OnPreviewSettings() => _preview.OnPreviewSettings();

        public override void OnInteractivePreviewGUI(Rect rect, GUIStyle background) => _preview.OnInteractivePreviewGUI(rect, background);

        public override bool RequiresConstantRepaint() => _preview.RequiresConstantRepaint;
    }
}
