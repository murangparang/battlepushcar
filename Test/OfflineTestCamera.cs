// ============================================================
// OfflineTestCamera.cs - 테스트용 카메라 컨트롤러
// 두 플레이어를 모두 볼 수 있도록 자동 추적
// ============================================================

using UnityEngine;

namespace BattleCarSumo.Test
{
    /// <summary>
    /// 두 차량의 중간점을 추적하는 탑다운 카메라.
    /// 거리에 따라 자동으로 줌인/아웃합니다.
    /// </summary>
    public class OfflineTestCamera : MonoBehaviour
    {
        [Header("=== 추적 대상 ===")]
        [SerializeField] private Transform _target1;
        [SerializeField] private Transform _target2;

        [Header("=== 카메라 설정 ===")]
        [SerializeField] private float _height = 25f;
        [SerializeField] private float _minHeight = 15f;
        [SerializeField] private float _maxHeight = 40f;
        [SerializeField] private float _smoothSpeed = 5f;
        [SerializeField] private float _lookAngle = 60f; // 탑다운 각도
        [SerializeField] private float _zoomPadding = 5f;

        private void LateUpdate()
        {
            if (_target1 == null && _target2 == null)
                return;

            Vector3 midpoint;
            float targetHeight;

            if (_target1 != null && _target2 != null)
            {
                midpoint = (_target1.position + _target2.position) / 2f;
                float dist = Vector3.Distance(_target1.position, _target2.position);
                targetHeight = Mathf.Clamp(_height + dist * 0.5f, _minHeight, _maxHeight);
            }
            else
            {
                Transform target = _target1 != null ? _target1 : _target2;
                midpoint = target.position;
                targetHeight = _height;
            }

            // 카메라 위치 계산 (약간 뒤에서 내려다보기)
            float angleRad = _lookAngle * Mathf.Deg2Rad;
            Vector3 offset = new Vector3(0f, targetHeight, -targetHeight / Mathf.Tan(angleRad));

            Vector3 targetPos = midpoint + offset;

            transform.position = Vector3.Lerp(transform.position, targetPos, Time.deltaTime * _smoothSpeed);
            transform.LookAt(midpoint);
        }
    }
}
