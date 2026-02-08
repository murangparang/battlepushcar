// ============================================================
// RelayManager.cs - UGS Relay + Fish-Net 연결 관리
// Battle Car Sumo - 1v1 Server Authoritative Physics Battle
//
// ※ 주의: Fish-Net은 기본적으로 Tugboat(UDP) 트랜스포트를 사용합니다.
//   UGS Relay를 사용하려면 FishyUnityTransport 어댑터가 필요합니다.
//   (https://github.com/ooonush/FishyUnityTransport)
//   이 어댑터는 Unity Transport Package를 Fish-Net에서 사용할 수 있게 해줍니다.
// ============================================================

using UnityEngine;
using System;
using System.Threading.Tasks;
using Unity.Services.Core;
using Unity.Services.Authentication;
using Unity.Services.Relay;
using Unity.Services.Relay.Models;
using Unity.Networking.Transport.Relay;
using System.Linq;
using FishNet;

namespace BattleCarSumo.Network
{
    /// <summary>
    /// UGS Relay를 통한 P2P 연결 관리자 (싱글톤).
    /// 익명 인증, Relay 할당, Join Code 생성/참여, Fish-Net 시작을 담당합니다.
    ///
    /// 필수 패키지:
    /// - com.unity.services.relay
    /// - com.unity.services.authentication
    /// - com.unity.transport
    /// - FishyUnityTransport (Fish-Net용 Unity Transport 어댑터)
    /// </summary>
    public class RelayManager : MonoBehaviour
    {
        #region Singleton

        private static RelayManager _instance;
        public static RelayManager Instance
        {
            get
            {
                if (_instance == null)
                {
                    _instance = FindFirstObjectByType<RelayManager>();
                    if (_instance == null)
                    {
                        GameObject go = new GameObject("RelayManager");
                        _instance = go.AddComponent<RelayManager>();
                    }
                }
                return _instance;
            }
        }

        private void Awake()
        {
            if (_instance != null && _instance != this)
            {
                Destroy(gameObject);
                return;
            }
            _instance = this;
            DontDestroyOnLoad(gameObject);
        }

        #endregion

        #region Events

        /// <summary>Relay 생성 성공 시 발생 (joinCode 전달)</summary>
        public event Action<string> OnRelayCreated;

        /// <summary>Relay 참가 성공 시 발생</summary>
        public event Action OnRelayJoined;

        /// <summary>연결 실패 시 발생 (에러 메시지 전달)</summary>
        public event Action<string> OnConnectionFailed;

        #endregion

        #region Fields

        private string _currentJoinCode;
        private bool _isInitialized;
        private Allocation _hostAllocation;
        private JoinAllocation _clientAllocation;

        [SerializeField]
        private int _maxPlayers = 2; // 1v1 이므로 2명

        #endregion

        #region UGS 초기화 및 인증

        /// <summary>
        /// Unity Gaming Services 초기화 및 익명 로그인.
        /// Relay 사용 전 반드시 호출해야 합니다.
        /// </summary>
        public async Task<bool> InitializeUGS()
        {
            try
            {
                if (!_isInitialized)
                {
                    var options = new InitializationOptions();

#if UNITY_EDITOR
                    // 에디터에서 멀티 인스턴스 테스트 시 프로필 구분
                    options.SetProfile($"Player_{UnityEngine.Random.Range(0, 10000)}");
#endif

                    await UnityServices.InitializeAsync(options);
                }

                if (!AuthenticationService.Instance.IsSignedIn)
                {
                    await AuthenticationService.Instance.SignInAnonymouslyAsync();
                }

                _isInitialized = true;
                Debug.Log($"[RelayManager] UGS 초기화 완료. Player ID: {AuthenticationService.Instance.PlayerId}");
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayManager] UGS 초기화 실패: {ex.Message}");
                OnConnectionFailed?.Invoke($"UGS 초기화 실패: {ex.Message}");
                return false;
            }
        }

        #endregion

        #region Relay 호스트 (방 생성)

