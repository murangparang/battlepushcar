// ============================================================
// GameEnums.cs - 게임 전역 열거형 정의
// Battle Car Sumo - 1v1 Server Authoritative Physics Battle
// ============================================================

namespace BattleCarSumo.Data
{
    /// <summary>
    /// 부품 장착 슬롯 위치
    /// </summary>
    public enum PartSlot
    {
        Front = 0,   // 앞 범퍼
        Rooftop = 1, // 루프탑
        Rear = 2     // 뒷 범퍼
    }

    /// <summary>
    /// 차량 체급 분류
    /// </summary>
    public enum WeightClass
    {
        Light = 0,   // 기동성 중심, 밀기 약함
        Middle = 1,  // 밸런스형
        Heavy = 2    // 낮은 속도, 강력한 관성 및 밀기 힘
    }

    /// <summary>
    /// 게임 상태 머신
    /// </summary>
    public enum GameState
    {
        WaitingForPlayers = 0,  // 플레이어 대기
        Countdown = 1,          // 라운드 시작 카운트다운
        Playing = 2,            // 경기 진행 중
        RoundEnd = 3,           // 라운드 종료
        Intermission = 4,       // 인터미션 (전략 정비)
        MatchEnd = 5            // 매치 종료 (3판 2승 결정)
    }

    /// <summary>
    /// 부품 액션 타입
    /// </summary>
    public enum PartActionType
    {
        None = 0,
        Punch = 1,      // 전방 충격 (앞 범퍼)
        Lift = 2,        // 들어올리기 (루프탑)
        Boost = 3,       // 후방 부스트 (뒷 범퍼)
        Shield = 4,      // 방어 (앞 범퍼)
        Slam = 5,        // 내려찍기 (루프탑)
        SpikeDrop = 6    // 스파이크 투하 (뒷 범퍼)
    }

    /// <summary>
    /// 아레나 타입
    /// </summary>
    public enum ArenaType
    {
        Classic = 0,   // 원형 기본 아레나
        Square = 1,    // 사각형 + 굴곡 바닥
        Punching = 2   // 대형 + 펀칭머신 4개
    }

    /// <summary>
    /// 라운드 결과
    /// </summary>
    public enum RoundResult
    {
        None = 0,
        Player1Win = 1,
        Player2Win = 2,
        Draw = 3,
        TimeExpired = 4  // 시간 초과 (무승부 처리)
    }
}
