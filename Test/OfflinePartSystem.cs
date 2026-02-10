// ============================================================
// OfflinePartSystem.cs - 오프라인 테스트용 부품 시스템
// 네트워크 없이 부품 선택/장착/액션 처리
// ============================================================

using UnityEngine;
using System.Collections.Generic;
using BattleCarSumo.Data;
using BattleCarSumo.Audio;

namespace BattleCarSumo.Test
{
    /// <summary>
    /// 오프라인 부품 데이터. ScriptableObject 없이 코드에서 정의.
    /// </summary>
    [System.Serializable]
    public class OfflinePartData
    {
        public string partName;
        public PartSlot slot;
        public PartActionType actionType;
        public float actionForce;
        public float cooldown;
        public float weight;
        public Color partColor;
        public string description;
        public string modelPath; // Resources 내 FBX 모델 경로 (없으면 프리미티브 폴백)

        // 런타임 시각적 오브젝트
        [System.NonSerialized] public GameObject visualObject;

        /// <summary>
        /// 독립적인 visualObject 추적을 위해 복제
        /// </summary>
        public OfflinePartData Clone()
        {
            return new OfflinePartData
            {
                partName = partName,
                slot = slot,
                actionType = actionType,
                actionForce = actionForce,
                cooldown = cooldown,
                weight = weight,
                partColor = partColor,
                description = description,
                modelPath = modelPath,
                visualObject = null
            };
        }
    }

    /// <summary>
    /// 오프라인 부품 시스템. 부품 등록, 장착, 시각적 표현, 액션 실행 관리.
    /// </summary>
    public class OfflinePartSystem
    {
        // 슬롯별 사용 가능한 부품 목록
        public Dictionary<PartSlot, List<OfflinePartData>> AvailableParts { get; private set; }

        // 플레이어별 장착된 부품
        public Dictionary<PartSlot, OfflinePartData> P1EquippedParts { get; private set; }
        public Dictionary<PartSlot, OfflinePartData> P2EquippedParts { get; private set; }

        // 슬롯별 선택 인덱스 (UI 용)
        public Dictionary<PartSlot, int> P1SelectedIndex { get; private set; }

        // 쿨다운 추적
        private Dictionary<PartSlot, float> _p1LastActionTime;
        private Dictionary<PartSlot, float> _p2LastActionTime;

        public OfflinePartSystem()
        {
            AvailableParts = new Dictionary<PartSlot, List<OfflinePartData>>();
            P1EquippedParts = new Dictionary<PartSlot, OfflinePartData>();
            P2EquippedParts = new Dictionary<PartSlot, OfflinePartData>();
            P1SelectedIndex = new Dictionary<PartSlot, int>();
            _p1LastActionTime = new Dictionary<PartSlot, float>();
            _p2LastActionTime = new Dictionary<PartSlot, float>();

            RegisterAllParts();

            // 기본 장착 (각 슬롯의 첫 번째 부품)
            // P2는 Clone()으로 독립적인 visual 추적
            foreach (var kvp in AvailableParts)
            {
                if (kvp.Value.Count > 0)
                {
                    P1EquippedParts[kvp.Key] = kvp.Value[0];
                    P2EquippedParts[kvp.Key] = kvp.Value[0].Clone();
                    P1SelectedIndex[kvp.Key] = 0;
                }
                _p1LastActionTime[kvp.Key] = -999f;
                _p2LastActionTime[kvp.Key] = -999f;
            }
        }

