using UnityEngine;
using TDA.Items;

namespace TDA.Character.AI
{
    /// <summary>
    /// [L3 Domain Layer] 몬스터/NPC 전용 방어 매니저입니다.
    /// 스태미나가 없는 AI 몬스터들의 특성에 맞춰, 확률적 패링이나 딜레이 가드 등을 확장할 수 있습니다.
    /// </summary>
    public class AICharacterDefenseManager : CharacterDefenseManager
    {
        // 추후 AICharacterManager 등 AI 전용 스크립트 연결을 위해 자리 보존
        // private AICharacterManager aiCharacter;

        protected override void Awake()
        {
            base.Awake();
            // aiCharacter = GetComponent<AICharacterManager>();
        }

        // [에러 해결] CS0115: 부모 클래스와 완벽히 동일한 시그니처(ShieldStance 파라미터)를 사용하여 상속 오류 해결
        public override void StartDefense(GuardDirection direction, ShieldStance stance = ShieldStance.CloseGrip)
        {
            // TODO: AI 특화 방어 딜레이 모션(Telegraph) 로직 등 기획 추가 시 여기에 작성
            if (showDebugLogs) Debug.Log($"<color=magenta>[AIDefense]</color> {gameObject.name}가 {stance} 자세로 가드를 올립니다.");
            base.StartDefense(direction, stance);
        }

        public override DefenseResult EvaluateDefense(HitEventData attackInfo)
        {
            // 1. 부모 클래스의 물리/내구도 방어 판정 실행
            DefenseResult result = base.EvaluateDefense(attackInfo);

            // 2. AI 난이도(Difficulty)에 따른 억지 패링 보정 로직 (주석 처리된 기획 예시)
            /* if (result == DefenseResult.Blocked)
            {
                // 보스급 몬스터는 막아냈을 때 20% 확률로 플레이어의 무기를 패링 쳐버림
                if (Random.Range(0, 100) < aiCharacter.parryChance) 
                {
                    if (showDebugLogs) Debug.Log($"<color=magenta>[AIDefense]</color> {gameObject.name}가 강제 패링을 터뜨렸습니다!");
                    return DefenseResult.Parried;
                }
            }
            */

            return result;
        }
    }
}