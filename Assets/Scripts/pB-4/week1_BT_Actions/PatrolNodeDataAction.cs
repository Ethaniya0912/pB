// =============================================================================
// PatrolNodeDataAction.cs  |  pB-4 커스텀 BT Action — 인접 노드 순찰
// 패키지  : Unity Behavior 1.0.15 (com.unity.behavior)
//
// 역할:
//   CaveNodeGraph의 인접 노드를 탐색하여 순찰합니다.
//   지형 태그에 따라 방향 가중치를 적용합니다:
//     DeathTrap 방향 → 가중치 ×0.1 (위험 회피)
//     NarrowPath 방향 → 가중치 ×0.5 (좁은 통로 선호도 낮음)
//   
//   목적지 도착 시 Success → Sequence의 다음 노드(Wait)로 넘어갑니다.
//   Wait 완료 후 Sequence가 반복되어 다시 이 노드가 실행됩니다.
//
// 폴백:
//   CaveNodeGraph가 없거나 인접 노드가 없으면
//   랜덤 방향 10m 이동 (기존 GetWanderLocation 대체).
//
// [v3.3 D3 갱신 — 2026-04-29]
//   ★ BB 변수 표준 정합 (개론서 v9 §1.20 GameBlackboard 스키마 동결):
//     - BlackboardVariable<string> TerrainTags (Wk1 임시 명명, 표준 동결 전 작성)
//        → BlackboardVariable<List<string>> ActiveTerrainTags  (D3 표준)
//     - 사유:
//        ① 개론서 v9 §1.20에서 activeTerrainTags(List<string>) 동결 박제
//        ② DEBT-17에서 사전 등록 7변수 표준 명단에 ActiveTerrainTags 박제
//        ③ D2 BlackboardUpdater가 ActiveTerrainTags + List<string> 직접 주입
//        ④ tech_v2.md / practical_v4.md / day2_techguide.md 등 모든 문서 일관
//     - 영향:
//        실제 코드 내 사용 0건 (변수 선언만 존재, 본 클래스 내부는 폴백 모드).
//        BG editor 인스턴스에서 본 노드의 ActiveTerrainTags 슬롯을
//        BB 변수 ActiveTerrainTags (String List)에 다시 연결 필요.
//   ★ TODO §117-122 가상 코드도 List<string>.Contains() 패턴으로 자연스럽게 작동
//     (List<T>가 .Contains() 메서드 보유, string도 동일 패턴이라 변경 영향 없음).
// =============================================================================
using System;
using System.Collections.Generic;
using Unity.Behavior;
using Unity.Properties;
using UnityEngine;
using UnityEngine.AI;
using Action = Unity.Behavior.Action;

[Serializable, GeneratePropertyBag]
[NodeDescription(
    name: "PatrolNodeData",
    story: "[Self] patrols to adjacent cave node",
    category: "pB-4/Patrol",
    id: "pb4_patrol_nodedata_action")]
public partial class PatrolNodeDataAction : Action
{
    [SerializeReference] public BlackboardVariable<GameObject> Self;

    /// <summary>
    /// 현재 활성 지형 태그 리스트. 방향 가중치 계산에 사용.
    /// ★ v3.3: BB 표준 동결에 맞춰 List<string> 타입 + 명명 ActiveTerrainTags.
    /// 자연 발현 5종 (Narrow/Open/Dense/Sparse/HighGround) +
    /// Wk5+ 복합 태그 (NarrowPath/DeathTrap 등) 모두 포함.
    /// </summary>
    [SerializeReference] public BlackboardVariable<List<string>> ActiveTerrainTags;

    private NavMeshAgent _nav;
    private Transform _selfTransform;

    /// <summary>새 목적지를 설정했는지 플래그. OnStart에서 1회 목적지 설정 후 도착 대기.</summary>
    private bool _destinationSet;

    protected override Status OnStart()
    {
        if (Self.Value == null)
        {
            LogFailure("PatrolNodeDataAction: Self가 null입니다.");
            return Status.Failure;
        }

        _nav = Self.Value.GetComponent<NavMeshAgent>();
        _selfTransform = Self.Value.transform;

        if (_nav == null || !_nav.isOnNavMesh)
        {
            LogFailure("PatrolNodeDataAction: NavMeshAgent 없음.");
            return Status.Failure;
        }

        // ── 목적지 결정 ──
        Vector3 destination = GetPatrolDestination();
        if (NavMesh.SamplePosition(destination, out NavMeshHit hit, 10f, NavMesh.AllAreas))
        {
            _nav.SetDestination(hit.position);
            _destinationSet = true;
        }
        else
        {
            // NavMesh 밖이면 폴백: 랜덤 방향
            Vector3 fallback = _selfTransform.position + UnityEngine.Random.insideUnitSphere * 10f;
            fallback.y = _selfTransform.position.y;
            if (NavMesh.SamplePosition(fallback, out NavMeshHit fbHit, 10f, NavMesh.AllAreas))
            {
                _nav.SetDestination(fbHit.position);
                _destinationSet = true;
            }
            else
            {
                return Status.Failure;
            }
        }

        return Status.Running;
    }

    protected override Status OnUpdate()
    {
        if (!_destinationSet || _nav == null || !_nav.isOnNavMesh)
            return Status.Failure;

        // 도착 판정: 경로가 완료되었거나 잔여 거리가 1m 이내
        if (!_nav.pathPending && _nav.remainingDistance <= 1.0f)
        {
            return Status.Success; // → Sequence 다음 노드(Wait)로 전환
        }

        return Status.Running;
    }

    protected override void OnEnd()
    {
        _destinationSet = false;
    }

    /// <summary>
    /// CaveNodeGraph 인접 노드에서 순찰 목적지를 결정합니다.
    /// 지형 태그로 방향 가중치를 적용합니다.
    /// TODO: CaveNodeGraphBuilder.GetAdjacentNodes() 연동
    /// </summary>
    private Vector3 GetPatrolDestination()
    {
        // ── TODO: 실제 CaveNodeGraph 연동 ──
        // var adjacent = CaveNodeGraphBuilder.Instance.GetAdjacentNodes(currentNodeId);
        // var currentTags = ActiveTerrainTags.Value;  // List<string>, null 안전 처리 필요
        // foreach (var node in adjacent)
        // {
        //     float weight = 1.0f;
        //     if (node.tags.Contains("DeathTrap"))  weight *= 0.1f;
        //     if (node.tags.Contains("NarrowPath")) weight *= 0.5f;
        //     // 또는 현재 태그 기반 회피:
        //     // if (currentTags != null && currentTags.Contains("Narrow"))
        //     //     weight *= (node.tags.Contains("Open") ? 1.5f : 1.0f);
        //     weightedList.Add(node, weight);
        // }
        // return WeightedRandom(weightedList).position;

        // ── 폴백: 랜덤 방향 10m ──
        Vector3 randomDir = UnityEngine.Random.insideUnitSphere * 10f;
        randomDir.y = 0f;
        return _selfTransform.position + randomDir;
    }
}
