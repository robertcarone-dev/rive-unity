using UnityEngine;
using Rive.Components.Utilities;
using Rive.Utils;

#if UNITY_EDITOR
using System.Collections.Generic;
using System.Linq;
using UnityEditor;
#endif

namespace Rive.Components
{
    /// <summary>
    /// Base class for preview rendering of RivePanel components in the editor.
    /// </summary>
    internal abstract class PanelPreview
    {
#if UNITY_EDITOR
        private class WidgetState
        {
            private Asset m_lastAsset;
            private File m_lastFile;
            private Fit m_lastFit;
            private Alignment m_lastAlignment;
            private string m_lastArtboardName;
            private string m_lastStateMachineName;
            private RiveWidget.DataBindingMode m_lastDataBindingMode;
            private string m_lastViewModelInstanceName;
            private Vector2 m_lastDimensions;

            private LayoutScalingMode m_lastScalingMode;
            private float m_lastScaleFactor;

            private float m_lastReferenceDPI;

            private float m_lastFallbackDPI;

            private Vector2 m_lastScreenSize;

            private RiveWidget m_riveWidget;

            private ArtboardLoadHelper m_riveViewController;

            private RectTransform m_rectTransform;

            private RivePanel m_rivePanel;
            private bool m_needsReload = true;

            private bool m_gameObjectActive;
            private int m_lastEditorPreviewComponentsRevision;
            private bool m_editorPreviewDirty = true;
            private bool m_preparedEditorPreviewChanged;
            private bool m_debugStateDirty;

            private readonly List<MonoBehaviour> m_widgetComponents = new List<MonoBehaviour>();
            private readonly System.Action m_requestEditorPreviewUpdate;


            public WidgetState(RiveWidget riveWidget, ArtboardLoadHelper riveViewController, RivePanel rivePanel, System.Action requestEditorPreviewUpdate)
            {
                m_riveWidget = riveWidget;
                m_requestEditorPreviewUpdate = requestEditorPreviewUpdate;
                m_riveWidget.OnEditorPreviewDirty += SetEditorPreviewDirty;

                // Get the RectTransform of the widget because we don't know that it is populated yet on the RiveWidget
                m_rectTransform = riveWidget.GetComponent<RectTransform>();
                m_rivePanel = rivePanel;

                m_lastAsset = riveWidget.Asset;
                m_lastFit = riveWidget.Fit;
                m_lastAlignment = riveWidget.Alignment;
                m_lastArtboardName = riveWidget.ArtboardName;
                m_lastStateMachineName = riveWidget.StateMachineName;
                m_lastDataBindingMode = riveWidget.BindingMode;
                m_lastViewModelInstanceName = riveWidget.ViewModelInstanceName;
                m_lastDimensions = m_rectTransform.rect.size;
                m_gameObjectActive = riveWidget.gameObject.activeInHierarchy;
                m_lastEditorPreviewComponentsRevision = GetEditorPreviewComponentsRevision();

                m_lastScalingMode = riveWidget.ScalingMode;
                m_lastScaleFactor = riveWidget.ScaleFactor;
                m_lastFallbackDPI = riveWidget.FallbackDPI;
                m_lastReferenceDPI = riveWidget.ReferenceDPI;

                m_riveViewController = riveViewController;

                // We track the screen size to detect changes when the user changes the resolution. This is important for layout scaling.
                m_lastScreenSize = new Vector2(Screen.width, Screen.height);

                LoadIfNeeded();

            }