        private void RegisterAllParts()
        {
            // ========== 앞범퍼 (Front) ==========
            var frontParts = new List<OfflinePartData>
            {
                new OfflinePartData
                {
                    partName = "펀치 범퍼",
                    slot = PartSlot.Front,
                    actionType = PartActionType.Punch,
                    actionForce = 18f,
                    cooldown = 2f,
                    weight = 80f,
                    partColor = new Color(1f, 0.3f, 0.1f),
                    description = "전방 3m 내 상대를 강하게 밀어냄",
                    modelPath = "Models/Parts/part_punch_bumper"
                },
                new OfflinePartData
                {
                    partName = "방패 범퍼",
                    slot = PartSlot.Front,
                    actionType = PartActionType.Shield,
                    actionForce = 0f,
                    cooldown = 5f,
                    weight = 120f,
                    partColor = new Color(0.2f, 0.5f, 0.9f),
                    description = "2초간 질량 1.5배 증가, 밀림 감소",
                    modelPath = "Models/Parts/part_shield_bumper"
                },
                new OfflinePartData
                {
                    partName = "돌진 범퍼",
                    slot = PartSlot.Front,
                    actionType = PartActionType.Punch,
                    actionForce = 25f,
                    cooldown = 4f,
                    weight = 100f,
                    partColor = new Color(0.9f, 0.1f, 0.1f),
                    description = "전방 4m 내 상대를 매우 강하게 밀어냄 (긴 쿨다운)",
                    modelPath = "Models/Parts/part_charge_bumper"
                },
                new OfflinePartData
                {
                    partName = "전기 범퍼",
                    slot = PartSlot.Front,
                    actionType = PartActionType.Punch,
                    actionForce = 12f,
                    cooldown = 1.5f,
                    weight = 60f,
                    partColor = new Color(0.3f, 0.9f, 1f),
                    description = "빠른 쿨다운의 약한 전기 충격",
                    modelPath = "Models/Parts/part_electric_bumper"
                },
            };

            // ========== 루프탑 (Rooftop) ==========
            var rooftopParts = new List<OfflinePartData>
            {
                new OfflinePartData
                {
                    partName = "리프트 장치",
                    slot = PartSlot.Rooftop,
                    actionType = PartActionType.Lift,
                    actionForce = 14f,
                    cooldown = 3f,
                    weight = 70f,
                    partColor = new Color(0.1f, 0.9f, 0.4f),
                    description = "2.5m 내 상대를 위로 들어올림",
                    modelPath = "Models/Parts/part_lift_device"
                },
                new OfflinePartData
                {
                    partName = "슬램 해머",
                    slot = PartSlot.Rooftop,
                    actionType = PartActionType.Slam,
                    actionForce = 20f,
                    cooldown = 3.5f,
                    weight = 110f,
                    partColor = new Color(0.7f, 0.2f, 0.8f),
                    description = "2m 내 상대를 내리찍어 감속시킴",
                    modelPath = "Models/Parts/part_slam_hammer"
                },
                new OfflinePartData
                {
                    partName = "자기장 발생기",
                    slot = PartSlot.Rooftop,
                    actionType = PartActionType.Lift,
                    actionForce = 10f,
                    cooldown = 2f,
                    weight = 90f,
                    partColor = new Color(0.9f, 0.8f, 0.1f),
                    description = "넓은 범위(4m)로 상대를 약하게 들어올림",
                    modelPath = "Models/Parts/part_magnet"
                },
                new OfflinePartData
                {
                    partName = "EMP 장치",
                    slot = PartSlot.Rooftop,
                    actionType = PartActionType.Slam,
                    actionForce = 8f,
                    cooldown = 5f,
                    weight = 50f,
                    partColor = new Color(0.1f, 0.1f, 0.9f),
                    description = "3m 내 상대의 마찰을 크게 증가 (강한 감속)",
                    modelPath = "Models/Parts/part_emp"
                },
            };

            // ========== 뒷범퍼 (Rear) ==========
            var rearParts = new List<OfflinePartData>
            {
                new OfflinePartData
                {
                    partName = "니트로 부스터",
                    slot = PartSlot.Rear,
                    actionType = PartActionType.Boost,
                    actionForce = 22f,
                    cooldown = 4f,
                    weight = 80f,
                    partColor = new Color(1f, 0.5f, 0f),
                    description = "전방으로 강력한 가속 돌진",
                    modelPath = "Models/Parts/part_nitro_booster"
                },
                new OfflinePartData
                {
                    partName = "스파이크 투하기",
                    slot = PartSlot.Rear,
                    actionType = PartActionType.SpikeDrop,
                    actionForce = 10f,
                    cooldown = 3f,
                    weight = 60f,
                    partColor = new Color(0.5f, 0.5f, 0.5f),
                    description = "후방 상대에게 넉백 + 감속",
                    modelPath = "Models/Parts/part_spike_dropper"
                },
                new OfflinePartData
                {
                    partName = "터보 차저",
                    slot = PartSlot.Rear,
                    actionType = PartActionType.Boost,
                    actionForce = 15f,
                    cooldown = 2.5f,
                    weight = 50f,
                    partColor = new Color(0.2f, 0.8f, 0.2f),
                    description = "짧은 쿨다운의 가벼운 부스트",
                    modelPath = "Models/Parts/part_turbo_charger"
                },
                new OfflinePartData
                {
                    partName = "로켓 엔진",
                    slot = PartSlot.Rear,
                    actionType = PartActionType.Boost,
                    actionForce = 35f,
                    cooldown = 7f,
                    weight = 150f,
                    partColor = new Color(0.9f, 0.2f, 0.5f),
                    description = "초강력 돌진! 무겁고 긴 쿨다운",
                    modelPath = "Models/Parts/part_rocket_engine"
                },
            };

            AvailableParts[PartSlot.Front] = frontParts;
            AvailableParts[PartSlot.Rooftop] = rooftopParts;
            AvailableParts[PartSlot.Rear] = rearParts;
        }

