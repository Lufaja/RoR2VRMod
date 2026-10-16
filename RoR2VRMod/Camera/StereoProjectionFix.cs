using BepInEx.Configuration;
using System.Globalization;
using UnityEngine;
using UnityEngine.XR;

namespace VRMod
{
    /// <summary>
    /// Existing reference-camera projection workaround. Its lifecycle is kept reversible;
    /// successful matrix copying is not evidence that a particular headset renders correctly.
    /// </summary>
    internal class StereoProjectionFix : MonoBehaviour
    {
        private static ConfigEntry<bool> workaroundEnabled;
        private static ConfigEntry<bool> diagnosticsEnabled;
        private static int diagnosticSamples;
        private const int MaximumDiagnosticSamples = 120;
        private Camera parentCamera;
        private Camera referenceCamera;
        private GameObject referenceObject;
        private bool projectionOverridden;
        private readonly float[] nextDiagnosticTimes = new float[3];

        internal static void Configure(ConfigFile config)
        {
            workaroundEnabled = config.Bind("Camera", "StereoProjectionWorkaround", true,
                "Use the existing reference-camera stereo projection workaround. Disable for an A/B capture; headset rendering still requires verification.");
            diagnosticsEnabled = config.Bind("Diagnostics", "StereoCamera", false,
                "Log stereo state and projection summaries at most once every 5 seconds per camera/eye plus a lifecycle sample, up to 120 samples per process. No controller pose or personal data is logged.");
        }

        private void Awake()
        {
            parentCamera = GetComponent<Camera>();
        }

        private void Start()
        {
            if (parentCamera && (workaroundEnabled == null || workaroundEnabled.Value))
                CreateReferenceCamera();
        }

        private void CreateReferenceCamera()
        {
            if (referenceObject || !parentCamera)
                return;

            referenceObject = new GameObject("VR Stereo Reference Camera");
            referenceObject.transform.SetParent(transform, false);
            referenceCamera = referenceObject.AddComponent<Camera>();
            referenceCamera.CopyFrom(parentCamera);
            // CopyFrom can copy custom projection state. Let the XR provider supply fresh matrices.
            referenceCamera.ResetProjectionMatrix();
            referenceCamera.ResetStereoProjectionMatrices();
            referenceCamera.cullingMask = 0;
            referenceCamera.clearFlags = CameraClearFlags.Nothing;
            referenceCamera.depth = -100;
            referenceCamera.allowHDR = false;
            referenceCamera.allowMSAA = false;
            referenceCamera.useOcclusionCulling = false;
            referenceCamera.stereoTargetEye = StereoTargetEyeMask.Both;
            referenceCamera.targetTexture = null;
            // The child inherits the parent's tracked pose. A second TrackedPoseDriver would
            // apply that pose again in child-local space. Disable automatic XR pose writes too.
            XRDevice.DisableAutoXRCameraTracking(referenceCamera, true);
            referenceCamera.enabled = parentCamera.isActiveAndEnabled;
        }

        private void OnEnable()
        {
            if (referenceCamera && parentCamera)
                referenceCamera.enabled = parentCamera.isActiveAndEnabled;
        }

        private void OnDisable()
        {
            if (referenceCamera)
                referenceCamera.enabled = false;
            ResetOverrides();
        }

        private void LateUpdate()
        {
            if (!parentCamera)
                return;

            bool useWorkaround = workaroundEnabled == null || workaroundEnabled.Value;
            if (useWorkaround && !referenceCamera)
                CreateReferenceCamera();
            if (referenceCamera)
            {
                referenceCamera.enabled = useWorkaround && parentCamera.isActiveAndEnabled;
                referenceCamera.nearClipPlane = parentCamera.nearClipPlane;
                referenceCamera.farClipPlane = parentCamera.farClipPlane;
            }
            if (!useWorkaround || !parentCamera.isActiveAndEnabled || !XRSettings.enabled || !XRSettings.isDeviceActive)
                ResetOverrides();

            LogDiagnostic("late-update");
        }

        private void OnPreCull() { ApplyStereoProjection(); }
        private void OnPreRender() { ApplyStereoProjection(); }

