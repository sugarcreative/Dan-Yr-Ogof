using System;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

public class ImageTargetAnchorPlacer : MonoBehaviour
{
    [Serializable]
    private class ImagePlacement
    {
        public string imageName;
        public GameObject contentPrefab;
        public Vector3 contentScale = Vector3.one;
    }

    private enum PlacementState
    {
        Searching,
        Calibrating,
        Anchored,
        WaitingForRecalibrationImage
    }

    private const string SearchingStatus = "Searching for image...";
    private const string FindImageStatus = "Find the image trigger.";
    private const string CalibratingStatus = "Calibrating, please remain focused on the image trigger.";
    private const string PlacedStatus = "Placed.";
    private const string MissingPrefabStatus = "Missing content prefab.";

    [Header("AR References")]
    [SerializeField] private ARTrackedImageManager trackedImageManager;

    [Header("Image Placements")]
    [SerializeField] private ImagePlacement[] imagePlacements;

    [Header("Calibration")]
    [SerializeField] private float calibrationDuration = 3f;
    [SerializeField] private float calibrationCorrectionSpeed = 6f;
    [SerializeField] private bool resetCalibrationTimerWhenImageLost;

    [Header("Status Events")]
    public UnityEvent<string> statusChanged;

    private ARAnchor anchor;
    private Transform contentRoot;
    private GameObject contentInstance;
    private ARTrackedImage activeTrackedImage;
    private ImagePlacement activePlacement;
    private PlacementState state = PlacementState.Searching;
    private bool isPlaced;
    private float calibrationTimer;
    private string currentStatus;

    public bool IsPlaced => isPlaced;
    public string CurrentStatus => currentStatus;
    public GameObject ContentInstance => contentInstance;
    public float CalibrationProgress => Mathf.Clamp01(calibrationTimer / Mathf.Max(0.0001f, calibrationDuration));

    private void Awake()
    {
        if (trackedImageManager == null)
            trackedImageManager = FindObjectOfType<ARTrackedImageManager>();

        SetStatus(SearchingStatus);
    }

