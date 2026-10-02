using UnityEngine;

namespace LDraw
{
    [DisallowMultipleComponent]
    public sealed class LDrawStudDistanceLod : MonoBehaviour
    {
        [SerializeField] Renderer nearRenderer;
        [SerializeField] Renderer farRenderer;
        [SerializeField] float switchDistance = 80f;
        [SerializeField] float hysteresis = 2f;

        bool usingFar;

        public void Configure(Renderer near, Renderer far, float distance, float hysteresisDistance)
        {
            nearRenderer = near;
            farRenderer = far;
            switchDistance = Mathf.Max(0.01f, distance);
            hysteresis = Mathf.Max(0f, hysteresisDistance);
            ApplyImmediate();
        }

        void OnEnable()
        {
            ApplyImmediate();
        }

        void LateUpdate()
        {
            if (nearRenderer == null || farRenderer == null)
                return;

            var cam = ResolveCamera();
            if (cam == null)
                return;

            float dist = Vector3.Distance(cam.transform.position, transform.position);
            float enterFar = switchDistance + hysteresis;
            float exitFar = switchDistance - hysteresis;

            if (usingFar)
            {
                if (dist < exitFar)
                    usingFar = false;
            }
            else
            {
                if (dist > enterFar)
                    usingFar = true;
            }

            nearRenderer.enabled = !usingFar;
            farRenderer.enabled = usingFar;
        }

        void ApplyImmediate()
        {
            if (nearRenderer == null || farRenderer == null)
                return;

            nearRenderer.enabled = true;
            farRenderer.enabled = false;
            usingFar = false;
        }

        static Camera ResolveCamera()
        {
            if (Application.isPlaying)
            {
                if (Camera.main != null)
                    return Camera.main;

                var cams = Camera.allCameras;
                return cams != null && cams.Length > 0 ? cams[0] : null;
            }

            #if UNITY_EDITOR
            var sv = UnityEditor.SceneView.lastActiveSceneView;
            if (sv != null && sv.camera != null)
                return sv.camera;
            #endif

            return Camera.main;
        }
    }
}

