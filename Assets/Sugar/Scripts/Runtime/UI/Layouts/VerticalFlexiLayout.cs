using UnityEngine;

namespace Sugar.UI
{
    [ExecuteAlways]
    [DisallowMultipleComponent]
    [RequireComponent(typeof(RectTransform))]
    public sealed class VerticalFlexiLayout : MonoBehaviour
    {
        [SerializeField] private RectTransform topTab;
        [SerializeField] private RectTransform bottomTab;

        private RectTransform contentArea;
        private RectTransform parentRect;

        public void Configure(RectTransform top, RectTransform bottom)
        {
            topTab = top;
            bottomTab = bottom;
            RefreshLayout();
        }

        private void OnEnable()
        {
            CacheTransforms();
            Canvas.willRenderCanvases += RefreshLayout;
            RefreshLayout();
        }

        private void OnDisable()
        {
            Canvas.willRenderCanvases -= RefreshLayout;
        }

        private void OnValidate()
        {
            CacheTransforms();
            RefreshLayout();
        }

        private void CacheTransforms()
        {
            if (contentArea == null)
                contentArea = GetComponent<RectTransform>();

            if (contentArea != null)
                parentRect = contentArea.parent as RectTransform;
        }

        private void RefreshLayout()
        {
            CacheTransforms();

            if (contentArea == null || parentRect == null || topTab == null || bottomTab == null)
                return;

            Bounds topBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                parentRect,
                topTab);
            Bounds bottomBounds = RectTransformUtility.CalculateRelativeRectTransformBounds(
                parentRect,
                bottomTab);

            float bottomEdge = bottomBounds.max.y;
            float topEdge = Mathf.Max(bottomEdge, topBounds.min.y);
            Rect parentBounds = parentRect.rect;

            Vector2 desiredOffsetMin = new Vector2(0f, bottomEdge - parentBounds.yMin);
            Vector2 desiredOffsetMax = new Vector2(0f, topEdge - parentBounds.yMax);

            if (contentArea.anchorMin != Vector2.zero)
                contentArea.anchorMin = Vector2.zero;

            if (contentArea.anchorMax != Vector2.one)
                contentArea.anchorMax = Vector2.one;

            if (contentArea.pivot != Vector2.one * 0.5f)
                contentArea.pivot = Vector2.one * 0.5f;

            if ((contentArea.offsetMin - desiredOffsetMin).sqrMagnitude > 0.0001f)
                contentArea.offsetMin = desiredOffsetMin;

            if ((contentArea.offsetMax - desiredOffsetMax).sqrMagnitude > 0.0001f)
                contentArea.offsetMax = desiredOffsetMax;
        }
    }
}
