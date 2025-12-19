using UnityEngine;
using System.Collections.Generic;
using System.Linq;

namespace SG
{
    public static class MeshSlicer
    {
        public class SlicedHullResult
        {
            public GameObject UpperHull;
            public GameObject LowerHull;
        }

        public static SlicedHullResult Slice(GameObject target, Vector3 planePosition, Vector3 planeNormal, Material crossSectionMat)
        {
            Vector3 localPlanePos = target.transform.InverseTransformPoint(planePosition);
            Vector3 localPlaneNormal = target.transform.InverseTransformDirection(planeNormal).normalized;
            Plane plane = new Plane(localPlaneNormal, localPlanePos);

            MeshFilter mf = target.GetComponent<MeshFilter>();
            if (mf == null) return null;
            Mesh originalMesh = mf.sharedMesh;

            MeshData upperData = new MeshData();
            MeshData lowerData = new MeshData();

            Vector3[] vertices = originalMesh.vertices;
            int[] triangles = originalMesh.triangles;
            Vector2[] uvs = originalMesh.uv;
            Vector3[] normals = originalMesh.normals;

            // 1. 표면 절단
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v1 = vertices[triangles[i]];
                Vector3 v2 = vertices[triangles[i + 1]];
                Vector3 v3 = vertices[triangles[i + 2]];

                bool side1 = plane.GetSide(v1);
                bool side2 = plane.GetSide(v2);
                bool side3 = plane.GetSide(v3);

                if (side1 == side2 && side2 == side3)
                {
                    MeshData targetData = side1 ? upperData : lowerData;
                    targetData.AddTriangle(
                        v1, v2, v3,
                        uvs[triangles[i]], uvs[triangles[i + 1]], uvs[triangles[i + 2]],
                        normals[triangles[i]], normals[triangles[i + 1]], normals[triangles[i + 2]],
                        true);
                }
                else
                {
                    CutTriangle(plane, upperData, lowerData,
                        v1, v2, v3,
                        uvs[triangles[i]], uvs[triangles[i + 1]], uvs[triangles[i + 2]],
                        normals[triangles[i]], normals[triangles[i + 1]], normals[triangles[i + 2]],
                        side1, side2, side3);
                }
            }

            // 2. 단면 채우기
            // UpperHull은 -Normal 방향, LowerHull은 +Normal 방향을 바라봐야 함
            FillCap(upperData, plane, -localPlaneNormal);
            FillCap(lowerData, plane, localPlaneNormal);

            // 3. 결과 생성
            SlicedHullResult result = new SlicedHullResult();
            result.UpperHull = CreateHullObject(target, upperData, "Upper", crossSectionMat);
            result.LowerHull = CreateHullObject(target, lowerData, "Lower", crossSectionMat);

            return result;
        }

        private class MeshData
        {
            public List<Vector3> Vertices = new List<Vector3>();
            public List<Vector2> UVs = new List<Vector2>();
            public List<Vector3> Normals = new List<Vector3>();
            public List<int> SurfaceTriangles = new List<int>();
            public List<int> CapTriangles = new List<int>();
            public List<Vector3> CapVerts = new List<Vector3>();

            public void AddTriangle(Vector3 v1, Vector3 v2, Vector3 v3, Vector2 uv1, Vector2 uv2, Vector2 uv3, Vector3 n1, Vector3 n2, Vector3 n3, bool isSurface)
            {
                int baseIndex = Vertices.Count;
                Vertices.Add(v1); Vertices.Add(v2); Vertices.Add(v3);
                UVs.Add(uv1); UVs.Add(uv2); UVs.Add(uv3);
                Normals.Add(n1); Normals.Add(n2); Normals.Add(n3);

                if (isSurface) { SurfaceTriangles.Add(baseIndex); SurfaceTriangles.Add(baseIndex + 1); SurfaceTriangles.Add(baseIndex + 2); }
                else { CapTriangles.Add(baseIndex); CapTriangles.Add(baseIndex + 1); CapTriangles.Add(baseIndex + 2); }
            }
        }

        private static void CutTriangle(Plane plane, MeshData upper, MeshData lower, Vector3 v1, Vector3 v2, Vector3 v3, Vector2 uv1, Vector2 uv2, Vector2 uv3, Vector3 n1, Vector3 n2, Vector3 n3, bool s1, bool s2, bool s3)
        {
            if (s1 == s2) { Swap(ref v1, ref v3); Swap(ref v2, ref v3); Swap(ref uv1, ref uv3); Swap(ref uv2, ref uv3); Swap(ref n1, ref n3); Swap(ref n2, ref n3); Swap(ref s1, ref s3); Swap(ref s2, ref s3); }
            else if (s2 == s3) { }
            else { Swap(ref v1, ref v2); Swap(ref v2, ref v3); Swap(ref uv1, ref uv2); Swap(ref uv2, ref uv3); Swap(ref n1, ref n2); Swap(ref n2, ref n3); Swap(ref s1, ref s2); Swap(ref s2, ref s3); }

            float enter;
            Ray ray1 = new Ray(v1, v2 - v1); plane.Raycast(ray1, out enter);
            float lerp1 = enter / Vector3.Distance(v1, v2);
            Vector3 i1 = ray1.GetPoint(enter); Vector2 uv_i1 = Vector2.Lerp(uv1, uv2, lerp1); Vector3 n_i1 = Vector3.Lerp(n1, n2, lerp1);

            Ray ray2 = new Ray(v1, v3 - v1); plane.Raycast(ray2, out enter);
            float lerp2 = enter / Vector3.Distance(v1, v3);
            Vector3 i2 = ray2.GetPoint(enter); Vector2 uv_i2 = Vector2.Lerp(uv1, uv3, lerp2); Vector3 n_i2 = Vector3.Lerp(n1, n3, lerp2);

            MeshData single = s1 ? upper : lower;
            single.AddTriangle(v1, i1, i2, uv1, uv_i1, uv_i2, n1, n_i1, n_i2, true);
            single.CapVerts.Add(i1); single.CapVerts.Add(i2);

            MeshData doub = s1 ? lower : upper;
            doub.AddTriangle(i1, v2, v3, uv_i1, uv2, uv3, n_i1, n2, n3, true);
            doub.AddTriangle(i1, v3, i2, uv_i1, uv3, uv_i2, n_i1, n3, n_i2, true);
            doub.CapVerts.Add(i2); doub.CapVerts.Add(i1);
        }

