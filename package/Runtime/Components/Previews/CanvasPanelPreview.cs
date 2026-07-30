using UnityEngine;
using UnityEngine.UI;

#if UNITY_EDITOR
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.SceneManagement;
#endif

namespace Rive.Components
{
    /// <summary>
    /// Handles the preview rendering for the RiveCanvasPanel component in the editor.
    /// </summary>
    internal class CanvasPanelPreview : PanelPreview
    {
#if UNITY_EDITOR
        private CanvasRendererRawImage m_displayImage;
        private RiveCanvasRenderer m_canvasRenderer;
        private Texture m_lastPreviewTexture;
        private Vector2Int m_lastPreviewPixelSize;
        private Vector2 m_lastPreviewDrawScale;
        private bool m_isUpdating;

        public CanvasPanelPreview(RivePanel panel) : base(panel)
        {
            m_displayImage = panel.GetComponent<CanvasRendererRawImage>();
            m_canvasRenderer = panel.GetComponent<RiveCanvasRenderer>();
        }

        protected override void Initialize()
        {
            // Ensure uGUI's layout callback is registered before this preview callback so the preview observes the final RectTransform state for this Canvas pass.
            _ = CanvasUpdateRegistry.instance;
            Canvas.willRenderCanvases -= OnWillRenderCanvases;
            Canvas.willRenderCanvases += OnWillRenderCanvases;
            EditorSceneManager.sceneLoaded -= OnSceneLoaded;
            EditorSceneManager.sceneLoaded += OnSceneLoaded;
            base.Initialize();
        }

        public override void Dispose()
        {
            Canvas.willRenderCanvases -= OnWillRenderCanvases;
            EditorSceneManager.sceneLoaded -= OnSceneLoaded;
            base.Dispose();
        }

        private void OnSceneLoaded(Scene arg0, LoadSceneMode arg1)
        {
            SetDirty();
        }

        protected override bool DeferEditorPreviewUpdateUntilRender => true;

        protected override Vector2Int GetPreviewPixelSize()
        {
            EnsureCanvasRenderer();
            if (m_canvasRenderer != null && m_canvasRenderer.MatchCanvasResolution)
            {
                return m_canvasRenderer.ComputeCanvasPixelSize(RivePanel);
            }

            return base.GetPreviewPixelSize();
        }

        protected override Vector2 GetPreviewDrawScale()
        {
            EnsureCanvasRenderer();
            if (m_canvasRenderer != null && m_canvasRenderer.MatchCanvasResolution)
            {
                return m_canvasRenderer.ComputeCanvasDrawScale(RivePanel);
            }

            return base.GetPreviewDrawScale();
        }

        private void OnWillRenderCanvases()
        {
            if (Application.isPlaying || RivePanel == null || RivePanel.DisableEditorPreview || !RivePanel.isActiveAndEnabled)
            {
                return;
            }

            bool widgetTransformsChanged = HaveWidgetTransformsChanged();
            bool renderMetricsChanged = HaveRenderMetricsChanged();
            bool updateRequested = TryConsumeEditorPreviewUpdateRequest();
            if (!widgetTransformsChanged && !renderMetricsChanged && !updateRequested)
            {
                return;
            }

            // Synchronize cached widget and RectTransform state immediately before rendering. This also prevents the fallback detector from requesting the same frame again.
            HasChanged();
            UpdateEditorPreview();
        }

        protected override void CleanupResources()
        {
            base.CleanupResources();
            if (m_displayImage != null)
            {
                m_displayImage.CleanupEditorPreview();
            }
            m_lastPreviewTexture = null;
            m_isUpdating = false;
        }

        protected override void UpdateEditorPreview()
        {
            // We want to prevent multiple calls to UpdateEditorPreview in the same frame
            // as that can cause performance issues and glitches
            if (!m_isUpdating && RivePanel != null && RivePanel.gameObject.activeInHierarchy && RivePanel.enabled)
            {
                DelayedUpdatePreview();
            }

        }

        private void DelayedUpdatePreview()
        {
            m_isUpdating = true;
            try
            {
                // If the scene is not loaded, we don't want to update the preview
                // because it can cause issues when switching scenes.
                if (m_displayImage == null)
                {
                    m_displayImage = RivePanel.GetComponent<CanvasRendererRawImage>();
                }

                if (Application.isPlaying ||
                    !RivePanel.gameObject.scene.isLoaded ||
                    m_displayImage == null)
                {
                    return;
                }

                RenderTexture renderTexture = RenderPreview();
                Texture previewTexture = renderTexture != null ? renderTexture : GetDefaultTexture();

                // Ensure correct color in Linear/Gamma space by using the decode UI material.
                var decodeMat = Rive.TextureHelper.GammaToLinearUIMaterial;
                if (decodeMat != null && m_displayImage.material != decodeMat)
                {
                    m_displayImage.material = decodeMat;
                }

                m_displayImage.UpdateEditorPreview(previewTexture);

                if (previewTexture != m_lastPreviewTexture)
                {
                    m_lastPreviewTexture = previewTexture;
                    PreviewRenderTexture = renderTexture;
                }
            }
            finally
            {
                m_isUpdating = false;
            }
        }

        private void EnsureCanvasRenderer()
        {
            if (m_canvasRenderer == null && RivePanel != null)
            {
                m_canvasRenderer = RivePanel.GetComponent<RiveCanvasRenderer>();
            }
        }

        private bool HaveRenderMetricsChanged()
        {
            Vector2Int previewPixelSize = GetPreviewPixelSize();
            Vector2 previewDrawScale = GetPreviewDrawScale();
            bool changed = previewPixelSize != m_lastPreviewPixelSize || previewDrawScale != m_lastPreviewDrawScale;
            m_lastPreviewPixelSize = previewPixelSize;
            m_lastPreviewDrawScale = previewDrawScale;
            return changed;
        }
#endif
    }
}
