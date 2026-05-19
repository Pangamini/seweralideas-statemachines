using SeweralIdeas.StateMachines;
using UnityEngine;

/// <summary>
/// Reusable IMGUI overlay that draws a <see cref="StateMachine"/> as nested vertical boxes
/// with horizontal child layout — recreating the package's original in-game debug visual.
/// Built on top of the public <see cref="IStateVisitor"/> protocol, so it has no editor
/// dependencies and works at runtime in standalone builds.
///
/// Usage from a MonoBehaviour:
///
/// <code>
/// private readonly InGameStateMachineOverlay _overlay = new();
///
/// private void OnGUI()
/// {
///     var rect = new Rect(32, Screen.height * 0.5f, Screen.width - 64, Screen.height * 0.5f - 32);
///     GUILayout.BeginArea(rect);
///     _overlay.Draw(_stateMachine);
///     GUILayout.EndArea();
/// }
/// </code>
/// </summary>
public sealed class InGameStateMachineOverlay : IStateVisitor
{
    private static readonly GUILayoutOption[] s_expandHeight = { GUILayout.ExpandHeight(true) };

    public Color activeColor = Color.yellow;
    public Color inactiveColor = new(0.6f, 0.6f, 0.6f, 1f);

    public void Draw(StateMachine machine)
    {
        if (machine == null)
        {
            GUILayout.Label("(no state machine)");
            return;
        }
        if (!machine.IsInitialized)
        {
            GUILayout.Label($"{machine.Name}: not initialized");
            return;
        }
        machine.Visit(this);
    }

    void IStateVisitor.BeginState(State state, int depth, bool isActive, bool hasChildren)
    {
        var prev = GUI.color;
        GUI.color = isActive ? activeColor : inactiveColor;
        GUILayout.BeginVertical(state.Name, GUI.skin.window, s_expandHeight);
        GUI.color = prev;

        if (hasChildren)
            GUILayout.BeginHorizontal();
    }

    void IStateVisitor.EndState(State state, int depth, bool isActive, bool hasChildren)
    {
        if (hasChildren)
            GUILayout.EndHorizontal();
        GUILayout.EndVertical();
    }
}
