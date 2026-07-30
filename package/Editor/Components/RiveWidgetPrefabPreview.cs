using Rive.Components;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Rive.EditorTools
{
    /// <summary>
    /// Supplies an editor-only panel environment for standalone RiveWidget prefabs.
    /// The environment sits outside prefabContentsRoot and is marked DontSave, following
    /// the same pattern Unity uses for its own prefab-stage environment objects.
    /// </summary>
    [InitializeOnLoad]
    internal static class RiveWidgetPrefabPreview
    {
        private const string PreviewHostName = "Rive Panel (Environment)";
        private static bool s_isEnsureScheduled;

        static RiveWidgetPrefabPreview()
        {
            PrefabStage.prefabStageOpened -= OnPrefabStageOpened;
            PrefabStage.prefabStageOpened += OnPrefabStageOpened;
            PrefabStage.prefabStageClosing -= OnPrefabStageClosing;
            PrefabStage.prefabStageClosing += OnPrefabStageClosing;
            ObjectChangeEvents.changesPublished -= OnObjectChangesPublished;
            ObjectChangeEvents.changesPublished += OnObjectChangesPublished;
            ScheduleEnsureCurrentPrefabStagePreview();
        }

        private static void OnPrefabStageOpened(PrefabStage prefabStage)
        {
            ScheduleEnsureCurrentPrefabStagePreview();
        }

        private static void OnPrefabStageClosing(PrefabStage prefabStage)
        {
            RemovePreviewHost(prefabStage.prefabContentsRoot);
        }

        private static void OnObjectChangesPublished(ref ObjectChangeEventStream stream)
        {
            for (var index = 0; index < stream.length; index++)
            {
                switch (stream.GetEventType(index))
                {
                    case ObjectChangeKind.ChangeGameObjectParent:
                    case ObjectChangeKind.ChangeGameObjectStructure:
                    case ObjectChangeKind.ChangeGameObjectStructureHierarchy:
                    case ObjectChangeKind.CreateGameObjectHierarchy:
                    case ObjectChangeKind.DestroyGameObjectHierarchy:
                        ScheduleEnsureCurrentPrefabStagePreview();
                        return;
                }
            }
        }

        private static void ScheduleEnsureCurrentPrefabStagePreview()
        {
            if (s_isEnsureScheduled)
            {
                return;
            }

            s_isEnsureScheduled = true;
            EditorApplication.delayCall += EnsureCurrentPrefabStagePreview;
        }

        private static void EnsureCurrentPrefabStagePreview()
        {
            s_isEnsureScheduled = false;
            EnsurePrefabStagePreview(PrefabStageUtility.GetCurrentPrefabStage());
        }

        private static void EnsurePrefabStagePreview(PrefabStage prefabStage)
        {
            if (Application.isPlaying || prefabStage == null || prefabStage != PrefabStageUtility.GetCurrentPrefabStage() || prefabStage.mode != PrefabStage.Mode.InIsolation)
            {
                return;
            }

            var prefabRoot = prefabStage.prefabContentsRoot;
            if (prefabRoot == null)
            {
                return;
            }

            var needsPreviewHost = prefabRoot.GetComponentInChildren<RiveWidget>(true) != null && prefabRoot.GetComponentInChildren<RivePanel>(true) == null;
            var hasPreviewHost = TryGetPreviewHost(prefabRoot, out var existingHost);
            if (!needsPreviewHost)
            {
                if (hasPreviewHost)
                {
                    RemovePreviewHost(prefabRoot);
                }

                return;
            }

            if (hasPreviewHost)
            {
                ConfigureHostRect((RectTransform)existingHost.transform);
                EnsureHostComponents(existingHost);
                EnsureWidgetPanelAssociation(existingHost, prefabRoot);
                RefreshWidgets(prefabRoot);
                return;
            }

            CreatePreviewHost(prefabRoot);
        }

        private static bool TryGetPreviewHost(GameObject prefabRoot, out GameObject previewHost)
        {
            var parent = prefabRoot.transform.parent;
            if (parent != null && parent.name == PreviewHostName && (parent.gameObject.hideFlags & HideFlags.DontSave) != 0)
            {
                previewHost = parent.gameObject;
                return true;
            }

            previewHost = null;
            return false;
        }

        private static void CreatePreviewHost(GameObject prefabRoot)
        {
            var prefabTransform = prefabRoot.transform;
            var originalParent = prefabTransform.parent;
            var originalSiblingIndex = prefabTransform.GetSiblingIndex();

            var host = EditorUtility.CreateGameObjectWithHideFlags(PreviewHostName, HideFlags.DontSave, typeof(RectTransform));
            SceneManager.MoveGameObjectToScene(host, prefabRoot.scene);

            // Rive's editor preview manager initializes from OnEnable. Keep the host
            // inactive until its Canvas, panel, renderer, and widget hierarchy all exist.
            host.SetActive(false);

            var hostRectTransform = (RectTransform)host.transform;
            hostRectTransform.SetParent(originalParent, false);
            hostRectTransform.SetSiblingIndex(originalSiblingIndex);
            ConfigureHostRect(hostRectTransform);

            if (host.GetComponentInParent<Canvas>() == null)
            {
                var canvas = host.AddComponent<Canvas>();
                canvas.renderMode = RenderMode.WorldSpace;
            }

            // The panel must exist before the prefab is parented. RiveWidget discovers
            // and registers with its panel from OnTransformParentChanged.
            EnsureHostComponents(host);
            prefabTransform.SetParent(hostRectTransform, false);
            SchedulePreviewInitialization(host, prefabRoot);
        }

        private static void RemovePreviewHost(GameObject prefabRoot)
        {
            if (prefabRoot == null || !TryGetPreviewHost(prefabRoot, out var host))
            {
                return;
            }

            var hostTransform = host.transform;
            var prefabTransform = prefabRoot.transform;
            var parent = hostTransform.parent;
            var siblingIndex = hostTransform.GetSiblingIndex();
            prefabTransform.SetParent(parent, false);
            prefabTransform.SetSiblingIndex(siblingIndex);
            Object.DestroyImmediate(host);
        }

        private static void SchedulePreviewInitialization(GameObject host, GameObject prefabRoot)
        {
            EditorApplication.delayCall += () => InitializePreview(host, prefabRoot);
        }

        private static void InitializePreview(GameObject host, GameObject prefabRoot)
        {
            if (host == null || prefabRoot == null || prefabRoot.transform.parent != host.transform || PrefabStageUtility.GetCurrentPrefabStage()?.prefabContentsRoot != prefabRoot)
            {
                return;
            }

            // Rive's own panel creation command restarts the panel after constructing
            // its widget hierarchy. This is necessary because the required, hidden
            // PanelContextPreviewManager initializes synchronously from OnEnable.
            host.SetActive(false);
            EnsureHostComponents(host);
            host.SetActive(true);

            Canvas.ForceUpdateCanvases();
            EnsureWidgetPanelAssociation(host, prefabRoot);

            EditorApplication.QueuePlayerLoopUpdate();
            EditorApplication.delayCall += () => RefreshWidgets(prefabRoot);
        }

        private static void EnsureHostComponents(GameObject host)
        {
            if (host.GetComponent<RivePanel>() == null)
            {
                host.AddComponent<RivePanel>();
            }

            var canvasRenderer = host.GetComponent<RiveCanvasRenderer>();
            if (canvasRenderer == null)
            {
                canvasRenderer = host.AddComponent<RiveCanvasRenderer>();
            }

            canvasRenderer.MatchCanvasResolution = true;
        }

        private static void EnsureWidgetPanelAssociation(GameObject host, GameObject prefabRoot)
        {
            var panel = host.GetComponent<RivePanel>();
            var widgets = prefabRoot.GetComponentsInChildren<RiveWidget>(true);
            var needsAssociationRefresh = false;
            foreach (var widget in widgets)
            {
                if (!ReferenceEquals(widget.RivePanel, panel))
                {
                    needsAssociationRefresh = true;
                    break;
                }
            }

            if (!needsAssociationRefresh)
            {
                return;
            }

            // Re-run WidgetBehaviour's normal parent-change lifecycle without changing
            // any serialized local RectTransform values.
            var prefabTransform = prefabRoot.transform;
            var hostTransform = host.transform;
            prefabTransform.SetParent(hostTransform.parent, false);
            prefabTransform.SetParent(hostTransform, false);
        }

        private static void RefreshWidgets(GameObject prefabRoot)
        {
            if (prefabRoot == null || PrefabStageUtility.GetCurrentPrefabStage()?.prefabContentsRoot != prefabRoot)
            {
                return;
            }

            var widgets = prefabRoot.GetComponentsInChildren<RiveWidget>(true);
            foreach (var widget in widgets)
            {
                widget.SetEditorPreviewDirty();
            }

            EditorApplication.QueuePlayerLoopUpdate();
            SceneView.RepaintAll();
        }

        private static void ConfigureHostRect(RectTransform hostRectTransform)
        {
            hostRectTransform.localPosition = Vector3.zero;
            hostRectTransform.localRotation = Quaternion.identity;
            hostRectTransform.localScale = Vector3.one;

            RectTransform parentRectTransform = hostRectTransform.parent as RectTransform;
            if (parentRectTransform != null && parentRectTransform.rect.width > 1f && parentRectTransform.rect.height > 1f)
            {
                hostRectTransform.anchorMin = Vector2.zero;
                hostRectTransform.anchorMax = Vector2.one;
                hostRectTransform.offsetMin = Vector2.zero;
                hostRectTransform.offsetMax = Vector2.zero;
                hostRectTransform.pivot = new Vector2(0.5f, 0.5f);
                return;
            }

            var previewSize = Handles.GetMainGameViewSize();
            if (previewSize.x <= 1f || previewSize.y <= 1f)
            {
                previewSize = new Vector2(1920f, 1080f);
            }

            hostRectTransform.anchorMin = new Vector2(0.5f, 0.5f);
            hostRectTransform.anchorMax = new Vector2(0.5f, 0.5f);
            hostRectTransform.pivot = new Vector2(0.5f, 0.5f);
            hostRectTransform.anchoredPosition = Vector2.zero;
            hostRectTransform.sizeDelta = previewSize;
        }
    }
}