        private static void FillCap(MeshData data, Plane plane, Vector3 faceNormal)
        {
            var distinctVerts = data.CapVerts.Distinct().ToList();
            if (distinctVerts.Count < 3) return;

            // 1. 중심점 계산
            Vector3 center = Vector3.zero;
            foreach (var v in distinctVerts) center += v;
            center /= distinctVerts.Count;

            // 2. 정렬을 위한 2D 기준축
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(faceNormal, up)) > 0.9f) up = Vector3.forward;
            Vector3 right = Vector3.Cross(faceNormal, up).normalized;
            up = Vector3.Cross(right, faceNormal).normalized;

            // 3. 각도순 정렬
            distinctVerts.Sort((a, b) =>
            {
                float angleA = Mathf.Atan2(Vector3.Dot(a - center, up), Vector3.Dot(a - center, right));
                float angleB = Mathf.Atan2(Vector3.Dot(b - center, up), Vector3.Dot(b - center, right));
                return angleA.CompareTo(angleB);
            });

            // 4. UV 및 삼각형 생성
            Vector2 GetUV(Vector3 p) => new Vector2(Vector3.Dot(p, right), Vector3.Dot(p, up)) * 0.5f;

            for (int i = 0; i < distinctVerts.Count; i++)
            {
                Vector3 v1 = distinctVerts[i];
                Vector3 v2 = distinctVerts[(i + 1) % distinctVerts.Count];

                // [Fix] Winding Order Check: 
                // 생성될 삼각형의 법선이 faceNormal과 같은 방향인지 확인하고, 아니면 순서를 뒤집음
                Vector3 triNormal = Vector3.Cross(v2 - v1, center - v1).normalized;

                if (Vector3.Dot(triNormal, faceNormal) < 0)
                {
                    // 방향이 반대면 순서 뒤집기 (v2 -> v1)
                    data.AddTriangle(v2, v1, center,
                        GetUV(v2), GetUV(v1), GetUV(center),
                        faceNormal, faceNormal, faceNormal, false);
                }
                else
                {
                    // 정방향 (v1 -> v2)
                    data.AddTriangle(v1, v2, center,
                        GetUV(v1), GetUV(v2), GetUV(center),
                        faceNormal, faceNormal, faceNormal, false);
                }
            }
        }

        private static GameObject CreateHullObject(GameObject original, MeshData data, string suffix, Material capMat)
        {
            if (data.Vertices.Count == 0) return null;

            GameObject hull = new GameObject(original.name + "_" + suffix);
            hull.transform.position = original.transform.position;
            hull.transform.rotation = original.transform.rotation;
            hull.transform.localScale = original.transform.localScale;
            hull.tag = original.tag;
            hull.layer = original.layer; // Layer 복사 중요 (Weapon이 인식해야 함)

            Mesh mesh = new Mesh();
            mesh.name = "GeneratedMesh_" + suffix;
            mesh.vertices = data.Vertices.ToArray();
            mesh.uv = data.UVs.ToArray();
            mesh.normals = data.Normals.ToArray();
            mesh.subMeshCount = 2;
            mesh.SetTriangles(data.SurfaceTriangles, 0);
            mesh.SetTriangles(data.CapTriangles, 1);
            mesh.RecalculateBounds();

            MeshFilter mf = hull.AddComponent<MeshFilter>();
            mf.mesh = mesh;

            MeshRenderer mr = hull.AddComponent<MeshRenderer>();
            Material[] originalMats = original.GetComponent<MeshRenderer>().sharedMaterials;
            Material surfaceMat = (originalMats.Length > 0) ? originalMats[0] : null;
            mr.materials = new Material[] { surfaceMat, capMat };

            // [중요] SlicedHull을 추가하고 초기화될 때까지 대기
            hull.AddComponent<SlicedHull>();

            SliceableObject originalSliceable = original.GetComponent<SliceableObject>();
            if (originalSliceable != null)
            {
                SliceableObject newSliceable = hull.AddComponent<SliceableObject>();
                newSliceable.crossSectionMaterial = originalSliceable.crossSectionMaterial;
                newSliceable.separationForce = originalSliceable.separationForce;

                int nextCount = originalSliceable.CurrentSliceCount + 1;
                newSliceable.SetSliceCount(nextCount);
                newSliceable.ingredientItem = originalSliceable.ingredientItem;
            }

            return hull;
        }

        private static void Swap<T>(ref T lhs, ref T rhs) { T temp = lhs; lhs = rhs; rhs = temp; }
    }
}