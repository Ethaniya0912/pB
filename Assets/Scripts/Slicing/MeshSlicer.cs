using UnityEngine;
using System.Collections.Generic;

namespace SG
{
    /// <summary>
    /// [Dev A] GameObject를 평면 기준으로 2개로 분할하는 핵심 유틸리티 클래스
    /// </summary>
    public static class MeshSlicer
    {
        // 절단 결과를 반환할 구조체
        public class SlicedHullResult
        {
            public GameObject UpperHull;
            public GameObject LowerHull;
        }

        /// <summary>
        /// 대상을 주어진 평면으로 자릅니다.
        /// </summary>
        /// <param name="target">자를 대상 GameObject</param>
        /// <param name="planePosition">평면 위의 한 점 (충돌 지점)</param>
        /// <param name="planeNormal">평면의 법선 벡터 (자르는 방향의 수직)</param>
        /// <param name="crossSectionMat">단면 재질</param>
        public static SlicedHullResult Slice(GameObject target, Vector3 planePosition, Vector3 planeNormal, Material crossSectionMat)
        {
            // 1. 로컬 좌표계로 변환 (메쉬 연산은 로컬에서 수행)
            // 평면을 타겟 오브젝트의 로컬 공간으로 변환
            Vector3 localPlanePos = target.transform.InverseTransformPoint(planePosition);
            Vector3 localPlaneNormal = target.transform.InverseTransformDirection(planeNormal).normalized;
            Plane plane = new Plane(localPlaneNormal, localPlanePos);

            // 2. 원본 메쉬 데이터 가져오기
            MeshFilter mf = target.GetComponent<MeshFilter>();
            if (mf == null) return null;
            Mesh originalMesh = mf.sharedMesh; // sharedMesh 사용 (원본 데이터)

            // 3. 메쉬 데이터 초기화
            MeshData upperData = new MeshData();
            MeshData lowerData = new MeshData();

            // 4. 삼각형 순회 및 분류 (핵심 알고리즘)
            Vector3[] vertices = originalMesh.vertices;
            int[] triangles = originalMesh.triangles;
            Vector2[] uvs = originalMesh.uv;
            Vector3[] normals = originalMesh.normals;

            // 3개의 정점으로 이루어진 삼각형 단위로 처리
            for (int i = 0; i < triangles.Length; i += 3)
            {
                Vector3 v1 = vertices[triangles[i]];
                Vector3 v2 = vertices[triangles[i + 1]];
                Vector3 v3 = vertices[triangles[i + 2]];

                bool side1 = plane.GetSide(v1);
                bool side2 = plane.GetSide(v2);
                bool side3 = plane.GetSide(v3);

                // 모든 점이 한쪽에 있는 경우 (자르지 않음)
                if (side1 == side2 && side2 == side3)
                {
                    MeshData targetData = side1 ? upperData : lowerData;
                    targetData.AddTriangle(v1, v2, v3, uvs[triangles[i]], uvs[triangles[i + 1]], uvs[triangles[i + 2]], normals[triangles[i]], normals[triangles[i + 1]], normals[triangles[i + 2]]);
                }
                else
                {
                    // 삼각형이 평면에 의해 잘리는 경우 (교차점 계산 필요)
                    CutTriangle(plane, upperData, lowerData,
                        v1, v2, v3,
                        uvs[triangles[i]], uvs[triangles[i + 1]], uvs[triangles[i + 2]],
                        normals[triangles[i]], normals[triangles[i + 1]], normals[triangles[i + 2]],
                        side1, side2, side3);
                }
            }

            // 5. 단면(Cap) 채우기
            // 간단한 구현을 위해 무게중심(Barycenter)을 이용한 Fan Triangulation 사용
            FillCap(upperData, plane, crossSectionMat);
            FillCap(lowerData, plane, crossSectionMat); // 주의: 반대 방향 처리는 내부에서

            // 6. 실제 GameObject 생성 (Dev B가 NGO Spawn 등을 하기 편하도록 오브젝트 생성)
            // 원본은 Dev B가 처리(Disable/Destroy)하도록 둠
            SlicedHullResult result = new SlicedHullResult();

            result.UpperHull = CreateHullObject(target, upperData, "Upper", crossSectionMat);
            result.LowerHull = CreateHullObject(target, lowerData, "Lower", crossSectionMat);

            return result;
        }

        // --- 내부 헬퍼 클래스 및 메서드 ---

        private class MeshData
        {
            public List<Vector3> Vertices = new List<Vector3>();
            public List<int> Triangles = new List<int>();
            public List<Vector2> UVs = new List<Vector2>();
            public List<Vector3> Normals = new List<Vector3>();