        /// <summary>
        /// 슬롯의 다음/이전 부품으로 변경
        /// </summary>
        public void CyclePart(PartSlot slot, int direction)
        {
            if (!AvailableParts.ContainsKey(slot)) return;
            var parts = AvailableParts[slot];
            if (parts.Count == 0) return;

            int idx = P1SelectedIndex.ContainsKey(slot) ? P1SelectedIndex[slot] : 0;
            idx = (idx + direction + parts.Count) % parts.Count;
            P1SelectedIndex[slot] = idx;
        }

        /// <summary>
        /// 현재 선택된 부품을 장착
        /// </summary>
        public void EquipSelected(PartSlot slot, OfflineVehicleController vehicle)
        {
            if (!AvailableParts.ContainsKey(slot)) return;
            var parts = AvailableParts[slot];
            int idx = P1SelectedIndex.ContainsKey(slot) ? P1SelectedIndex[slot] : 0;
            if (idx < 0 || idx >= parts.Count) return;

            OfflinePartData part = parts[idx];
            P1EquippedParts[slot] = part;

            // 시각적 업데이트
            UpdatePartVisual(slot, vehicle, part);
        }

        /// <summary>
        /// 부품 액션 실행
        /// </summary>
        public bool TryExecuteAction(PartSlot slot, OfflineVehicleController owner,
                                       OfflineVehicleController target, bool isP1,
                                       ref Vector3 ownerVelocity, ref Vector3 targetVelocity)
        {
            var equipped = isP1 ? P1EquippedParts : P2EquippedParts;
            var lastAction = isP1 ? _p1LastActionTime : _p2LastActionTime;

            if (!equipped.ContainsKey(slot)) return false;
            OfflinePartData part = equipped[slot];

            // 쿨다운 체크
            if (!lastAction.ContainsKey(slot)) lastAction[slot] = -999f;
            if (Time.time - lastAction[slot] < part.cooldown) return false;
            lastAction[slot] = Time.time;

            switch (part.actionType)
            {
                case PartActionType.Punch:
                    ExecutePunch(owner, target, part, ref targetVelocity);
                    break;
                case PartActionType.Shield:
                    ExecuteShield(owner, part);
                    break;
                case PartActionType.Lift:
                    ExecuteLift(owner, target, part);
                    break;
                case PartActionType.Slam:
                    ExecuteSlam(owner, target, part);
                    break;
                case PartActionType.Boost:
                    ExecuteBoost(owner, part, ref ownerVelocity);
                    break;
                case PartActionType.SpikeDrop:
                    ExecuteSpikeDrop(owner, target, part, ref targetVelocity);
                    break;
            }

            return true;
        }

        private void ExecutePunch(OfflineVehicleController owner, OfflineVehicleController target,
                                    OfflinePartData part, ref Vector3 targetVelocity)
        {
            if (target == null) return;
            float range = part.actionForce > 20f ? 4f : 3f;
            float dist = Vector3.Distance(owner.transform.position, target.transform.position);
            if (dist > range) return;

            Vector3 dir = (target.transform.position - owner.transform.position).normalized;
            targetVelocity = dir * part.actionForce;

            if (AudioManager.Instance != null) AudioManager.Instance.PlayPunchHit();
        }

