// ============================================================
// GameConfig.cs - 게임 설정 상수 및 밸런스 데이터
// Battle Car Sumo - 1v1 Server Authoritative Physics Battle
// ============================================================

using UnityEngine;

namespace BattleCarSumo.Data
{
    /// <summary>
    /// 게임 전역 설정 (ScriptableObject)
    /// Inspector에서 밸런스 조정 가능
    /// </summary>
    [CreateAssetMenu(fileName = "GameConfig", menuName = "BattleCarSumo/Game Config")]
    public class GameConfig : ScriptableObject
    {
        [Header("=== 매치 설정 ===")]
        [Tooltip("승리에 필요한 라운드 수")]
        public int roundsToWin = 2;

        [Tooltip("최대 라운드 수")]
        public int maxRounds = 3;

        [Tooltip("라운드 시간 (초)")]
        public float roundDuration = 180f; // 3분

        [Tooltip("인터미션 시간 (초)")]
        public float intermissionDuration = 30f;

        [Tooltip("라운드 시작 카운트다운 (초)")]
        public float countdownDuration = 3f;

        [Tooltip("라운드 결과 표시 시간 (초)")]
        public float roundResultDisplayDuration = 3f;

        [Header("=== 경기장 설정 ===")]
        [Tooltip("경기장 반지름")]
        public float arenaRadius = 15f;

        [Tooltip("경기장 외곽 낙하 판정 높이")]
        public float fallThreshold = -5f;

        [Tooltip("스폰 포인트 간 거리 (중심으로부터)")]
        public float spawnDistanceFromCenter = 8f;

        [Header("=== 체급별 물리 설정 ===")]
        public WeightClassPhysics lightClass = new WeightClassPhysics
        {
            maxSpeed = 12f,
            acceleration = 8f,
            turnSpeed = 180f,
            baseMass = 800f,
            maxTotalWeight = 1200f,
            linearDrag = 2f,
            angularDrag = 5f
        };

        public WeightClassPhysics middleClass = new WeightClassPhysics
        {
            maxSpeed = 9f,
            acceleration = 6f,
            turnSpeed = 140f,
            baseMass = 1200f,
            maxTotalWeight = 1800f,
            linearDrag = 3f,
            angularDrag = 6f
        };

        public WeightClassPhysics heavyClass = new WeightClassPhysics
        {
            maxSpeed = 6f,
            acceleration = 4f,
            turnSpeed = 100f,
            baseMass = 1800f,
            maxTotalWeight = 2500f,
            linearDrag = 4f,
            angularDrag = 7f
        };

        [Header("=== 부품 액션 쿨다운 ===")]
        [Tooltip("부품 액션 글로벌 쿨다운 (초)")]
        public float actionGlobalCooldown = 0.5f;

        [Header("=== 네트워크 설정 ===")]
        [Tooltip("물리 동기화 틱 레이트 (Hz)")]
        public int physicsSyncRate = 30;

        [Tooltip("클라이언트 보간 지연 (초)")]
        public float interpolationDelay = 0.1f;

        // ===================================================================
        // PascalCase 프로퍼티 - 다른 스크립트에서 일관된 접근용
        // ===================================================================

        /// <summary>라운드 시간 (초)</summary>
        public float RoundDuration => roundDuration;

        /// <summary>인터미션 시간 (초)</summary>
        public float IntermissionDuration => intermissionDuration;

        /// <summary>카운트다운 시간 (초)</summary>
        public float CountdownDuration => countdownDuration;

        /// <summary>라운드 결과 표시 시간 (초)</summary>
        public float RoundResultDisplayDuration => roundResultDisplayDuration;

        /// <summary>경기장 반지름</summary>
        public float ArenaRadius => arenaRadius;

        /// <summary>낙하 판정 높이</summary>
        public float FallThreshold => fallThreshold;

        /// <summary>스폰 거리</summary>
        public float SpawnDistanceFromCenter => spawnDistanceFromCenter;

        /// <summary>
        /// 체급별 최대 허용 무게 반환
        /// </summary>
        public float GetMaxWeightForClass(WeightClass weightClass)
        {
            return GetPhysicsForClass(weightClass).maxTotalWeight;
        }

        /// <summary>
        /// 체급에 해당하는 물리 설정 반환
        /// </summary>
        public WeightClassPhysics GetPhysicsForClass(WeightClass weightClass)
        {
            return weightClass switch
            {
                WeightClass.Light => lightClass,
                WeightClass.Middle => middleClass,
                WeightClass.Heavy => heavyClass,
                _ => middleClass
            };
        }
    }

    /// <summary>
    /// 체급별 물리 파라미터
    /// </summary>
    [System.Serializable]
    public struct WeightClassPhysics
    {
        [Tooltip("최대 속도 (m/s)")]
        public float maxSpeed;

        [Tooltip("가속력")]
        public float acceleration;

        [Tooltip("회전 속도 (도/초)")]
        public float turnSpeed;

        [Tooltip("차량 본체 기본 질량 (kg)")]
        public float baseMass;

        [Tooltip("체급 최대 허용 무게 (본체+부품 합산)")]
        public float maxTotalWeight;

        [Tooltip("선형 저항")]
        public float linearDrag;

        [Tooltip("각속도 저항")]
        public float angularDrag;
    }
}