            // 단면 생성을 위한 임시 저장소 (교차점들)
            public List<Vector3> CapVerts = new List<Vector3>();

            public void AddTriangle(Vector3 v1, Vector3 v2, Vector3 v3, Vector2 uv1, Vector2 uv2, Vector2 uv3, Vector3 n1, Vector3 n2, Vector3 n3)
            {
                int baseIndex = Vertices.Count;
                Vertices.Add(v1); Vertices.Add(v2); Vertices.Add(v3);
                UVs.Add(uv1); UVs.Add(uv2); UVs.Add(uv3);
                Normals.Add(n1); Normals.Add(n2); Normals.Add(n3);
                Triangles.Add(baseIndex); Triangles.Add(baseIndex + 1); Triangles.Add(baseIndex + 2);
            }
        }

        // 삼각형 절단 로직 (Intersection)
        private static void CutTriangle(Plane plane, MeshData upper, MeshData lower,
            Vector3 v1, Vector3 v2, Vector3 v3,
            Vector2 uv1, Vector2 uv2, Vector2 uv3,
            Vector3 n1, Vector3 n2, Vector3 n3,
            bool s1, bool s2, bool s3)
        {
            // 로직 단순화를 위해 정점 순서를 재배열하여 '혼자 다른 쪽에 있는 점'을 v1으로 만듦
            if (s1 == s2) // v3가 혼자 다름
            {
                // Shift: v1->v3, v2->v1, v3->v2
                // 재귀 호출이나 값 교환으로 처리 가능. 여기선 값 교환.
                Swap(ref v1, ref v3); Swap(ref v2, ref v3); // v3->v1
                Swap(ref uv1, ref uv3); Swap(ref uv2, ref uv3);
                Swap(ref n1, ref n3); Swap(ref n2, ref n3);
                Swap(ref s1, ref s3); Swap(ref s2, ref s3);
            }
            else if (s2 == s3) // v1이 혼자 다름. 이미 정렬됨.
            {
                // Do nothing
            }
            else // s1 == s3, v2가 혼자 다름
            {
                Swap(ref v1, ref v2); Swap(ref v2, ref v3); // v2->v1
                Swap(ref uv1, ref uv2); Swap(ref uv2, ref uv3);
                Swap(ref n1, ref n2); Swap(ref n2, ref n3);
                Swap(ref s1, ref s2); Swap(ref s2, ref s3);
            }

            // 이제 v1은 혼자 side1에 있고, v2, v3는 반대편에 있음.
            // 교차점 계산
            float enter;
            Ray ray1 = new Ray(v1, v2 - v1);
            plane.Raycast(ray1, out enter);
            float lerp1 = enter / Vector3.Distance(v1, v2);
            Vector3 intersect1 = ray1.GetPoint(enter);
            Vector2 uvInt1 = Vector2.Lerp(uv1, uv2, lerp1);
            Vector3 nInt1 = Vector3.Lerp(n1, n2, lerp1);

            Ray ray2 = new Ray(v1, v3 - v1);
            plane.Raycast(ray2, out enter);
            float lerp2 = enter / Vector3.Distance(v1, v3);
            Vector3 intersect2 = ray2.GetPoint(enter);
            Vector2 uvInt2 = Vector2.Lerp(uv1, uv3, lerp2);
            Vector3 nInt2 = Vector3.Lerp(n1, n3, lerp2);

            // 데이터 추가
            // 혼자 있는 쪽(v1)은 삼각형 1개 생성
            MeshData singleSide = s1 ? upper : lower;
            singleSide.AddTriangle(v1, intersect1, intersect2, uv1, uvInt1, uvInt2, n1, nInt1, nInt2);

            // 단면 생성을 위한 교차점 기록 (순서 중요)
            singleSide.CapVerts.Add(intersect1);
            singleSide.CapVerts.Add(intersect2);

            // 둘이 있는 쪽(v2, v3)은 사각형이 되므로 삼각형 2개 생성
            MeshData doubleSide = s1 ? lower : upper;
            doubleSide.AddTriangle(intersect1, v2, v3, uvInt1, uv2, uv3, nInt1, n2, n3);
            doubleSide.AddTriangle(intersect1, v3, intersect2, uvInt1, uv3, uvInt2, nInt1, n3, nInt2);

            // 단면 생성을 위한 교차점 기록
            doubleSide.CapVerts.Add(intersect2);
            doubleSide.CapVerts.Add(intersect1); // 반대 방향이므로 순서 뒤집기? (나중에 Normal 체크)
        }

