using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ImageTargetAnchorPlacer : MonoBehaviour
{
    [Header("AR References")]
    [SerializeField] private ARTrackedImageManager trackedImageManager;

    [Header("Placement")]
    [SerializeField] private GameObject contentPrefab;
    [SerializeField] private string targetImageName;
    [SerializeField] private Vector3 positionOffset;
    [SerializeField] private Vector3 rotationOffset;
    [SerializeField] private Vector3 contentScale = Vector3.one;

    [Header("Optional Drift Correction")]
    [SerializeField] private bool correctWhenImageVisible;
    [SerializeField] private float correctionPositionThreshold = 0.03f;
    [SerializeField] private float correctionRotationThreshold = 3f;
    [SerializeField] private float correctionSpeed = 2f;

    [Header("Status Events")]
    public UnityEvent<string> statusChanged;

    private ARAnchor anchor;
    private Transform contentRoot;
    private GameObject contentInstance;
    private bool isPlaced;
    private string currentStatus;

    public bool IsPlaced => isPlaced;
    public string CurrentStatus => currentStatus;
    public GameObject ContentInstance => contentInstance;

    private void Awake()
    {
        if (trackedImageManager == null)
            trackedImageManager = FindObjectOfType<ARTrackedImageManager>();

        SetStatus("Searching for image...");
    }

    private void OnValidate()
    {
        correctionPositionThreshold = Mathf.Max(0f, correctionPositionThreshold);
        correctionRotationThreshold = Mathf.Max(0f, correctionRotationThreshold);
        correctionSpeed = Mathf.Max(0f, correctionSpeed);
    }

    private void OnEnable()
    {
        if (trackedImageManager != null)
            trackedImageManager.trackedImagesChanged += OnTrackedImagesChanged;
    }

    private void OnDisable()
    {
        if (trackedImageManager != null)
            trackedImageManager.trackedImagesChanged -= OnTrackedImagesChanged;
    }

    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        foreach (ARTrackedImage trackedImage in eventArgs.added)
            TryPlaceOrCorrect(trackedImage);

        foreach (ARTrackedImage trackedImage in eventArgs.updated)
            TryPlaceOrCorrect(trackedImage);
    }

    private void TryPlaceOrCorrect(ARTrackedImage trackedImage)
    {
        if (!IsUsableImage(trackedImage))
            return;

        Pose targetPose = GetContentPose(trackedImage.transform);

        if (!isPlaced)
        {
            PlaceContent(targetPose);
            return;
        }

        if (correctWhenImageVisible)
            CorrectContentToward(targetPose);
    }

    private bool IsUsableImage(ARTrackedImage trackedImage)
    {
        if (trackedImage == null)
            return false;

        if (trackedImage.trackingState != TrackingState.Tracking)
        {
            if (!isPlaced)
                SetStatus("Calibrating...");

            return false;
        }

        if (string.IsNullOrWhiteSpace(targetImageName))
            return true;

        return string.Equals(
            trackedImage.referenceImage.name,
            targetImageName,
            StringComparison.OrdinalIgnoreCase);
    }

    private Pose GetContentPose(Transform imageTransform)
    {
        Quaternion rotation = imageTransform.rotation * Quaternion.Euler(rotationOffset);
        Vector3 position = imageTransform.TransformPoint(positionOffset);
        return new Pose(position, rotation);
    }

    private void PlaceContent(Pose targetPose)
    {
        if (contentPrefab == null)
        {
            SetStatus("Missing content prefab.");
            Debug.LogWarning($"{nameof(ImageTargetAnchorPlacer)} needs a content prefab.", this);
            return;
        }

        GameObject anchorObject = new GameObject("Image Target Anchor");
        anchorObject.transform.SetPositionAndRotation(targetPose.position, targetPose.rotation);
        anchor = anchorObject.AddComponent<ARAnchor>();

        GameObject rootObject = new GameObject("Anchored Content Root");
        contentRoot = rootObject.transform;
        contentRoot.SetParent(anchorObject.transform, false);
        contentRoot.localPosition = Vector3.zero;
        contentRoot.localRotation = Quaternion.identity;
        contentRoot.localScale = Vector3.one;

        contentInstance = Instantiate(contentPrefab, contentRoot);
        contentInstance.transform.localPosition = Vector3.zero;
        contentInstance.transform.localRotation = Quaternion.identity;
        contentInstance.transform.localScale = contentScale;
        contentInstance.SetActive(true);

        isPlaced = true;
        SetStatus("Placed.");
    }

    private void CorrectContentToward(Pose targetPose)
    {
        if (contentRoot == null)
            return;

        float positionDelta = Vector3.Distance(contentRoot.position, targetPose.position);
        float rotationDelta = Quaternion.Angle(contentRoot.rotation, targetPose.rotation);

        if (positionDelta < correctionPositionThreshold && rotationDelta < correctionRotationThreshold)
            return;

        float t = 1f - Mathf.Exp(-correctionSpeed * Time.deltaTime);
        contentRoot.SetPositionAndRotation(
            Vector3.Lerp(contentRoot.position, targetPose.position, t),
            Quaternion.Slerp(contentRoot.rotation, targetPose.rotation, t));

        SetStatus("Correcting placement...");
    }

    private void SetStatus(string status)
    {
        if (currentStatus == status)
            return;

        currentStatus = status;
        statusChanged?.Invoke(currentStatus);
    }

    public void ResetPlacement()
    {
        if (contentInstance != null)
            Destroy(contentInstance);

        if (contentRoot != null)
            Destroy(contentRoot.gameObject);

        if (anchor != null)
            Destroy(anchor.gameObject);

        contentInstance = null;
        contentRoot = null;
        anchor = null;
        isPlaced = false;
        SetStatus("Searching for image...");
    }
}
