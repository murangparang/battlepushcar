// ============================================================
// WeightClassData.cs - 체급 데이터 및 유틸리티
// Battle Car Sumo - 1v1 Server Authoritative Physics Battle
// ============================================================

using UnityEngine;

namespace BattleCarSumo.Data
{
    /// <summary>
    /// 체급 시스템 유틸리티 클래스
    /// 총 무게 계산 및 체급 판별, 무게 제한 검증
    /// </summary>
    public static class WeightClassUtility
    {
        /// <summary>
        /// 총 무게로 체급 자동 판별
        /// </summary>
        public static WeightClass DetermineWeightClass(float totalWeight, GameConfig config)
        {
            if (totalWeight <= config.lightClass.maxTotalWeight)
                return WeightClass.Light;
            else if (totalWeight <= config.middleClass.maxTotalWeight)
                return WeightClass.Middle;
            else
                return WeightClass.Heavy;
        }

        /// <summary>
        /// 부품 교체 시 무게 제한 내인지 검증
        /// </summary>
        /// <param name="currentTotalWeight">현재 총 무게</param>
        /// <param name="removingPartWeight">제거할 부품 무게</param>
        /// <param name="addingPartWeight">추가할 부품 무게</param>
        /// <param name="weightClass">현재 체급</param>
        /// <param name="config">게임 설정</param>
        /// <returns>교체 가능 여부</returns>
        public static bool CanSwapPart(
            float currentTotalWeight,
            float removingPartWeight,
            float addingPartWeight,
            WeightClass weightClass,
            GameConfig config)
        {
            float newWeight = currentTotalWeight - removingPartWeight + addingPartWeight;
            float maxWeight = config.GetPhysicsForClass(weightClass).maxTotalWeight;
            return newWeight <= maxWeight;
        }

        /// <summary>
        /// 체급별 Rigidbody 물리 값 적용
        /// </summary>
        public static void ApplyPhysicsToRigidbody(
            Rigidbody rb,
            float totalWeight,
            WeightClass weightClass,
            GameConfig config)
        {
            WeightClassPhysics physics = config.GetPhysicsForClass(weightClass);

            rb.mass = totalWeight;
            rb.linearDamping = physics.linearDrag;
            rb.angularDamping = physics.angularDrag;

            // 무게 중심을 약간 낮게 설정하여 안정성 확보
            rb.centerOfMass = new Vector3(0f, -0.3f, 0f);
        }

        /// <summary>
        /// 체급 이름 (한국어)
        /// </summary>
        public static string GetWeightClassName(WeightClass weightClass)
        {
            return weightClass switch
            {
                WeightClass.Light => "경량급",
                WeightClass.Middle => "중량급",
                WeightClass.Heavy => "초중량급",
                _ => "알 수 없음"
            };
        }

        /// <summary>
        /// 체급 이름 (영문)
        /// </summary>
        public static string GetWeightClassNameEn(WeightClass weightClass)
        {
            return weightClass switch
            {
                WeightClass.Light => "Light",
                WeightClass.Middle => "Middle",
                WeightClass.Heavy => "Heavy",
                _ => "Unknown"
            };
        }
    }
}