            public IRenderObject ToRenderObject(RectTransform clonedRectTransform, RectTransform clonedPanelRectTransform)
            {
                if (m_riveViewController == null || m_riveWidget == null || m_riveWidget.Asset == null || !m_riveWidget.gameObject.activeInHierarchy || !m_riveWidget.enabled || m_riveWidget.RectTransform == null)
                {
                    return null;
                }
                LoadIfNeeded();
                IRenderObject renderObject = m_riveViewController.RenderObject;
                if (renderObject == null)
                {
                    return null;
                }

                bool editorPreviewChanged = m_preparedEditorPreviewChanged || UpdateEditorPreviewIfNeeded();
                m_preparedEditorPreviewChanged = false;
                bool layoutChanged = ResizeArtboardForLayoutIfNeeded();
                bool debugStateChanged = m_debugStateDirty;
                m_debugStateDirty = false;
                if ((editorPreviewChanged || layoutChanged || debugStateChanged) && m_riveViewController.StateMachine != null)
                {
                    m_riveViewController.StateMachine.Advance(0f);
                }

                if (clonedRectTransform != null && clonedPanelRectTransform != null)
                {
                    renderObject.RenderTransform = RenderTransform.FromRectTransform(clonedRectTransform, clonedPanelRectTransform);
                }

                return renderObject;
            }

            public void PrepareEditorPreview()
            {
                bool neededReload = m_needsReload;
                LoadIfNeeded();
                m_preparedEditorPreviewChanged = m_preparedEditorPreviewChanged || neededReload || UpdateEditorPreviewIfNeeded();
            }

            public void AdvanceAfterExternalImageUpdate()
            {
                m_preparedEditorPreviewChanged = true;
            }

            public File File => m_lastFile;

            public StateMachine StateMachine => m_riveViewController?.StateMachine;

            public void SetDebugStateDirty()
            {
                m_debugStateDirty = true;
                m_requestEditorPreviewUpdate?.Invoke();
            }

            public void RequestReload()
            {
                m_needsReload = true;
                m_editorPreviewDirty = true;
                m_debugStateDirty = false;
                m_requestEditorPreviewUpdate?.Invoke();
            }

            private string GetValidArtboardName(RiveWidget widget)
            {
                if (widget.Asset == null) return null;

                var metadata = widget.Asset.EditorOnlyMetadata;
                var artboardNames = metadata.GetArtboardNames();

                // If no artboard name is specified or the specified one isn't valid, use the first available
                if (string.IsNullOrEmpty(widget.ArtboardName) || !artboardNames.Contains(widget.ArtboardName))
                {
                    return artboardNames.Length > 0 ? artboardNames[0] : null;
                }

                return widget.ArtboardName;
            }

            private string GetValidStateMachineName(RiveWidget widget, string artboardName)
            {
                if (widget.Asset == null || string.IsNullOrEmpty(artboardName)) return null;

                var metadata = widget.Asset.EditorOnlyMetadata;
                var stateMachineNames = metadata.GetStateMachineNames(artboardName);

                // If no state machine is specified or the specified one isn't valid, use the first available
                if (string.IsNullOrEmpty(widget.StateMachineName) || !stateMachineNames.Contains(widget.StateMachineName))
                {
                    return stateMachineNames.Length > 0 ? stateMachineNames[0] : null;
                }

                return widget.StateMachineName;
            }



            private void LoadIfNeeded()
            {

                if (m_needsReload && m_riveWidget.Asset != null && m_riveViewController != null)
                {
                    if (m_lastFile != null)
                    {
                        // The artboard and state machine retain native objects owned by the file,
                        // so release them before disposing the file they came from.
                        m_riveViewController.Dispose();
                        m_lastFile.Dispose();
                    }

                    m_lastFile = File.Load(m_riveWidget.Asset);
                    if (m_lastFile == null)
                    {
                        m_needsReload = false;
                        return;
                    }

                    string validArtboardName = GetValidArtboardName(m_riveWidget);
                    string validStateMachineName = GetValidStateMachineName(m_riveWidget, validArtboardName);

                    if (validArtboardName != null && validStateMachineName != null)
                    {
                        m_riveViewController.Load(
                            m_lastFile,
                            m_riveWidget.Fit,
                            m_riveWidget.Alignment,
                            validArtboardName,
                            validStateMachineName,
                            m_riveWidget.ScaleFactor,
                            new ArtboardLoadHelper.DataBindingLoadInfo(m_riveWidget.BindingMode, m_riveWidget.ViewModelInstanceName)
                        );

                        UpdateEditorPreviewIfNeeded();

                        if (ResizeArtboardForLayoutIfNeeded() && m_riveViewController.StateMachine != null)
                        {
                            // Layout requires an extra advance before the normal post-load advance below to settle correctly on its first frame.
                            m_riveViewController.StateMachine.Advance(0f);
                        }
                        if (m_riveViewController.StateMachine != null)
                        {
                            m_riveViewController.StateMachine.Advance(0f);

                        }
                    }
                    m_needsReload = false;
                }
            }

