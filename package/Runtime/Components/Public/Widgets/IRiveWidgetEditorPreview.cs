#if UNITY_EDITOR
namespace Rive.Components
{
    /// <summary>
    /// Implemented by components that configure a RiveWidget's data-bound values in its editor preview.
    /// Call <see cref="RiveWidget.SetEditorPreviewDirty"/> after the component's preview values change.
    /// </summary>
    public interface IRiveWidgetEditorPreview
    {
        /// <summary>
        /// Applies component-owned values to the editor preview's view model instance.
        /// The preview owns this instance and may replace it when the widget reloads.
        /// Implementations that cache property handles must rebuild them when the instance changes.
        /// </summary>
        /// <param name="viewModelInstance">The view model instance bound to the editor preview.</param>
        void ApplyEditorPreview(ViewModelInstance viewModelInstance);
    }
}
#endif