        private void ExecuteShield(OfflineVehicleController owner, OfflinePartData part)
        {
            Rigidbody rb = owner.GetComponent<Rigidbody>();
            if (rb != null)
            {
                float origMass = rb.mass;
                rb.mass *= 1.5f;
                // 2초 후 복원은 코루틴으로 처리해야 하지만 간단히 처리
                owner.StartCoroutine(RestoreMassCoroutine(rb, origMass, 2f));
            }
        }

        private System.Collections.IEnumerator RestoreMassCoroutine(Rigidbody rb, float origMass, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (rb != null) rb.mass = origMass;
        }

        private void ExecuteLift(OfflineVehicleController owner, OfflineVehicleController target,
                                   OfflinePartData part)
        {
            if (target == null) return;
            float range = part.actionForce > 12f ? 2.5f : 4f;
            float dist = Vector3.Distance(owner.transform.position, target.transform.position);
            if (dist > range) return;

            Rigidbody rb = target.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, part.actionForce, rb.linearVelocity.z);
            }

            if (AudioManager.Instance != null) AudioManager.Instance.PlayLift();
        }

        private void ExecuteSlam(OfflineVehicleController owner, OfflineVehicleController target,
                                   OfflinePartData part)
        {
            if (target == null) return;
            float range = part.actionType == PartActionType.Slam ? 3f : 2f;
            float dist = Vector3.Distance(owner.transform.position, target.transform.position);
            if (dist > range) return;

            Rigidbody rb = target.GetComponent<Rigidbody>();
            if (rb != null)
            {
                rb.linearVelocity = new Vector3(rb.linearVelocity.x, -part.actionForce * 0.5f, rb.linearVelocity.z);
                // 마찰 증가 효과
                float origDrag = rb.linearDamping;
                rb.linearDamping = origDrag * 2f;
                owner.StartCoroutine(RestoreDragCoroutine(rb, origDrag, 2f));
            }
        }

        private System.Collections.IEnumerator RestoreDragCoroutine(Rigidbody rb, float origDrag, float delay)
        {
            yield return new WaitForSeconds(delay);
            if (rb != null) rb.linearDamping = origDrag;
        }

        private void ExecuteBoost(OfflineVehicleController owner, OfflinePartData part,
                                    ref Vector3 ownerVelocity)
        {
            Vector3 forward = owner.transform.forward;
            forward.y = 0f;
            forward.Normalize();
            ownerVelocity += forward * part.actionForce;

            if (AudioManager.Instance != null) AudioManager.Instance.PlayBoost();
        }

        private void ExecuteSpikeDrop(OfflineVehicleController owner, OfflineVehicleController target,
                                        OfflinePartData part, ref Vector3 targetVelocity)
        {
            if (target == null) return;
            // 후방 상대 체크
            Vector3 toTarget = (target.transform.position - owner.transform.position).normalized;
            float dot = Vector3.Dot(-owner.transform.forward, toTarget);
            if (dot < 0.3f) return; // 뒤에 있지 않으면 효과 없음

            float dist = Vector3.Distance(owner.transform.position, target.transform.position);
            if (dist > 5f) return;

            targetVelocity += -owner.transform.forward * part.actionForce;

            if (AudioManager.Instance != null) AudioManager.Instance.PlayPunchHit();
        }

        /// <summary>
        /// 부품 시각적 오브젝트 업데이트 (차량에 부착)
        /// FBX 모델의 AP (Attachment Point) 위치를 사용하고, 없으면 폴백 위치 사용
        /// </summary>
        public void UpdatePartVisual(PartSlot slot, OfflineVehicleController vehicle, OfflinePartData part)
        {
            if (vehicle == null || part == null) return;

            // 기존 시각적 오브젝트 제거
            if (part.visualObject != null)
            {
                Object.Destroy(part.visualObject);
                part.visualObject = null;
            }

            // 슬롯별 폴백 위치 & 프리미티브
            Vector3 localPos;
            Vector3 fallbackScale;
            PrimitiveType fallbackShape;
            string apName;

            switch (slot)
            {
                case PartSlot.Front:
                    localPos = new Vector3(0f, 0.15f, 1.1f);
                    fallbackScale = new Vector3(1.2f, 0.35f, 0.4f);
                    fallbackShape = PrimitiveType.Cube;
                    apName = "AP_Front";
                    break;
                case PartSlot.Rooftop:
                    localPos = new Vector3(0f, 0.7f, 0f);
                    fallbackScale = new Vector3(0.6f, 0.3f, 0.6f);
                    fallbackShape = PrimitiveType.Cylinder;
                    apName = "AP_Rooftop";
                    break;
                case PartSlot.Rear:
                    localPos = new Vector3(0f, 0.2f, -1.1f);
                    fallbackScale = new Vector3(0.8f, 0.3f, 0.5f);
                    fallbackShape = PrimitiveType.Cube;
                    apName = "AP_Rear";
                    break;
                default:
                    return;
            }

            // ★ FBX 차량 모델의 Attachment Point 위치 찾기
            Transform vehicleModel = vehicle.transform.Find("_VehicleModel");
            if (vehicleModel != null)
            {
                Transform ap = FindChildRecursive(vehicleModel, apName);
                if (ap != null)
                {
                    // AP의 월드 위치 → 차량 로컬 좌표로 변환
                    localPos = vehicle.transform.InverseTransformPoint(ap.position);
                }
            }

            // ★ FBX 부품 모델 로드 시도 → 실패 시 프리미티브 폴백
            GameObject partObj = null;
            bool usedModel = false;

            if (!string.IsNullOrEmpty(part.modelPath))
            {
                GameObject prefab = Resources.Load<GameObject>(part.modelPath);
                if (prefab != null)
                {
                    partObj = Object.Instantiate(prefab);
                    partObj.name = $"Part_{slot}_{part.partName}";
                    usedModel = true;
                }
            }

            if (partObj == null)
            {
                // ★ 폴백: 슬롯별 입체적 부품 모양 빌드
                partObj = BuildProceduralPart(slot, part.actionType);
                partObj.name = $"Part_{slot}_{part.partName}";
            }

            // ★ 차량 루트에 부착 (모델 교체에도 파괴되지 않음)
            partObj.transform.SetParent(vehicle.transform);
            partObj.transform.localPosition = localPos;
            partObj.transform.localRotation = Quaternion.identity;
            if (usedModel)
                partObj.transform.localScale = Vector3.one; // FBX는 이미 규격 크기

            // 모든 콜라이더 제거 (충돌 방지)
            foreach (Collider col in partObj.GetComponentsInChildren<Collider>())
                Object.Destroy(col);

            // ★ 색상 적용 (URP 호환)
            ApplyPartMaterial(partObj, part.partColor);

            part.visualObject = partObj;
        }

        /// <summary>
        /// URP 호환 머티리얼 색상 적용
        /// </summary>
        private void ApplyPartMaterial(GameObject partObj, Color color)
        {
            foreach (Renderer renderer in partObj.GetComponentsInChildren<Renderer>())
            {
                if (renderer == null) continue;

                // sharedMaterial이 null일 수 있음 (FBX 임포트 문제)
                Material srcMat = renderer.sharedMaterial;
                Material mat = null;

                if (srcMat != null)
                {
                    mat = new Material(srcMat);
                }
                else
                {
                    // 폴백: 씬에서 기존 머티리얼 찾아서 복사
                    foreach (Renderer sr in Object.FindObjectsByType<Renderer>(FindObjectsSortMode.None))
                    {
                        if (sr != null && sr.sharedMaterial != null)
                        {
                            mat = new Material(sr.sharedMaterial);
                            break;
                        }
                    }
                    // 그래도 없으면 URP Lit 시도
                    if (mat == null)
                    {
                        string[] shaderNames = { "Universal Render Pipeline/Lit", "Standard", "Sprites/Default" };
                        foreach (string sn in shaderNames)
                        {
                            Shader s = Shader.Find(sn);
                            if (s != null) { mat = new Material(s); break; }
                        }
                    }
                }
                if (mat == null) continue; // 머티리얼 생성 실패 시 스킵

                mat.color = color;
                if (mat.HasProperty("_BaseColor"))
                    mat.SetColor("_BaseColor", color);
                if (mat.HasProperty("_Smoothness"))
                    mat.SetFloat("_Smoothness", 0.6f);
                if (mat.HasProperty("_EmissionColor"))
                {
                    mat.EnableKeyword("_EMISSION");
                    mat.SetColor("_EmissionColor", color * 0.3f);
                }
                renderer.material = mat;
            }
        }

        /// <summary>
        /// Transform 재귀 검색
        /// </summary>
        private static Transform FindChildRecursive(Transform parent, string name)
        {
            foreach (Transform child in parent)
            {
                if (child.name == name) return child;
                Transform found = FindChildRecursive(child, name);
                if (found != null) return found;
            }
            return null;
        }

        /// <summary>
        /// 슬롯/타입별 입체적 부품 모양 프로시저럴 빌드
        /// </summary>
        private static GameObject BuildProceduralPart(PartSlot slot, PartActionType actionType)
        {
            GameObject root = new GameObject("PartRoot");

            switch (slot)
            {
                case PartSlot.Front:
                    BuildFrontPart(root, actionType);
                    break;
                case PartSlot.Rooftop:
                    BuildRooftopPart(root, actionType);
                    break;
                case PartSlot.Rear:
                    BuildRearPart(root, actionType);
                    break;
            }

            return root;
        }

        private static GameObject MakePrim(PrimitiveType type, Transform parent, string name,
            Vector3 pos, Vector3 scale, Quaternion? rot = null)
        {
            GameObject obj = GameObject.CreatePrimitive(type);
            obj.name = name;
            obj.transform.SetParent(parent);
            obj.transform.localPosition = pos;
            obj.transform.localScale = scale;
            obj.transform.localRotation = rot ?? Quaternion.identity;
            return obj;
        }

        private static void BuildFrontPart(GameObject root, PartActionType type)
        {
            Transform t = root.transform;
            if (type == PartActionType.Shield)
            {
                // 방패: 넓은 판
                MakePrim(PrimitiveType.Cube, t, "ShieldPlate",
                    Vector3.zero, new Vector3(1.2f, 0.35f, 0.08f));
                MakePrim(PrimitiveType.Cube, t, "ShieldRim",
                    new Vector3(0f, 0f, -0.06f), new Vector3(1.3f, 0.4f, 0.03f));
            }
            else if (type == PartActionType.Punch)
            {
                // 펀치/돌진: 뾰족한 범퍼
                MakePrim(PrimitiveType.Cube, t, "BumperBase",
                    Vector3.zero, new Vector3(1.0f, 0.25f, 0.2f));
                MakePrim(PrimitiveType.Cube, t, "BumperTip",
                    new Vector3(0f, 0f, 0.15f), new Vector3(0.5f, 0.2f, 0.15f));
                MakePrim(PrimitiveType.Sphere, t, "PunchHead",
                    new Vector3(0f, 0f, 0.25f), new Vector3(0.3f, 0.25f, 0.2f));
            }
            else
            {
                // 기타 (전기 등): 코일 형태
                MakePrim(PrimitiveType.Cube, t, "CoilBase",
                    Vector3.zero, new Vector3(0.9f, 0.2f, 0.15f));
                MakePrim(PrimitiveType.Cylinder, t, "CoilL",
                    new Vector3(-0.3f, 0.1f, 0f), new Vector3(0.15f, 0.1f, 0.15f));
                MakePrim(PrimitiveType.Cylinder, t, "CoilR",
                    new Vector3(0.3f, 0.1f, 0f), new Vector3(0.15f, 0.1f, 0.15f));
            }
        }

        private static void BuildRooftopPart(GameObject root, PartActionType type)
        {
            Transform t = root.transform;
            if (type == PartActionType.Lift)
            {
                // 리프트: 유압 실린더
                MakePrim(PrimitiveType.Cube, t, "LiftBase",
                    Vector3.zero, new Vector3(0.4f, 0.08f, 0.4f));
                MakePrim(PrimitiveType.Cylinder, t, "LiftArm",
                    new Vector3(0f, 0.15f, 0f), new Vector3(0.1f, 0.12f, 0.1f));
                MakePrim(PrimitiveType.Cube, t, "LiftFork",
                    new Vector3(0f, 0.28f, 0.1f), new Vector3(0.3f, 0.04f, 0.2f));
            }
            else if (type == PartActionType.Slam)
            {
                // 슬램 해머
                MakePrim(PrimitiveType.Cylinder, t, "HammerPole",
                    new Vector3(0f, 0.12f, 0f), new Vector3(0.08f, 0.1f, 0.08f));
                MakePrim(PrimitiveType.Cube, t, "HammerHead",
                    new Vector3(0f, 0.26f, 0f), new Vector3(0.35f, 0.12f, 0.2f));
            }
            else
            {
                // 자기장/EMP: 디스크
                MakePrim(PrimitiveType.Cylinder, t, "Disk",
                    Vector3.zero, new Vector3(0.45f, 0.05f, 0.45f));
                MakePrim(PrimitiveType.Sphere, t, "Core",
                    new Vector3(0f, 0.08f, 0f), new Vector3(0.18f, 0.18f, 0.18f));
            }
        }

        private static void BuildRearPart(GameObject root, PartActionType type)
        {
            Transform t = root.transform;
            if (type == PartActionType.Boost)
            {
                // 부스터/터보/로켓: 배기관
                MakePrim(PrimitiveType.Cylinder, t, "ExhaustL",
                    new Vector3(-0.15f, 0f, 0f), new Vector3(0.15f, 0.15f, 0.15f),
                    Quaternion.Euler(90f, 0f, 0f));
                MakePrim(PrimitiveType.Cylinder, t, "ExhaustR",
                    new Vector3(0.15f, 0f, 0f), new Vector3(0.15f, 0.15f, 0.15f),
                    Quaternion.Euler(90f, 0f, 0f));
                MakePrim(PrimitiveType.Cube, t, "TankBox",
                    new Vector3(0f, 0.08f, -0.12f), new Vector3(0.5f, 0.15f, 0.12f));
            }
            else if (type == PartActionType.SpikeDrop)
            {
                // 스파이크 투하기
                MakePrim(PrimitiveType.Cube, t, "DropperBox",
                    Vector3.zero, new Vector3(0.5f, 0.15f, 0.3f));
                for (int i = 0; i < 3; i++)
                {
                    MakePrim(PrimitiveType.Sphere, t, $"Spike_{i}",
                        new Vector3(-0.15f + i * 0.15f, -0.12f, 0f), new Vector3(0.08f, 0.08f, 0.08f));
                }
            }
            else
            {
                // 기타
                MakePrim(PrimitiveType.Cube, t, "RearDevice",
                    Vector3.zero, new Vector3(0.6f, 0.2f, 0.25f));
                MakePrim(PrimitiveType.Cylinder, t, "Nozzle",
                    new Vector3(0f, 0f, -0.15f), new Vector3(0.2f, 0.1f, 0.2f),
                    Quaternion.Euler(90f, 0f, 0f));
            }
        }

        /// <summary>
        /// 모든 장착된 부품의 시각적 오브젝트 생성
        /// </summary>
        public void RefreshAllVisuals(OfflineVehicleController p1, OfflineVehicleController p2)
        {
            try
            {
                // 기존 부품 오브젝트 모두 제거
                ClearAllVisuals();

                foreach (var kvp in P1EquippedParts)
                {
                    UpdatePartVisual(kvp.Key, p1, kvp.Value);
                }
                foreach (var kvp in P2EquippedParts)
                {
                    UpdatePartVisual(kvp.Key, p2, kvp.Value);
                }
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[PARTS] RefreshAllVisuals 실패: {e.Message}");
            }
        }

        public void ClearAllVisuals()
        {
            foreach (var kvp in P1EquippedParts)
            {
                if (kvp.Value.visualObject != null)
                {
                    Object.Destroy(kvp.Value.visualObject);
                    kvp.Value.visualObject = null;
                }
            }
            foreach (var kvp in P2EquippedParts)
            {
                if (kvp.Value.visualObject != null)
                {
                    Object.Destroy(kvp.Value.visualObject);
                    kvp.Value.visualObject = null;
                }
            }
        }

        /// <summary>
        /// 쿨다운 남은 시간 (0이면 사용 가능)
        /// </summary>
        public float GetCooldownRemaining(PartSlot slot, bool isP1)
        {
            var lastAction = isP1 ? _p1LastActionTime : _p2LastActionTime;
            var equipped = isP1 ? P1EquippedParts : P2EquippedParts;

            if (!lastAction.ContainsKey(slot) || !equipped.ContainsKey(slot)) return 0f;
            float remaining = equipped[slot].cooldown - (Time.time - lastAction[slot]);
            return Mathf.Max(0f, remaining);
        }
    }
}