        private void ApplyStereoProjection()
        {
            if (!parentCamera || !referenceCamera || !referenceCamera.isActiveAndEnabled ||
                !XRSettings.enabled || !XRSettings.isDeviceActive ||
                (workaroundEnabled != null && !workaroundEnabled.Value))
            {
                ResetOverrides();
                return;
            }

            // A mono/spectator pass must not inherit the previous eye's mono override.
            Camera.StereoscopicEye eye;
            switch (parentCamera.stereoActiveEye)
            {
                case Camera.MonoOrStereoscopicEye.Left: eye = Camera.StereoscopicEye.Left; break;
                case Camera.MonoOrStereoscopicEye.Right: eye = Camera.StereoscopicEye.Right; break;
                default: ResetOverrides(); return;
            }

            Matrix4x4 left = referenceCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left);
            Matrix4x4 right = referenceCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right);
            if (!IsUsableProjection(left) || !IsUsableProjection(right))
            {
                ResetOverrides();
                LogDiagnostic("invalid-reference-projection");
                return;
            }

            LogDiagnostic("before-apply");
            parentCamera.projectionMatrix = eye == Camera.StereoscopicEye.Left ? left : right;
            parentCamera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Left, left);
            parentCamera.SetStereoProjectionMatrix(Camera.StereoscopicEye.Right, right);
            projectionOverridden = true;
        }

        private void ResetOverrides()
        {
            if (!projectionOverridden || !parentCamera)
                return;
            parentCamera.ResetProjectionMatrix();
            parentCamera.ResetStereoProjectionMatrices();
            projectionOverridden = false;
        }

        private static bool IsUsableProjection(Matrix4x4 matrix)
        {
            for (int i = 0; i < 16; i++)
                if (float.IsNaN(matrix[i]) || float.IsInfinity(matrix[i]))
                    return false;
            return Mathf.Abs(matrix.m00) > 0.00001f && Mathf.Abs(matrix.m11) > 0.00001f;
        }

        private void LogDiagnostic(string phase)
        {
            if (diagnosticsEnabled == null || !diagnosticsEnabled.Value || !parentCamera ||
                diagnosticSamples >= MaximumDiagnosticSamples)
                return;
            int slot = phase == "late-update" ? 2 : parentCamera.stereoActiveEye == Camera.MonoOrStereoscopicEye.Right ? 1 : 0;
            if (Time.realtimeSinceStartup < nextDiagnosticTimes[slot])
                return;
            nextDiagnosticTimes[slot] = Time.realtimeSinceStartup + 5f;
            diagnosticSamples++;
            string reference = referenceCamera
                ? ProjectionSummary(referenceCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left)) + "/" +
                  ProjectionSummary(referenceCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right))
                : "none";
            VRMod.StaticLogger.LogInfo($"[VR stereo {diagnosticSamples}/{MaximumDiagnosticSamples}] phase={phase}; frame={Time.frameCount}; scene={gameObject.scene.name}; camera={parentCamera.name}; xrEnabled={XRSettings.enabled}; deviceActive={XRSettings.isDeviceActive}; mode={XRSettings.stereoRenderingMode}; stereoEnabled={parentCamera.stereoEnabled}; eye={parentCamera.stereoActiveEye}; target={parentCamera.stereoTargetEye}; texture={parentCamera.targetTexture != null}; workaround={workaroundEnabled == null || workaroundEnabled.Value}; override={projectionOverridden}; near={parentCamera.nearClipPlane}; far={parentCamera.farClipPlane}; parentL/R={ProjectionSummary(parentCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Left))}/{ProjectionSummary(parentCamera.GetStereoProjectionMatrix(Camera.StereoscopicEye.Right))}; referenceActive={referenceCamera && referenceCamera.isActiveAndEnabled}; referenceStereo={referenceCamera && referenceCamera.stereoEnabled}; referenceL/R={reference}");
        }

        private static string ProjectionSummary(Matrix4x4 matrix)
        {
            return string.Format(CultureInfo.InvariantCulture, "[{0:F4},{1:F4},{2:F4},{3:F4};valid={4}]",
                matrix.m00, matrix.m11, matrix.m02, matrix.m12, IsUsableProjection(matrix));
        }

        private void OnDestroy()
        {
            ResetOverrides();
            if (referenceCamera)
                referenceCamera.enabled = false;
            if (referenceObject)
                Destroy(referenceObject);
        }
    }
}
