// =============================================================================
// CharacterDomainInterfaces.cs  |  Character 도메인 내부 인터페이스 (NGO)
// Layer  : Character Domain Internal
// Namespace: TDA.PB4.Interfaces.Intelligence (Wk0 유지)
//
// 역할:
//   Character / Player 도메인의 NGO 관련 인터페이스.
//   v3 NGO 2.0 + Day 3 신규 인터페이스 통합.
//
// 수록:
//   [v3 NGO-A] ITrustProvider (N:M 관계, ulong playerId 오버로드)
//   [Wk0] ITraumaProvider, IAlignmentProvider
//   [Day 3 T3.5] ICommandFilter + CommandSeverity / CommandRequest / FixedCommandId
//
// 이력:
//   2026-04-23 v3 — PB4Interfaces.cs Intelligence namespace 의 Character 관련
//   2026-05-11 정리 — CharacterDomainInterfaces.cs 로 분리 (Interfaces/Domain/)
//                    파일명에서 PB4 접두사 제거
// =============================================================================
using System;
using Unity.Netcode;
using UnityEngine;

namespace TDA.PB4.Interfaces.Intelligence
{
    // ═══════════════════════════════════════════════════════════════════════════
    // [NGO-A] ITrustProvider — v3 N:M 관계 확장
    // ═══════════════════════════════════════════════════════════════════════════
    /// <summary>
    /// NPC의 플레이어에 대한 신뢰도 조회·수정. 4 Tier 구조.
    /// Hostility(0~19) / Doubt(20~59) / Cooperation(60~89) / BlindTrust(90~100).
    /// [v3 NGO-A] N:M 관계 — 멀티플레이어 환경에서는 ulong playerId 오버로드 사용.
    /// 기존 API(매개변수 없음)는 단독 Play 또는 "현재 로컬 플레이어" 의미로 유지.
    /// </summary>
    public interface ITrustProvider
    {
        // ─── 기존 API (Day 2 호환) ─────────────────────────────────
        float GetNormalizedTrust();
        int GetCurrentTierIndex();
        void AddDelta(float amount, string reason);
        bool EvaluateCommand(float commandSeverity, float npcFear);

        // ─── [NGO-A] 신규 N:M API (Day 3~) ─────────────────────────
        float GetNormalizedTrust(ulong playerId);
        int GetCurrentTierIndex(ulong playerId);
        void AddDelta(ulong playerId, float amount, string reason);
        bool EvaluateCommand(ulong playerId, float commandSeverity, float npcFear);
    }

    /// <summary>NPC의 트라우마 상태. 4 Stage: None/AcuteShock/Crossroads/PermanentScarring.</summary>
    public interface ITraumaProvider
    {
        int CurrentStageIndex { get; }
        void ApplyShock(float intensity, string causeId);
        float GetFearMultiplier();
    }

    /// <summary>NPC의 진영(Alignment) 상태 조회.</summary>
    /// <remarks>
    /// 4 Alignment: Hostile=0 / Neutral=1 / Friendly=2 / Companion=3.
    /// 매 1초 trust + karma 기반 동적 전이 평가 (NPCAlignmentController, Day 3 T3.2).
    /// </remarks>
    public interface IAlignmentProvider
    {
        int CurrentAlignmentIndex { get; }
        bool IsHostileTo(UnityEngine.GameObject other);
        event System.Action<int, int> OnAlignmentChanged;
    }

    // ═══════════════════════════════════════════════════════════════════════════
    // [Day 3 T3.5 신규] ICommandFilter — 명령 수락/거부 판정
    // ═══════════════════════════════════════════════════════════════════════════

    /// <summary>명령 위험도 5단계.</summary>
    public enum CommandSeverity
    {
        Trivial = 0,     // 가만히 있어, 여기 있어 — 거의 무조건 수락
        Minor = 1,       // 따라와, 줍다 — 낮은 신뢰도 거부
        Moderate = 2,    // 공격해, 밀어내 — 중간 신뢰도 필요
        Severe = 3,      // 혼자 적 막아 — 높은 신뢰도 + Trust Companion 필요
        Suicidal = 4     // 폭탄 들고 돌격 — Blind Trust 외 거의 거부
    }

    /// <summary>명령 요청 구조체.</summary>
    [Serializable]
    public struct CommandRequest : INetworkSerializable
    {
        public ulong issuerPlayerId;          // 명령자 clientId
        public FixedCommandId commandId;      // 명령 종류
        public CommandSeverity severity;      // 위험도
        public Vector3 targetPosition;        // 좌표
        public ulong targetEntityId;          // 타겟 NetworkObjectId (0=없음)

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
        {
            s.SerializeValue(ref issuerPlayerId);
            s.SerializeValue(ref commandId);
            int sev = (int)severity;
            s.SerializeValue(ref sev);
            severity = (CommandSeverity)sev;
            s.SerializeValue(ref targetPosition);
            s.SerializeValue(ref targetEntityId);
        }
    }

    /// <summary>명령 ID (FixedString32으로 네트워크 전송).</summary>
    [Serializable]
    public struct FixedCommandId : INetworkSerializable, IEquatable<FixedCommandId>
    {
        public Unity.Collections.FixedString32Bytes value;

        public static FixedCommandId From(string s) =>
            new FixedCommandId { value = new Unity.Collections.FixedString32Bytes(s) };

        public override string ToString() => value.ToString();
        public bool Equals(FixedCommandId o) => value.Equals(o.value);

        public void NetworkSerialize<T>(BufferSerializer<T> s) where T : IReaderWriter
            => s.SerializeValue(ref value);
    }

    /// <summary>명령 수락 판정 계약.</summary>
    /// <remarks>
    /// 구현체: CommandAcceptanceFilter (Day 3 T3.5).
    /// 수식: acceptance = Trust × (1 - Severity/4) × (1/FearMult) × Loyalty
    /// </remarks>
    public interface ICommandFilter
    {
        /// <summary>명령 수락 여부 결정. 서버 권한 검증.</summary>
        bool TryAccept(CommandRequest request, out float acceptance, out string reason);

        /// <summary>명령 수락 임계값. 기본 0.3.</summary>
        float AcceptanceThreshold { get; set; }
    }
}
