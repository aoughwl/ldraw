using UnityEngine;

namespace LDraw
{
    public static class LDrawPartLodUtility
    {
        public const string StudlessLodChildName = "LOD_NoStud";
        const float DefaultSwitchDistance = 80f;
        const float DefaultHysteresis = 2f;

        public static void ConfigureStudlessLod(GameObject root, Mesh studlessMesh, Material[] sharedMaterials,
            float switchDistance = DefaultSwitchDistance, float hysteresisDistance = DefaultHysteresis)
        {
            if (root == null)
                return;

            var rootFilter = root.GetComponent<MeshFilter>();
            var rootRenderer = root.GetComponent<MeshRenderer>();
            if (rootFilter == null || rootRenderer == null)
                return;

            if (studlessMesh == null)
            {
                ClearStudlessLod(root);
                return;
            }

            var lodChild = root.transform.Find(StudlessLodChildName);
            GameObject childGo;
            if (lodChild == null)
            {
                childGo = new GameObject(StudlessLodChildName);
                childGo.transform.SetParent(root.transform, false);
            }
            else
            {
                childGo = lodChild.gameObject;
            }

            var childFilter = childGo.GetComponent<MeshFilter>();
            if (childFilter == null)
                childFilter = childGo.AddComponent<MeshFilter>();
            childFilter.sharedMesh = studlessMesh;

            var childRenderer = childGo.GetComponent<MeshRenderer>();
            if (childRenderer == null)
                childRenderer = childGo.AddComponent<MeshRenderer>();
            childRenderer.sharedMaterials = sharedMaterials != null ? sharedMaterials : rootRenderer.sharedMaterials;

            var existingLodGroup = root.GetComponent<LODGroup>();
            if (existingLodGroup != null)
            {
                #if UNITY_EDITOR
                if (!Application.isPlaying)
                    Object.DestroyImmediate(existingLodGroup);
                else
                #endif
                    Object.Destroy(existingLodGroup);
            }

            var distanceLod = root.GetComponent<LDrawStudDistanceLod>();
            if (distanceLod == null)
                distanceLod = root.AddComponent<LDrawStudDistanceLod>();
            distanceLod.Configure(rootRenderer, childRenderer, switchDistance, hysteresisDistance);
        }

        public static void ClearStudlessLod(GameObject root)
        {
            if (root == null)
                return;

            var lodChild = root.transform.Find(StudlessLodChildName);
            if (lodChild != null)
            {
                #if UNITY_EDITOR
                if (!Application.isPlaying)
                    Object.DestroyImmediate(lodChild.gameObject);
                else
                #endif
                    Object.Destroy(lodChild.gameObject);
            }

            var lodGroup = root.GetComponent<LODGroup>();
            if (lodGroup != null)
            {
                #if UNITY_EDITOR
                if (!Application.isPlaying)
                    Object.DestroyImmediate(lodGroup);
                else
                #endif
                    Object.Destroy(lodGroup);
            }

            var distanceLod = root.GetComponent<LDrawStudDistanceLod>();
            if (distanceLod != null)
            {
                #if UNITY_EDITOR
                if (!Application.isPlaying)
                    Object.DestroyImmediate(distanceLod);
                else
                #endif
                    Object.Destroy(distanceLod);
            }
        }
    }
}