    private void OnValidate()
    {
        calibrationDuration = Mathf.Max(0f, calibrationDuration);
        calibrationCorrectionSpeed = Mathf.Max(0f, calibrationCorrectionSpeed);

        if (imagePlacements == null)
            return;

        foreach (ImagePlacement placement in imagePlacements)
        {
            if (placement == null)
                continue;

            if (placement.contentScale == Vector3.zero)
                placement.contentScale = Vector3.one;
        }
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

    private void Update()
    {
        if (state != PlacementState.Calibrating)
            return;

        if (!IsTrackedActiveImage())
        {
            if (resetCalibrationTimerWhenImageLost)
                calibrationTimer = 0f;

            SetStatus(FindImageStatus);
            return;
        }

        Pose targetPose = GetImagePose(activeTrackedImage.transform);
        CorrectContentToward(targetPose);

        calibrationTimer += Time.deltaTime;
        SetStatus(CalibratingStatus);

        if (calibrationTimer >= calibrationDuration)
            CompleteCalibration();
    }

    private void OnTrackedImagesChanged(ARTrackedImagesChangedEventArgs eventArgs)
    {
        foreach (ARTrackedImage trackedImage in eventArgs.added)
            TryUseTrackedImage(trackedImage);

        foreach (ARTrackedImage trackedImage in eventArgs.updated)
            TryUseTrackedImage(trackedImage);
    }

    private void TryUseTrackedImage(ARTrackedImage trackedImage)
    {
        ImagePlacement placement = GetPlacementFor(trackedImage);
        if (placement == null)
            return;

        if (state == PlacementState.Searching)
        {
            activePlacement = placement;
            activeTrackedImage = trackedImage;

            Pose targetPose = GetImagePose(trackedImage.transform);
            if (PlaceContentForCalibration(targetPose, activePlacement))
                BeginCalibration();

            return;
        }

        if (placement != activePlacement)
        {
            if (SwapContent(placement))
            {
                activePlacement = placement;
                activeTrackedImage = trackedImage;
            }
        }

        if (state == PlacementState.WaitingForRecalibrationImage)
        {
            activeTrackedImage = trackedImage;
            BeginCalibration();
        }
    }

    private ImagePlacement GetPlacementFor(ARTrackedImage trackedImage)
    {
        if (trackedImage == null)
            return null;

        if (trackedImage.trackingState != TrackingState.Tracking)
            return null;

        if (imagePlacements == null)
            return null;

        string imageName = trackedImage.referenceImage.name;

        foreach (ImagePlacement placement in imagePlacements)
        {
            if (placement == null || string.IsNullOrWhiteSpace(placement.imageName))
                continue;

            if (string.Equals(placement.imageName, imageName, StringComparison.OrdinalIgnoreCase))
                return placement;
        }

        return null;
    }

    private bool IsTrackedActiveImage()
    {
        return activeTrackedImage != null
            && activeTrackedImage.trackingState == TrackingState.Tracking
            && activePlacement != null
            && string.Equals(
                activePlacement.imageName,
                activeTrackedImage.referenceImage.name,
                StringComparison.OrdinalIgnoreCase);
    }

    private Pose GetImagePose(Transform imageTransform)
    {
        return new Pose(imageTransform.position, imageTransform.rotation);
    }

    private bool PlaceContentForCalibration(Pose targetPose, ImagePlacement placement)
    {
        GameObject rootObject = new GameObject("AR Content Root");
        contentRoot = rootObject.transform;
        contentRoot.SetPositionAndRotation(targetPose.position, targetPose.rotation);
        contentRoot.localScale = Vector3.one;

        if (!SpawnContent(placement))
        {
            Destroy(rootObject);
            contentRoot = null;
            return false;
        }

        isPlaced = true;
        return true;
    }

    private bool SwapContent(ImagePlacement placement)
    {
        return contentRoot != null && SpawnContent(placement);
    }

    private bool SpawnContent(ImagePlacement placement)
    {
        if (placement == null)
            return false;

        if (placement.contentPrefab == null)
        {
            SetStatus(MissingPrefabStatus);
            Debug.LogWarning(
                $"{nameof(ImageTargetAnchorPlacer)} is missing a content prefab for '{placement.imageName}'.",
                this);
            return false;
        }

        if (contentInstance != null)
            Destroy(contentInstance);

        contentInstance = Instantiate(placement.contentPrefab, contentRoot);
        contentInstance.transform.localPosition = Vector3.zero;
        contentInstance.transform.localRotation = Quaternion.identity;
        contentInstance.transform.localScale = placement.contentScale;
        contentInstance.SetActive(true);

        return true;
    }

    private void CorrectContentToward(Pose targetPose)
    {
        if (contentRoot == null)
            return;

        float t = calibrationCorrectionSpeed <= 0f
            ? 1f
            : 1f - Mathf.Exp(-calibrationCorrectionSpeed * Time.deltaTime);

        contentRoot.SetPositionAndRotation(
            Vector3.Lerp(contentRoot.position, targetPose.position, t),
            Quaternion.Slerp(contentRoot.rotation, targetPose.rotation, t));
    }

    private void BeginCalibration()
    {
        RemoveCurrentAnchorKeepContentInPlace();
        calibrationTimer = 0f;
        state = PlacementState.Calibrating;
        SetStatus(CalibratingStatus);
    }

    private void CompleteCalibration()
    {
        if (contentRoot != null)
            CreateAnchorAtContentRoot();

        state = PlacementState.Anchored;
        calibrationTimer = 0f;
        SetStatus(PlacedStatus);
    }

    private void CreateAnchorAtContentRoot()
    {
        RemoveCurrentAnchorKeepContentInPlace();

        GameObject anchorObject = new GameObject("Image Target Anchor");
        anchorObject.transform.SetPositionAndRotation(contentRoot.position, contentRoot.rotation);
        anchor = anchorObject.AddComponent<ARAnchor>();

        contentRoot.SetParent(anchorObject.transform, true);
        contentRoot.localPosition = Vector3.zero;
        contentRoot.localRotation = Quaternion.identity;
        contentRoot.localScale = Vector3.one;
    }

    private void RemoveCurrentAnchorKeepContentInPlace()
    {
        if (anchor == null)
            return;

        Transform oldAnchorTransform = anchor.transform;

        if (contentRoot != null)
            contentRoot.SetParent(null, true);

        Destroy(oldAnchorTransform.gameObject);
        anchor = null;
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
        activeTrackedImage = null;
        activePlacement = null;
        isPlaced = false;
        calibrationTimer = 0f;
        state = PlacementState.Searching;
        SetStatus(SearchingStatus);
    }

    public void Recalibrate()
    {
        if (!isPlaced)
        {
            ResetPlacement();
            return;
        }

        state = PlacementState.WaitingForRecalibrationImage;
        calibrationTimer = 0f;
        SetStatus(FindImageStatus);
    }
}
