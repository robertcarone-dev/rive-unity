using NUnit.Framework;
using Rive.Components;
using Rive.EditorTools;
using UnityEditor;
using UnityEngine;

namespace Rive.Tests.EditorTests
{
    public class RiveWidgetEditorPreviewTests
    {
        [Test]
        public void SetEditorPreviewDirty_NotifiesEditorPreview()
        {
            GameObject gameObject = new GameObject("Rive Widget", typeof(RectTransform), typeof(RiveWidget));

            try
            {
                RiveWidget widget = gameObject.GetComponent<RiveWidget>();
                bool wasNotified = false;
                widget.OnEditorPreviewDirty += () => wasNotified = true;

                widget.SetEditorPreviewDirty();

                Assert.IsTrue(wasNotified);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void EditorPreviewData_IsUnavailableWithoutActivePreview()
        {
            GameObject gameObject = new GameObject("Rive Widget", typeof(RectTransform), typeof(RiveWidget));

            try
            {
                RiveWidget widget = gameObject.GetComponent<RiveWidget>();

                bool found = widget.TryGetEditorPreviewData(out File file, out StateMachine stateMachine);

                Assert.IsFalse(found);
                Assert.IsNull(file);
                Assert.IsNull(stateMachine);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void SetEditorPreviewDebugStateDirty_DoesNotReapplyComponentPreviewBindings()
        {
            GameObject gameObject = new GameObject("Rive Widget", typeof(RectTransform), typeof(RiveWidget));

            try
            {
                RiveWidget widget = gameObject.GetComponent<RiveWidget>();
                bool componentPreviewWasInvalidated = false;
                widget.OnEditorPreviewDirty += () => componentPreviewWasInvalidated = true;

                widget.SetEditorPreviewDebugStateDirty();

                Assert.IsFalse(componentPreviewWasInvalidated);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void RequestEditorPreviewReload_ReturnsFalseWithoutActivePreview()
        {
            GameObject gameObject = new GameObject("Rive Widget", typeof(RectTransform), typeof(RiveWidget));

            try
            {
                RiveWidget widget = gameObject.GetComponent<RiveWidget>();

                bool requested = widget.RequestEditorPreviewReload();

                Assert.IsFalse(requested);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DataBindingPlayground_TargetsSelectedWidgetInEditMode()
        {
            GameObject gameObject = new GameObject("Rive Widget", typeof(RectTransform), typeof(RiveWidget));

            try
            {
                RiveWidget widget = gameObject.GetComponent<RiveWidget>();
                Selection.activeGameObject = gameObject;

                RiveWidget selectedWidget = DataBindingPlaygroundWindow.GetSelectedWidget();

                Assert.AreSame(widget, selectedWidget);
            }
            finally
            {
                Selection.activeObject = null;
                Object.DestroyImmediate(gameObject);
            }
        }

        [Test]
        public void DataBindingPlayground_RestoresComponentPreviewValuesWhenTargetIsReleased()
        {
            GameObject gameObject = new GameObject("Rive Widget", typeof(RectTransform), typeof(RiveWidget));

            try
            {
                RiveWidget widget = gameObject.GetComponent<RiveWidget>();
                bool componentPreviewWasInvalidated = false;
                widget.OnEditorPreviewDirty += () => componentPreviewWasInvalidated = true;

                DataBindingPlaygroundWindow.RestoreEditorPreviewValues(widget);

                Assert.IsTrue(componentPreviewWasInvalidated);
            }
            finally
            {
                Object.DestroyImmediate(gameObject);
            }
        }
    }
}
