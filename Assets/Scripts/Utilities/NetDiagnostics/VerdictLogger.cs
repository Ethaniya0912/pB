using UnityEngine;

namespace NetDiag
{
    /// <summary>
    /// [Step 0 · 계측] VerdictLogger — M5(전투 판정 일치율) 측정기.
    /// 실행계획 v1.1 §0.A.2-2.
    ///
    /// 양측 머신에서 동일 형식의 verdicts.csv 를 기록하고, 세션 후 diff로 판정 일치율을 산출한다.
    /// 데미지 체인의 4개 지점에 1줄씩 이식된다 (게임 로직 변경 없음):
    ///
    ///  ① DEFENSE_EVAL — MeleeWeaponDamageCollider.DamageTarget (공격자 머신)
    ///       : 피격자 방어/패링 심사가 "공격자 머신에서" 수행되는 현행 구조(P0-3)의 직접 증거.
    ///  ② SEND         — MeleeWeaponDamageCollider.SendDamageToServer (공격자 머신)
    ///  ③ RECV         — CharacterNetworkManager.NotifyTheServerOfCharacterDamageClientRpc (전 머신)
    ///       : 같은 사건을 전 머신이 수신하므로, 머신 간 diff의 기준 행.
    ///  ④ HP_APPLY     — TakeDamageEffect.CalculateDamage (피격자 Owner 머신)
    ///       : Owner-Write 수치 차감 지점. 카운터 "verdict.hp_apply.attackerSide" 는
    ///         Step 2 검증(R6 교정: "공격자 측 수치 차감 경로 실행 0건")의 측정기로 그대로 사용된다.
    ///
    /// diff 키: (serverTime 반올림, attackerId, victimId, kind) — Tools\diff_verdicts.ps1 참조.
    /// </summary>
    public static class VerdictLogger
    {
        const string Header = "realtime,frame,serverTime,role,localClient,kind,attackerId,victimId,phys,poise,verdict,extra";

        public static void Log(string kind, ulong attackerId, ulong victimId,
                               float phys, float poise, string verdict, string extra = "")
        {
            if (!NetDiagnostics.Enabled) return;

            NetDiagnostics.IncrementCounter($"verdict.{kind}");
            NetDiagnostics.Row("verdicts.csv", Header,
                $"{Time.realtimeSinceStartup:F3},{Time.frameCount},{NetDiagnostics.ServerTime():F3}," +
                $"{NetDiagnostics.Role()},{NetDiagnostics.LocalClientId()}," +
                $"{kind},{attackerId},{victimId},{phys:F1},{poise:F1},{verdict},{extra}");
        }

        // -------------------------------------------------------------------
        // 체인 지점별 편의 메서드 (호출부 1줄 유지용)
        // -------------------------------------------------------------------

        /// <summary>① 공격자 머신의 방어/패링 심사 결과 (P0-3 분산 권위의 증거 지점).</summary>
        public static void LogDefenseEval(Component attacker, Component victim, float phys, float poise, string defenseResult)
            => Log("DEFENSE_EVAL", NetId(attacker), NetId(victim), phys, poise, defenseResult);

        /// <summary>② 공격자 머신이 서버로 데미지 RPC를 송신.</summary>
        public static void LogSend(ulong attackerId, ulong victimId, float phys, float poise)
            => Log("SEND", attackerId, victimId, phys, poise, "-");

        /// <summary>③ 전 머신이 데미지 브로드캐스트를 수신 (diff 기준 행).</summary>
        public static void LogRecv(ulong attackerId, ulong victimId, float phys, float poise)
            => Log("RECV", attackerId, victimId, phys, poise, "-");

        /// <summary>④ 피격자 Owner 머신의 HP 차감 적용. Step 2의 R6 카운터를 겸한다.</summary>
        public static void LogHpApply(Component victim, Component attacker, int finalDamage, float hpAfter)
        {
            ulong victimId = NetId(victim);
            ulong attackerId = NetId(attacker);
            Log("HP_APPLY", attackerId, victimId, finalDamage, 0, "APPLIED", $"hpAfter={hpAfter:F0}");

            // R6 교정 검증용: 이 차감이 "공격자 본인 머신"에서 실행됐는지 분류.
            // 현행(P0-3): 피격자 Owner 게이트가 있으므로 보통 victim Owner 머신에서만 찍히지만,
            // 호스트는 다수 AI의 Owner이므로 호스트 행이 다수인 것이 정상이다. Step 2 이후
            // "attackerSide" 카운터(공격자=로컬 Owner인데 victim 차감이 실행된 경우)가 0이어야 한다.
            var attackerNetObj = attacker != null ? attacker.GetComponent<Unity.Netcode.NetworkObject>() : null;
            if (attackerNetObj != null && attackerNetObj.IsOwner && victim != attacker)
                NetDiagnostics.IncrementCounter("verdict.hp_apply.attackerSide");
        }

        static ulong NetId(Component c)
        {
            if (c == null) return 0;
            var no = c.GetComponent<Unity.Netcode.NetworkObject>();
            return no != null ? no.NetworkObjectId : 0;
        }
    }
}