            private bool ResizeArtboardForLayoutIfNeeded()
            {
                if (m_riveWidget == null || m_rectTransform == null || m_riveWidget.Fit != Fit.Layout || m_riveViewController?.Artboard == null || m_riveViewController.RenderObject == null)
                {
                    return false;
                }

                Vector2 originalArtboardSize = new Vector2(m_riveViewController.OriginalArtboardWidth, m_riveViewController.OriginalArtboardHeight);
                float effectiveScaleFactor = ArtboardLoadHelper.CalculateEffectiveScaleFactor(m_riveWidget.ScalingMode, m_riveWidget.ScaleFactor, originalArtboardSize, m_rectTransform.rect, m_riveWidget.ReferenceDPI, m_riveWidget.FallbackDPI);

                if (!ArtboardLoadHelper.CalculateArtboardDimensionsForLayout(m_rectTransform.rect, effectiveScaleFactor, out float width, out float height))
                {
                    return false;
                }

                bool changed = !Mathf.Approximately(m_riveViewController.RenderObject.EffectiveLayoutScaleFactor, effectiveScaleFactor) || !Mathf.Approximately(m_riveViewController.Artboard.Width, width) || !Mathf.Approximately(m_riveViewController.Artboard.Height, height);

                if (!changed)
                {
                    return false;
                }

                m_riveViewController.RenderObject.EffectiveLayoutScaleFactor = effectiveScaleFactor;
                m_riveViewController.Artboard.Width = width;
                m_riveViewController.Artboard.Height = height;
                return true;
            }

            private int GetEditorPreviewComponentsRevision()
            {
                m_widgetComponents.Clear();
                m_riveWidget.GetComponents(m_widgetComponents);

                unchecked
                {
                    int componentsRevision = 17;

                    foreach (var component in m_widgetComponents)
                    {
                        if (component is IRiveWidgetEditorPreview && component.isActiveAndEnabled)
                        {
                            componentsRevision = componentsRevision * 31 + component.GetInstanceID();
                        }
                    }

                    return componentsRevision;
                }
            }

            private void SetEditorPreviewDirty()
            {
                m_editorPreviewDirty = true;
                m_requestEditorPreviewUpdate?.Invoke();
            }

            private bool UpdateEditorPreviewIfNeeded()
            {
                if (!m_editorPreviewDirty)
                {
                    return false;
                }

                m_editorPreviewDirty = false;

                ViewModelInstance viewModelInstance = m_riveViewController?.StateMachine?.ViewModelInstance;
                if (viewModelInstance == null)
                {
                    return false;
                }

                bool previewApplied = false;
                m_widgetComponents.Clear();
                m_riveWidget.GetComponents(m_widgetComponents);
                foreach (var component in m_widgetComponents)
                {
                    if (component is IRiveWidgetEditorPreview editorPreview && component.isActiveAndEnabled)
                    {
                        try
                        {
                            editorPreview.ApplyEditorPreview(viewModelInstance);
                            previewApplied = true;
                        }
                        catch (System.Exception exception)
                        {
                            Debug.LogException(exception, component);
                        }
                    }
                }

                return previewApplied;
            }



