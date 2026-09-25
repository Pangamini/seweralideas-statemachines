#nullable enable

using System.Collections.Generic;
using UnityEngine;

namespace SeweralIdeas.StateMachines.Editor
{
    /// <summary>
    /// Non-<c>GUILayout</c> rendering of a state tree as nested boxes, for use inside
    /// <see cref="UnityEditor.Editor.OnInteractivePreviewGUI"/>. That callback is not run
    /// through the normal Layout+Repaint cycle <c>GUILayout</c>'s auto-sizing depends on, so
    /// content built with <c>GUILayout.BeginVertical</c>/<c>BeginHorizontal</c> silently
    /// renders nothing there. This does its own two-pass layout (measure, then draw at
    /// explicit <see cref="Rect"/>s) instead. <see cref="NestedBoxRenderer"/> remains the
    /// right choice for <see cref="StateMachineWindow"/>, an ordinary <c>EditorWindow</c>
    /// where <c>GUILayout</c> works normally.
    /// </summary>
    internal static class StateMachineTreeLayout
    {
        private const float Spacing = 4f;
        private const float MinLeafWidth = 60f;
        private const float MinLeafHeight = 34f;

        public static Vector2 Measure(StateMachine machine, out Dictionary<State, Vector2> sizes)
        {
            var visitor = new MeasureVisitor();
            machine.Visit(visitor);
            sizes = visitor.Sizes;
            return visitor.RootSize;
        }

        public static void Draw(StateMachine machine, Dictionary<State, Vector2> sizes, Vector2 origin, Color activeColor, Color inactiveColor)
        {
            var draw = new DrawVisitor(sizes, activeColor, inactiveColor, origin);
            machine.Visit(draw);
        }

        private sealed class MeasureVisitor : IStateVisitor
        {
            public readonly Dictionary<State, Vector2> Sizes = new();
            public Vector2 RootSize;

            private readonly Stack<List<Vector2>> _childSizesStack = new();

            public void BeginState(State state, int depth, bool isActive, bool hasChildren)
            {
                _childSizesStack.Push(new List<Vector2>());
            }

            public void EndState(State state, int depth, bool isActive, bool hasChildren)
            {
                var childSizes = _childSizesStack.Pop();
                var style = GUI.skin.window;
                Vector2 titleSize = style.CalcSize(new GUIContent(state.Name));

                float width;
                float height;

                if (hasChildren)
                {
                    float childrenWidth = 0f;
                    float childrenHeight = 0f;
                    foreach (var childSize in childSizes)
                    {
                        childrenWidth += childSize.x;
                        childrenHeight = Mathf.Max(childrenHeight, childSize.y);
                    }
                    childrenWidth += Spacing * (childSizes.Count - 1);

                    width = Mathf.Max(titleSize.x, childrenWidth + style.padding.left + style.padding.right);
                    height = style.padding.top + childrenHeight + style.padding.bottom;
                }
                else
                {
                    width = Mathf.Max(titleSize.x, MinLeafWidth);
                    height = Mathf.Max(titleSize.y, MinLeafHeight);
                }

                var size = new Vector2(width, height);
                Sizes[state] = size;
                RootSize = size;

                if (_childSizesStack.Count > 0)
                    _childSizesStack.Peek().Add(size);
            }
        }

        private sealed class DrawVisitor : IStateVisitor
        {
            private readonly Dictionary<State, Vector2> _sizes;
            private readonly Color _activeColor;
            private readonly Color _inactiveColor;
            private readonly Stack<Vector2> _cursorStack = new();
            private readonly Vector2 _rootOrigin;

            public DrawVisitor(Dictionary<State, Vector2> sizes, Color activeColor, Color inactiveColor, Vector2 rootOrigin)
            {
                _sizes = sizes;
                _activeColor = activeColor;
                _inactiveColor = inactiveColor;
                _rootOrigin = rootOrigin;
            }

            public void BeginState(State state, int depth, bool isActive, bool hasChildren)
            {
                Vector2 origin = _cursorStack.Count > 0 ? _cursorStack.Peek() : _rootOrigin;
                Vector2 size = _sizes[state];
                var boxRect = new Rect(origin, size);

                var style = GUI.skin.window;
                var prevColor = GUI.color;
                GUI.color = isActive ? _activeColor : _inactiveColor;
                GUI.Box(boxRect, state.Name, style);
                GUI.color = prevColor;

                _cursorStack.Push(new Vector2(boxRect.x + style.padding.left, boxRect.y + style.padding.top));
            }

            public void EndState(State state, int depth, bool isActive, bool hasChildren)
            {
                _cursorStack.Pop();

                if (_cursorStack.Count > 0)
                {
                    Vector2 parentCursor = _cursorStack.Pop();
                    parentCursor.x += _sizes[state].x + Spacing;
                    _cursorStack.Push(parentCursor);
                }
            }
        }
    }
}
