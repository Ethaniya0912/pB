// =============================================================================
// ProceduralSplineMesh.cs  |  pB-4 Project — Week 1
// Layer  : L3 Domain (Terrain)
// Owner  : Person B
// Catmull-Rom spline extrusion for HNGS tunnel deformation.
// =============================================================================
using System.Collections.Generic;
using UnityEngine;

namespace TDA.PB4.Terrain
{
    public class ProceduralSplineMesh : MonoBehaviour
    {
        [Header("Spline Settings")]
        [Range(4, 24)] public int crossSectionSegments = 12;
        [Range(1, 10)] public int interpolationSteps = 4;
        public float baseRadius = 3.0f;
        [Range(0.1f, 1.0f)] public float minRadiusMultiplier = 0.4f;

        private Mesh generatedMesh;
        private MeshFilter meshFilter;

        private void Awake()
        {
            meshFilter = GetComponent<MeshFilter>();
            if (meshFilter == null) meshFilter = gameObject.AddComponent<MeshFilter>();
            if (GetComponent<MeshRenderer>() == null)
            {
                var r = gameObject.AddComponent<MeshRenderer>();
                r.material = new Material(Shader.Find("Universal Render Pipeline/Lit"));
            }
        }

        public Mesh GenerateMesh(Vector3[] spinePoints, float intensity)
        {
            if (spinePoints == null || spinePoints.Length < 2) return null;

            float radiusMul = Mathf.Lerp(1.0f, minRadiusMultiplier, intensity);
            float currentRadius = baseRadius * radiusMul;
            List<Vector3> interpolated = InterpolateCatmullRom(spinePoints, interpolationSteps);

            int pathLen = interpolated.Count;
            int seg = crossSectionSegments;
            Vector3[] verts = new Vector3[pathLen * (seg + 1)];
            Vector3[] norms = new Vector3[verts.Length];
            Vector2[] uvs = new Vector2[verts.Length];
            int[] tris = new int[(pathLen - 1) * seg * 6];

            for (int p = 0; p < pathLen; p++)
            {
                Vector3 pos = interpolated[p];
                Vector3 tangent = p < pathLen - 1 ? (interpolated[p+1]-pos).normalized : (pos-interpolated[p-1]).normalized;
                Vector3 normal = Vector3.Cross(tangent, Vector3.up).normalized;
                if (normal.sqrMagnitude < 0.01f) normal = Vector3.Cross(tangent, Vector3.right).normalized;
                Vector3 binormal = Vector3.Cross(tangent, normal).normalized;

                float t = (float)p / (pathLen - 1);
                float r = currentRadius * (1f + 0.1f * Mathf.Sin(t * Mathf.PI));

                for (int s = 0; s <= seg; s++)
                {
                    float a = (2f * Mathf.PI / seg) * s;
                    Vector3 off = (normal * Mathf.Cos(a) + binormal * Mathf.Sin(a)) * r;
                    int vi = p * (seg + 1) + s;
                    verts[vi] = pos + off;
                    norms[vi] = off.normalized;
                    uvs[vi] = new Vector2((float)s / seg, t);
                }
            }

            int ti = 0;
            for (int p = 0; p < pathLen - 1; p++)
                for (int s = 0; s < seg; s++)
                {
                    int c = p * (seg + 1) + s, n = c + seg + 1;
                    tris[ti++]=c; tris[ti++]=n; tris[ti++]=c+1;
                    tris[ti++]=c+1; tris[ti++]=n; tris[ti++]=n+1;
                }

            if (generatedMesh == null) generatedMesh = new Mesh();
            else generatedMesh.Clear();
            generatedMesh.name = "ProceduralTunnel";
            generatedMesh.vertices = verts; generatedMesh.normals = norms;
            generatedMesh.uv = uvs; generatedMesh.triangles = tris;
            generatedMesh.RecalculateBounds();
            if (meshFilter) meshFilter.mesh = generatedMesh;
            return generatedMesh;
        }

        private List<Vector3> InterpolateCatmullRom(Vector3[] pts, int steps)
        {
            var res = new List<Vector3>();
            for (int i = 0; i < pts.Length - 1; i++)
            {
                var p0=pts[Mathf.Max(0,i-1)]; var p1=pts[i];
                var p2=pts[Mathf.Min(pts.Length-1,i+1)]; var p3=pts[Mathf.Min(pts.Length-1,i+2)];
                for (int s = 0; s < steps; s++)
                    res.Add(CatmullRom(p0,p1,p2,p3,(float)s/steps));
            }
            res.Add(pts[pts.Length-1]);
            return res;
        }

        private Vector3 CatmullRom(Vector3 p0,Vector3 p1,Vector3 p2,Vector3 p3,float t)
        {
            float t2=t*t, t3=t2*t;
            return 0.5f*((2f*p1)+(-p0+p2)*t+(2f*p0-5f*p1+4f*p2-p3)*t2+(-p0+3f*p1-3f*p2+p3)*t3);
        }
    }
}
