// =============================================================================
// GameBlackboardAdapter.cs  |  pB-4 Week 2 — Day 1 T1.3 (v2 개정)
// 역할: GameBlackboard 싱글톤(MonoBehaviour)을 IBlackboard 인터페이스로 래핑.
//       Brain이 GameBlackboard의 내부 구조 변경에 영향받지 않음.
// 확장: Week 3에서 지형팀이 GameBlackboard를 리팩터링해도 Adapter만 수정하면 됨.
// Mock 테스트 시: Adapter 대신 MockBlackboard(IBlackboard 구현)를 주입.
//
// [v2 코드리뷰 개정]
//   - [GB1] EmptyStringList를 ReadOnlyCollection으로 감싸서 외부 수정 방지.
//           이전: readonly static List<string> → 클라이언트가 .Add() 호출 시 공유 상태 오염.
//           현재: ReadOnlyCollection<string> 반환으로 immutability 보장.
// =============================================================================
using System.Collections.Generic;
using System.Collections.ObjectModel;
using UnityEngine;
using TDA.PB4.Core;
using TDA.PB4.Interfaces.Core;

namespace TDA.PB4.AI
{
    /// <summary>GameBlackboard 싱글톤을 IBlackboard 인터페이스로 감싸는 어댑터.</summary>
    /// <remarks>
    /// Brain은 IBlackboard만 참조. 실제 구현은 이 Adapter가 GameBlackboard로 위임.
    /// Mock 테스트: Adapter 대신 MockBlackboard(IBlackboard 구현)를 주입.
    /// </remarks>
    public class GameBlackboardAdapter : IBlackboard
    {
        private readonly GameBlackboard bb;

        /// <summary>
        /// 빈 태그 리스트. 수정 불가능한 ReadOnlyCollection로 wrap.
        /// </summary>
        /// <remarks>
        /// [GB1] 이전 버전은 static List&lt;string&gt;을 반환 → 클라이언트 수정 시
        /// 다음 호출자에게 오염된 리스트 제공 위험.
        /// ReadOnlyCollection로 감싸면 .Add/.Remove 호출 시 NotSupportedException.
        /// </remarks>
        private static readonly IReadOnlyList<string> EmptyTags =
            new ReadOnlyCollection<string>(new List<string>());

        public GameBlackboardAdapter(GameBlackboard blackboard)
        {
            if (blackboard == null)
                throw new System.ArgumentNullException(nameof(blackboard));
            bb = blackboard;
        }

        /// <summary>현재 활성 지형 태그 목록. ContextAnalyzer가 갱신.</summary>
        public IReadOnlyList<string> GetActiveTerrainTags()
        {
            return bb.ActiveTerrainTags ?? EmptyTags;
        }

        /// <summary>전역 긴장도 점수 (0~1). HNGS가 계산.</summary>
        public float GetGlobalIntensity()
        {
            return bb.GlobalIntensityScore;
        }

        /// <summary>캐릭터별 스탯 조회. key는 'hp'/'stamina' 등.</summary>
        /// <remarks>
        /// GameBlackboard.CurrentCharacterStats 딕셔너리에서 조회.
        /// 존재하지 않는 key는 0f 반환.
        /// [GB2] TODO Day 3: 프로젝트 전체 관례 통일 후 key 포맷 문서화
        /// (예: "{npcId}_hp" vs "hp" 단일 키).
        /// </remarks>
        public float GetCharacterStat(string key)
        {
            if (string.IsNullOrEmpty(key)) return 0f;

            var stats = bb.CurrentCharacterStats;
            if (stats == null) return 0f;

            if (stats.TryGetValue(key, out float value))
                return value;

            return 0f;
        }

        /// <summary>디버그용 현재 상태 덤프.</summary>
        public override string ToString()
        {
            var tags = GetActiveTerrainTags();
            return $"[Adapter] Tags={tags.Count}, Intensity={GetGlobalIntensity():F2}, " +
                   $"Stats={bb.CurrentCharacterStats?.Count ?? 0}";
        }
    }
}