        private static void FillCap(MeshData data, Plane plane, Material mat)
        {
            if (data.CapVerts.Count < 3) return;

            // 간단한 중심점(Centroid) 방식 사용
            Vector3 center = Vector3.zero;
            foreach (var v in data.CapVerts) center += v;
            center /= data.CapVerts.Count;

            // UV 매핑을 위해 평면 기준 좌표계 생성
            Vector3 up = Vector3.up;
            if (Mathf.Abs(Vector3.Dot(plane.normal, up)) > 0.9f) up = Vector3.forward;
            Vector3 right = Vector3.Cross(plane.normal, up).normalized;
            up = Vector3.Cross(right, plane.normal).normalized;

            Vector2 GetCapUV(Vector3 point)
            {
                // 월드 좌표 기반 UV 매핑 (단면 텍스처가 이어지게)
                return new Vector2(Vector3.Dot(point, right), Vector3.Dot(point, up)) * 0.5f; // 0.5f는 스케일
            }

            // CapVerts는 쌍(Pair)으로 들어오므로, 이를 루프(Loop)로 연결하는 과정이 필요하지만
            // 여기서는 성능과 코드량을 위해 단순히 추가된 순서대로 인접한다고 가정하고 Fan을 만듭니다.
            // (실제 프로덕션에서는 Convex Hull 알고리즘 필요)

            for (int i = 0; i < data.CapVerts.Count; i += 2)
            {
                // 교차점 쌍 (Intersection Edge)
                Vector3 v1 = data.CapVerts[i];
                Vector3 v2 = data.CapVerts[i + 1];

                // 중심점과 연결하여 삼각형 생성
                // Normal은 평면의 Normal을 따름 (혹은 반대)
                data.AddTriangle(v1, v2, center,
                    GetCapUV(v1), GetCapUV(v2), GetCapUV(center),
                    plane.normal, plane.normal, plane.normal);
            }

            // 주의: 위 로직은 Cap이 닫혀있지 않으면 구멍이 뚫릴 수 있음.
            // Step 4 단계에서는 이 정도 근사치로 진행하고 추후 고도화.
        }

        private static GameObject CreateHullObject(GameObject original, MeshData data, string suffix, Material capMat)
        {
            if (data.Vertices.Count == 0) return null;

            GameObject hull = new GameObject(original.name + "_" + suffix);
            hull.transform.position = original.transform.position;
            hull.transform.rotation = original.transform.rotation;
            hull.transform.localScale = original.transform.localScale;

            // 태그 및 레이어 복사
            hull.tag = original.tag;
            hull.layer = original.layer;

            // Mesh 생성
            Mesh mesh = new Mesh();
            mesh.name = "GeneratedMesh_" + suffix;
            mesh.vertices = data.Vertices.ToArray();
            mesh.triangles = data.Triangles.ToArray();
            mesh.uv = data.UVs.ToArray();
            mesh.normals = data.Normals.ToArray();
            mesh.RecalculateBounds();
            // mesh.RecalculateTangents(); // 필요시

            // Renderer 설정
            MeshFilter mf = hull.AddComponent<MeshFilter>();
            mf.mesh = mesh;

            MeshRenderer mr = hull.AddComponent<MeshRenderer>();
            // Material 배열 관리 (원본 재질 유지 + 단면 재질 추가)
            Material[] originalMats = original.GetComponent<MeshRenderer>().sharedMaterials;
            // 여기서는 단순화를 위해 모든 서브메쉬를 하나로 합쳤다고 가정하거나, 첫 번째 재질만 사용
            // 실제로는 SubMesh 인덱스 관리 필요.
            mr.material = originalMats[0];
            // *단면 재질 적용 로직은 SubMesh 분리가 필요하나, Step 4에서는 전체 재질 유지로 타협하거나
            // MeshSlicer가 SubMesh를 지원하도록 확장해야 함. (현재는 단일 재질 가정)

            // 컴포넌트 복사 및 설정
            hull.AddComponent<SlicedHull>(); // Dev A가 만든 후처리 스크립트

            // 기존 SliceableObject 복사 (재귀적 절단을 위해)
            SliceableObject originalSliceable = original.GetComponent<SliceableObject>();
            if (originalSliceable != null)
            {
                SliceableObject newSliceable = hull.AddComponent<SliceableObject>();
                newSliceable.crossSectionMaterial = originalSliceable.crossSectionMaterial;
                newSliceable.separationForce = originalSliceable.separationForce;
                // Count는 외부에서 처리 (Increment)
            }

            return hull;
        }

        private static void Swap<T>(ref T lhs, ref T rhs) { T temp = lhs; lhs = rhs; rhs = temp; }
    }
}