            public bool HasChanged()
            {
                if (m_riveWidget == null) return false;

                bool transformChanged = HasTransformChanged();

                int editorPreviewComponentsRevision = GetEditorPreviewComponentsRevision();
                bool editorPreviewComponentsChanged = m_lastEditorPreviewComponentsRevision != editorPreviewComponentsRevision;

                // These settings require a reload of the file
                m_needsReload = m_needsReload ||
                               m_lastAsset != m_riveWidget.Asset ||
                               m_lastFit != m_riveWidget.Fit ||
                               m_lastAlignment != m_riveWidget.Alignment ||
                               m_lastArtboardName != m_riveWidget.ArtboardName ||
                               m_lastStateMachineName != m_riveWidget.StateMachineName ||
                               m_lastDataBindingMode != m_riveWidget.BindingMode ||
                               m_lastViewModelInstanceName != m_riveWidget.ViewModelInstanceName ||
                               editorPreviewComponentsChanged;

                bool layoutSettingsChanged = m_riveWidget.Fit == Fit.Layout && (m_lastScalingMode != m_riveWidget.ScalingMode || m_lastScaleFactor != m_riveWidget.ScaleFactor || m_lastReferenceDPI != m_riveWidget.ReferenceDPI || m_lastFallbackDPI != m_riveWidget.FallbackDPI || m_lastScreenSize != new Vector2(Screen.width, Screen.height));

                if (m_needsReload)
                {
                    m_editorPreviewDirty = true;
                }

                bool changed = m_needsReload || layoutSettingsChanged || m_editorPreviewDirty || transformChanged || m_gameObjectActive != m_riveWidget.gameObject.activeInHierarchy;




                if (changed)
                {


                    m_lastAsset = m_riveWidget.Asset;
                    m_lastFit = m_riveWidget.Fit;
                    m_lastAlignment = m_riveWidget.Alignment;
                    m_lastArtboardName = m_riveWidget.ArtboardName;
                    m_lastStateMachineName = m_riveWidget.StateMachineName;
                    m_lastDataBindingMode = m_riveWidget.BindingMode;
                    m_lastViewModelInstanceName = m_riveWidget.ViewModelInstanceName;
                    m_gameObjectActive = m_riveWidget.gameObject.activeInHierarchy;
                    m_lastEditorPreviewComponentsRevision = editorPreviewComponentsRevision;

                    m_lastScalingMode = m_riveWidget.ScalingMode;
                    m_lastScaleFactor = m_riveWidget.ScaleFactor;
                    m_lastFallbackDPI = m_riveWidget.FallbackDPI;
                    m_lastReferenceDPI = m_riveWidget.ReferenceDPI;

                    m_lastScreenSize = new Vector2(Screen.width, Screen.height);
                }

                if (m_rectTransform.hasChanged)
                {
                    m_rectTransform.hasChanged = false;
                }

                return changed;
            }

            public bool HasTransformChanged()
            {
                if (m_rectTransform == null)
                {
                    return false;
                }

                var dimensions = m_rectTransform.rect.size;
                bool dimensionsChanged = m_lastDimensions != dimensions;
                if (dimensionsChanged)
                {
                    m_lastDimensions = dimensions;
                    m_editorPreviewDirty = true;
                }

                return dimensionsChanged || m_rectTransform.hasChanged;
            }

            public void Cleanup()
            {
                if (m_riveWidget != null)
                {
                    m_riveWidget.OnEditorPreviewDirty -= SetEditorPreviewDirty;
                }

                m_riveViewController?.Dispose();
                m_lastFile?.Dispose();
                m_lastFile = null;
            }

        }

        protected readonly RivePanel m_rivePanel;
        protected static Texture2D s_defaultTexture;
        private RenderTexture m_previewRenderTexture;
        private static readonly HashSet<PanelPreview> s_activePreviews = new HashSet<PanelPreview>();
        private int m_lastPanelChildCount;
        private Dictionary<RiveWidget, WidgetState> m_widgetStates = new Dictionary<RiveWidget, WidgetState>();
        private List<RiveWidget> m_disabledWidgets = new List<RiveWidget>();
        private int m_lastScreenWidth;
        private int m_lastScreenHeight;

        private bool m_editorPreviewUpdateRequested;
        private bool m_initialized;


        private Renderer m_renderer;


        public RenderTexture PreviewRenderTexture
        {
            get => m_previewRenderTexture;
            protected set => m_previewRenderTexture = value;
        }
        public RivePanel RivePanel => m_rivePanel;

        protected PanelPreview(RivePanel panel)
        {
            m_rivePanel = panel;
            Initialize();
        }