        /// <summary>
        /// Relay 할당을 생성하고 Join Code를 반환합니다.
        /// 호스트가 이 메서드를 호출하여 상대방이 참가할 수 있는 코드를 생성합니다.
        /// </summary>
        /// <returns>Join Code 문자열, 실패 시 null</returns>
        public async Task<string> CreateRelay()
        {
            if (!_isInitialized)
            {
                Debug.LogError("[RelayManager] UGS가 초기화되지 않았습니다. InitializeUGS()를 먼저 호출하세요.");
                OnConnectionFailed?.Invoke("UGS 미초기화");
                return null;
            }

            try
            {
                // Relay 할당 요청 (maxPlayers - 1: 호스트 제외)
                _hostAllocation = await RelayService.Instance.CreateAllocationAsync(_maxPlayers - 1);

                // Join Code 발급
                _currentJoinCode = await RelayService.Instance.GetJoinCodeAsync(_hostAllocation.AllocationId);

                Debug.Log($"[RelayManager] Relay 생성 완료. Join Code: {_currentJoinCode}");

                // Transport에 Relay 데이터 설정
                ConfigureTransportAsHost(_hostAllocation);

                OnRelayCreated?.Invoke(_currentJoinCode);
                return _currentJoinCode;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayManager] Relay 생성 실패: {ex.Message}");
                OnConnectionFailed?.Invoke($"Relay 생성 실패: {ex.Message}");
                return null;
            }
        }

        /// <summary>
        /// FishyUnityTransport에 호스트 Relay 데이터를 설정합니다.
        /// </summary>
        private void ConfigureTransportAsHost(Allocation allocation)
        {
            // RelayServerData 생성 (UDP 프로토콜 사용)
            var dtlsEndpoint = allocation.ServerEndpoints.First(e => e.ConnectionType == "dtls");
            var relayServerData = new RelayServerData(
                dtlsEndpoint.Host,
                (ushort)dtlsEndpoint.Port,
                allocation.AllocationIdBytes,
                allocation.ConnectionData,
                allocation.ConnectionData,
                allocation.Key,
                true
            );

            // FishyUnityTransport의 SetRelayServerData를 호출
            // ※ FishyUnityTransport는 별도 설치 필요 (GitHub에서 가져오기)
            var transport = InstanceFinder.NetworkManager.GetComponent<MonoBehaviour>();

            // 리플렉션 또는 인터페이스를 통해 SetRelayServerData 호출
            // 실제 구현 시 FishyUnityTransport 타입을 직접 참조:
            // var fishyTransport = InstanceFinder.NetworkManager.GetComponent<FishyUnityTransport>();
            // fishyTransport.SetRelayServerData(relayServerData);

            // ─── 대안: 직접 Unity Transport Package 사용 ───
            // Unity.Networking.Transport.NetworkDriver를 통해 Relay 연결도 가능
            SetRelayServerDataViaReflection(relayServerData);

            Debug.Log("[RelayManager] 호스트 Transport 설정 완료");
        }

        #endregion

        #region Relay 클라이언트 (방 참가)

        /// <summary>
        /// Join Code를 사용하여 호스트의 Relay 세션에 참가합니다.
        /// </summary>
        /// <param name="joinCode">호스트로부터 받은 Join Code</param>
        /// <returns>성공 여부</returns>
        public async Task<bool> JoinRelay(string joinCode)
        {
            if (!_isInitialized)
            {
                Debug.LogError("[RelayManager] UGS가 초기화되지 않았습니다.");
                OnConnectionFailed?.Invoke("UGS 미초기화");
                return false;
            }

            if (string.IsNullOrEmpty(joinCode))
            {
                Debug.LogError("[RelayManager] Join Code가 비어있습니다.");
                OnConnectionFailed?.Invoke("잘못된 Join Code");
                return false;
            }

            try
            {
                _clientAllocation = await RelayService.Instance.JoinAllocationAsync(joinCode);

                Debug.Log($"[RelayManager] Relay 참가 성공. Allocation ID: {_clientAllocation.AllocationId}");

                ConfigureTransportAsClient(_clientAllocation);

                OnRelayJoined?.Invoke();
                return true;
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayManager] Relay 참가 실패: {ex.Message}");
                OnConnectionFailed?.Invoke($"Relay 참가 실패: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// FishyUnityTransport에 클라이언트 Relay 데이터를 설정합니다.
        /// </summary>
        private void ConfigureTransportAsClient(JoinAllocation joinAllocation)
        {
            var dtlsEndpoint = joinAllocation.ServerEndpoints.First(e => e.ConnectionType == "dtls");
            var relayServerData = new RelayServerData(
                dtlsEndpoint.Host,
                (ushort)dtlsEndpoint.Port,
                joinAllocation.AllocationIdBytes,
                joinAllocation.ConnectionData,
                joinAllocation.HostConnectionData,
                joinAllocation.Key,
                true
            );
            SetRelayServerDataViaReflection(relayServerData);

            Debug.Log("[RelayManager] 클라이언트 Transport 설정 완료");
        }

        #endregion

        #region Transport 설정 헬퍼

        /// <summary>
        /// Fish-Net Transport에 RelayServerData를 설정합니다.
        /// FishyUnityTransport 패키지 설치 후 이 메서드를 직접 참조로 교체하세요.
        ///
        /// 교체 예시:
        /// var transport = InstanceFinder.NetworkManager.GetComponent&lt;FishyUnityTransport&gt;();
        /// transport.SetRelayServerData(relayServerData);
        /// </summary>
        private void SetRelayServerDataViaReflection(RelayServerData relayServerData)
        {
            if (InstanceFinder.NetworkManager == null)
            {
                Debug.LogError("[RelayManager] NetworkManager를 찾을 수 없습니다.");
                return;
            }

            // FishyUnityTransport를 사용할 경우 아래 코드로 교체:
            // ─────────────────────────────────────────────
            // using FishyUnityTransport;
            //
            // var transport = InstanceFinder.NetworkManager.GetComponent<FishyUnityTransport>();
            // if (transport != null)
            // {
            //     transport.SetRelayServerData(relayServerData);
            // }
            // ─────────────────────────────────────────────

            // 리플렉션 방식 (FishyUnityTransport 타입 참조 없이)
            var components = InstanceFinder.NetworkManager.GetComponents<MonoBehaviour>();
            foreach (var component in components)
            {
                var method = component.GetType().GetMethod("SetRelayServerData");
                if (method != null)
                {
                    method.Invoke(component, new object[] { relayServerData });
                    Debug.Log($"[RelayManager] {component.GetType().Name}에 RelayServerData 설정 완료");
                    return;
                }
            }

            Debug.LogWarning("[RelayManager] SetRelayServerData를 지원하는 Transport를 찾지 못했습니다. " +
                             "FishyUnityTransport를 설치하고 NetworkManager에 추가하세요.");
        }

        #endregion

        #region Fish-Net 시작/종료

        /// <summary>
        /// Fish-Net을 호스트(서버+클라이언트)로 시작합니다.
        /// Relay 할당 후에 호출하세요.
        /// </summary>
        public void StartHost()
        {
            if (InstanceFinder.NetworkManager == null)
            {
                Debug.LogError("[RelayManager] NetworkManager를 찾을 수 없습니다.");
                OnConnectionFailed?.Invoke("NetworkManager 없음");
                return;
            }

            try
            {
                InstanceFinder.NetworkManager.ServerManager.StartConnection();
                InstanceFinder.NetworkManager.ClientManager.StartConnection();
                Debug.Log("[RelayManager] Fish-Net 호스트 시작 완료");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayManager] 호스트 시작 실패: {ex.Message}");
                OnConnectionFailed?.Invoke($"호스트 시작 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// Fish-Net을 클라이언트로 시작합니다.
        /// Relay 참가 후에 호출하세요.
        /// </summary>
        public void StartClient()
        {
            if (InstanceFinder.NetworkManager == null)
            {
                Debug.LogError("[RelayManager] NetworkManager를 찾을 수 없습니다.");
                OnConnectionFailed?.Invoke("NetworkManager 없음");
                return;
            }

            try
            {
                InstanceFinder.NetworkManager.ClientManager.StartConnection();
                Debug.Log("[RelayManager] Fish-Net 클라이언트 시작 완료");
            }
            catch (Exception ex)
            {
                Debug.LogError($"[RelayManager] 클라이언트 시작 실패: {ex.Message}");
                OnConnectionFailed?.Invoke($"클라이언트 시작 실패: {ex.Message}");
            }
        }

        /// <summary>
        /// 네트워크 연결 종료
        /// </summary>
        public void StopConnection()
        {
            if (InstanceFinder.NetworkManager == null)
                return;

            if (InstanceFinder.NetworkManager.IsServerStarted)
                InstanceFinder.NetworkManager.ServerManager.StopConnection(true);

            if (InstanceFinder.NetworkManager.IsClientStarted)
                InstanceFinder.NetworkManager.ClientManager.StopConnection();

            Debug.Log("[RelayManager] 네트워크 연결 종료");
        }

        #endregion

        #region 유틸리티

        /// <summary>현재 Join Code 반환 (호스트인 경우)</summary>
        public string GetCurrentJoinCode() => _currentJoinCode ?? "";

        /// <summary>UGS 초기화 완료 여부</summary>
        public bool IsInitialized => _isInitialized;

        /// <summary>네트워크 연결 상태</summary>
        public bool IsConnected => InstanceFinder.NetworkManager != null &&
                                   (InstanceFinder.NetworkManager.IsServerStarted ||
                                    InstanceFinder.NetworkManager.IsClientStarted);

        #endregion
    }
}