        protected virtual void Initialize()
        {
            if (m_rivePanel == null || m_initialized) return;

            m_initialized = true;
            m_wasEditorPreviewDisabled = m_rivePanel.DisableEditorPreview;
            EditorApplication.update += OnEditorUpdate;
            EditorApplication.playModeStateChanged += OnPlayModeStateChanged;
            s_activePreviews.Add(this);
            if (s_activePreviews.Count == 1)
            {
                EditorApplication.hierarchyChanged += OnHierarchyChanged;
                ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
            }

            if (!m_wasEditorPreviewDisabled)
            {
                SetDirty();
            }
        }

        public virtual void Dispose()
        {
            if (!m_initialized)
            {
                return;
            }

            m_initialized = false;
            EditorApplication.update -= OnEditorUpdate;
            EditorApplication.playModeStateChanged -= OnPlayModeStateChanged;
            s_activePreviews.Remove(this);
            if (s_activePreviews.Count == 0)
            {
                EditorApplication.hierarchyChanged -= OnHierarchyChanged;
                ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
            }

            CleanupResources();
        }

        private void OnPlayModeStateChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                CleanupResources();
            }
            else if (state == PlayModeStateChange.EnteredEditMode && m_rivePanel != null && !m_rivePanel.DisableEditorPreview)
            {
                RestoreResources();
                SetDirty();
            }
        }

        private static void OnHierarchyChanged()
        {
            foreach (PanelPreview preview in s_activePreviews)
            {
                preview.SetDirty();
            }
        }

        private static void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (int i = 0; i < stream.length; i++)
            {
                if (stream.GetEventType(i) != ObjectChangeKind.ChangeGameObjectOrComponentProperties)
                {
                    continue;
                }

                stream.GetChangeGameObjectOrComponentPropertiesEvent(i, out ChangeGameObjectOrComponentPropertiesEventArgs data);
                Object changedObject = EditorUtility.InstanceIDToObject(data.instanceId);
                Transform changedTransform = changedObject is GameObject gameObject
                    ? gameObject.transform
                    : (changedObject as UnityEngine.Component)?.transform;
                if (changedTransform == null)
                {
                    continue;
                }

                foreach (PanelPreview preview in s_activePreviews)
                {
                    if (preview.ObjectBelongsToPanel(changedTransform))
                    {
                        preview.SetDirty();
                    }
                }
            }
        }

        private bool ObjectBelongsToPanel(Transform changedTransform)
        {
            return m_rivePanel != null &&
                   (changedTransform == m_rivePanel.transform || changedTransform.IsChildOf(m_rivePanel.transform));
        }

        protected virtual void CleanupResources()
        {
            CleanupPreviewRenderTexture();

            foreach (var widgetState in m_widgetStates.Values)
            {
                widgetState?.Cleanup();
            }
            m_widgetStates.Clear();
            m_disabledWidgets.Clear();

            if (m_renderer != null)
            {
                m_renderer.RenderQueue.Dispose();
                m_renderer = null;
            }
        }

        protected virtual void RestoreResources()
        {
        }

        private bool m_wasEditorPreviewDisabled;

        internal void SetDirty()
        {
            m_editorPreviewUpdateRequested = true;
        }

        internal static bool TryGetWidgetEditorPreviewData(RiveWidget widget, out File file, out StateMachine stateMachine)
        {
            foreach (PanelPreview preview in s_activePreviews)
            {
                if (preview.m_widgetStates.TryGetValue(widget, out WidgetState widgetState))
                {
                    file = widgetState.File;
                    stateMachine = widgetState.StateMachine;
                    return file != null && stateMachine != null;
                }
            }

            file = null;
            stateMachine = null;
            return false;
        }

        internal static void SetWidgetEditorPreviewDebugStateDirty(RiveWidget widget)
        {
            foreach (PanelPreview preview in s_activePreviews)
            {
                if (preview.m_widgetStates.TryGetValue(widget, out WidgetState widgetState))
                {
                    widgetState.SetDebugStateDirty();
                    return;
                }
            }
        }

        internal static bool RequestWidgetEditorPreviewReload(RiveWidget widget)
        {
            foreach (PanelPreview preview in s_activePreviews)
            {
                if (preview.m_widgetStates.TryGetValue(widget, out WidgetState widgetState))
                {
                    widgetState.RequestReload();
                    return true;
                }
            }

            return false;
        }

        private static void SetExternalImageUpdateDirty()
        {
            foreach (PanelPreview preview in s_activePreviews)
            {
                foreach (WidgetState widgetState in preview.m_widgetStates.Values)
                {
                    widgetState.AdvanceAfterExternalImageUpdate();
                }
                preview.SetDirty();
            }
        }

        protected bool TryConsumeEditorPreviewUpdateRequest()
        {
            if (!m_editorPreviewUpdateRequested)
            {
                return false;
            }

            m_editorPreviewUpdateRequested = false;
            return true;
        }

        /// <summary>
        /// Canvas previews consume refresh requests during <see cref="Canvas.willRenderCanvases"/> so they observe the final layout state for the current Canvas update.
        /// </summary>
        protected virtual bool DeferEditorPreviewUpdateUntilRender => false;

        protected virtual void OnEditorUpdate()
        {
            if (m_rivePanel == null) return;

            if (m_rivePanel.DisableEditorPreview)
            {
                if (!m_wasEditorPreviewDisabled)
                {
                    CleanupResources();
                    m_wasEditorPreviewDisabled = true;
                }
                return;
            }

            if (m_wasEditorPreviewDisabled)
            {
                m_wasEditorPreviewDisabled = false;
                RestoreResources();
                SetDirty();
            }

            if (!Application.isPlaying)
            {
#if RIVE_USING_EXPERIMENTAL
                if (RenderTextureImageManager.HasPerFrameBindings)
                {
                    SetDirty();
                }
#endif
                if (m_lastScreenWidth != Screen.width || m_lastScreenHeight != Screen.height)
                {
                    SetDirty();
                }

                if (!m_editorPreviewUpdateRequested)
                {
                    return;
                }

                if (!DeferEditorPreviewUpdateUntilRender)
                {
                    HasChanged();
                    if (TryConsumeEditorPreviewUpdateRequest())
                    {
                        UpdateEditorPreview();
                    }
                }

                // We use this to force the editor to update the preview when any of the widget settings change.
                // If we don't, the update might be delayed until the user interacts with the scene view or somewhere else in the editor. This might give the impression that the settings are not working.
                EditorApplication.QueuePlayerLoopUpdate();

            }
        }


        protected virtual bool HasChanged()
        {
            if (m_rivePanel == null) return false;

            bool changed = m_lastScreenWidth != Screen.width || m_lastScreenHeight != Screen.height;
            if (changed)
            {
                m_lastScreenWidth = Screen.width;
                m_lastScreenHeight = Screen.height;
            }

            foreach (var widget in m_widgetStates)
            {
                changed |= widget.Value.HasChanged();
            }

            bool childCountChanged = m_lastPanelChildCount != m_rivePanel.transform.childCount;
            if (childCountChanged)
            {
                m_lastPanelChildCount = m_rivePanel.transform.childCount;
                changed = true;
            }

            int disabledWidgetEnabledCount = 0;
            if (m_disabledWidgets.Count > 0)
            {
                for (int i = m_disabledWidgets.Count - 1; i >= 0; i--)
                {
                    if (m_disabledWidgets[i].gameObject.activeInHierarchy)
                    {
                        m_disabledWidgets.RemoveAt(i);
                        disabledWidgetEnabledCount++;
                    }
                }
            }

            return changed || disabledWidgetEnabledCount > 0;
        }

        protected bool HaveWidgetTransformsChanged()
        {
            bool changed = false;
            foreach (var widget in m_widgetStates)
            {
                changed |= widget.Value.HasTransformChanged();
            }
            return changed;
        }

        protected abstract void UpdateEditorPreview();

        private bool WidgetIsChildOfPanel(RiveWidget widget)
        {
            if (widget == null)
            {
                return false;
            }



            return widget.transform.IsChildOf(m_rivePanel.transform);
        }

        protected virtual Vector2Int GetPreviewPixelSize()
        {
            return new Vector2Int((int)m_rivePanel.WidgetContainer.rect.width, (int)m_rivePanel.WidgetContainer.rect.height);
        }

        protected virtual Vector2 GetPreviewDrawScale()
        {
            return Vector2.one;
        }

        protected RenderTexture RenderPreview()
        {
            if (!NativeUsageGuard.IsNativeAvailable)
            {
                return null;
            }

            if (m_rivePanel == null || !m_rivePanel.enabled)
            {
                return null;
            }


            Vector2Int previewPixelSize = GetPreviewPixelSize();
            int width = previewPixelSize.x;
            int height = previewPixelSize.y;

            if (width < 1 || height < 1)
            {
                return null;
            }

            bool dimensionsChanged = m_previewRenderTexture == null || m_previewRenderTexture.width != width || m_previewRenderTexture.height != height;

            if (dimensionsChanged)
            {
                if (m_previewRenderTexture == null)
                {
                    m_previewRenderTexture = CreateRenderTexture(width, height);
                }
                else
                {
                    m_previewRenderTexture.Release();
                    m_previewRenderTexture.width = width;
                    m_previewRenderTexture.height = height;
                    m_previewRenderTexture.Create();
                }
            }

            List<RiveWidget> currentWidgets = new List<RiveWidget>();
            m_rivePanel.GetComponentsInChildren(currentWidgets);

            // Filter out widgets with invalid configurations
            currentWidgets = currentWidgets.Where(widget =>
                widget != null &&
                widget.gameObject.activeInHierarchy &&
                widget.Asset != null
            ).ToList();

            if (currentWidgets.Count == 0)
            {
                return null;
            }


            // Remove widget states for widgets that no longer exist
            m_widgetStates.Keys.Where(widget => !currentWidgets.Contains(widget)).ToList().ForEach(widget =>
            {
                m_widgetStates[widget].Cleanup();
                // If the widget is disabled and still a child of the panel, we need to keep it around for the next frame to check if it is enabled again
                if (widget != null && !widget.gameObject.activeInHierarchy && WidgetIsChildOfPanel(widget) && !m_disabledWidgets.Contains(widget))
                {
                    m_disabledWidgets.Add(widget);
                }
                m_widgetStates.Remove(widget);
            });

            if (currentWidgets.All(widget => widget.gameObject.activeInHierarchy == false) || currentWidgets.All(widget => widget.Asset == null))
            {
                return null;
            }



            RenderTexture rt = m_previewRenderTexture;
            RenderTexture previousActive = RenderTexture.active;
            RenderTexture.active = rt;


            if (m_renderer != null)
            {
                // When using OpenGL in the Unity Editor, we get this error if we try to use the same renderer each time: OPENGL NATIVE PLUG-IN ERROR: GL_INVALID_OPERTATION: Operation Invalid in current state.
                // Current workaround is to dispose the renderer and create a new one when needed.
                if (TextureHelper.IsOpenGLPlatform())
                {
                    m_renderer.RenderQueue.Dispose();
                    m_renderer = null;
                }
                else
                {
                    // For other platforms, we clear the existing renderer to avoid rendering leftover data from the previous visual.
                    m_renderer.Clear();

                }
            }

            //var rq = new RenderQueue(SystemInfo.graphicsDeviceType == GraphicsDeviceType.Metal ? null : rt); <-- Doing this causes the Unity Editor to hang when the RivePanel game object is duplicated.
            if (m_renderer == null)
            {
                var rq = new RenderQueue(rt);

                m_renderer = rq.Renderer();
                if (m_renderer == null)
                {
                    rq.Dispose();
                    RenderTexture.active = previousActive;
                    return null;
                }
            }

            if (!ReferenceEquals(m_renderer.RenderQueue.Texture, rt))
            {
                m_renderer.RenderQueue.UpdateTexture(rt);
            }

            Vector2 drawScale = GetPreviewDrawScale();
            if (Mathf.Abs(drawScale.x - 1f) > 0.001f || Mathf.Abs(drawScale.y - 1f) > 0.001f)
            {
                m_renderer.Transform(System.Numerics.Matrix3x2.CreateScale(drawScale.x, drawScale.y));
            }




            for (int i = 0; i < currentWidgets.Count; i++)
            {
                var widget = currentWidgets[i];

                if (widget == null)
                {
                    continue;
                }



                if (!m_widgetStates.TryGetValue(widget, out WidgetState widgetState))
                {
                    ArtboardLoadHelper riveViewController = new ArtboardLoadHelper();
                    widgetState = new WidgetState(widget, riveViewController, m_rivePanel, SetDirty);
                    m_widgetStates.Add(widget, widgetState);
                }
            }

#if RIVE_USING_EXPERIMENTAL
            for (int i = 0; i < currentWidgets.Count; i++)
            {
                if (m_widgetStates.TryGetValue(currentWidgets[i], out WidgetState widgetState))
                {
                    widgetState.PrepareEditorPreview();
                }
            }

            if (RenderTextureImageManager.HasAnyBindings && RenderTextureImageManager.Instance.Tick())
            {
                SetExternalImageUpdateDirty();
            }
#endif

            for (int i = 0; i < currentWidgets.Count; i++)
            {
                var widget = currentWidgets[i];

                if (widget == null || !m_widgetStates.TryGetValue(widget, out WidgetState widgetState))
                {
                    continue;
                }

                IRenderObject renderObject = widgetState.ToRenderObject(widget.RectTransform, m_rivePanel.WidgetContainer);

                if (renderObject == null)
                {
                    continue;
                }
                RenderContext renderContext = new RenderContext(RenderContext.ClippingModeSetting.CheckClipping);
                RenderTargetStrategy.DrawRenderObject(m_renderer, renderObject, m_rivePanel, renderContext);

            }

            var cmb = m_renderer.ToCommandBuffer();

            cmb.SetRenderTarget(rt);
            m_renderer.AddToCommandBuffer(cmb);


            Graphics.ExecuteCommandBuffer(cmb);

            GL.InvalidateState();

            cmb.Clear();


            RenderTexture.active = previousActive;

            return rt;
        }

        protected void CleanupPreviewRenderTexture()
        {
            if (m_previewRenderTexture != null)
            {
                RenderTexture activeRT = RenderTexture.active;
                if (activeRT == m_previewRenderTexture)
                {
                    RenderTexture.active = null;
                }
                ReleaseRenderTexture(m_previewRenderTexture);
                m_previewRenderTexture = null;
            }
        }

        protected RenderTexture CreateRenderTexture(int width, int height)
        {
            var descriptor = TextureHelper.Descriptor(width, height);
            RenderTexture rt = new RenderTexture(descriptor);

            rt.Create();

            return rt;
        }

        protected void ReleaseRenderTexture(RenderTexture rt)
        {

            if (rt != null)
            {
                rt.Release();
            }

        }

        protected Texture2D GetDefaultTexture()
        {
            if (s_defaultTexture == null)
            {
                string iconPath = "Packages/app.rive.rive-unity/Editor/Images/rive-preview-image.png";
                s_defaultTexture = AssetDatabase.LoadAssetAtPath<Texture2D>(iconPath);

                if (s_defaultTexture == null)
                {
                    DebugLogger.Instance.LogWarning($"Failed to load default texture from {iconPath}. Creating a plain colored texture instead.");
                    s_defaultTexture = new Texture2D(1600, 900, TextureFormat.RGBA32, false);
                    UnityEngine.Color darkGrey = new UnityEngine.Color(0.2f, 0.2f, 0.2f, 1f);
                    UnityEngine.Color[] colors = new UnityEngine.Color[1600 * 900];
                    for (int i = 0; i < colors.Length; i++)
                    {
                        colors[i] = darkGrey;
                    }
                    s_defaultTexture.SetPixels(colors);
                    s_defaultTexture.Apply();
                }
                else
                {
                    s_defaultTexture.wrapMode = TextureWrapMode.Clamp;
                    s_defaultTexture.filterMode = FilterMode.Bilinear;
                }
            }

            return s_defaultTexture;
        }
#endif
    }
}
