// ============================================================
// AudioManager.cs - 프로시저럴 오디오 매니저
// 오디오 파일 없이 런타임에 사운드를 생성합니다.
// ============================================================

using System.Collections;
using System.Collections.Generic;
using UnityEngine;

namespace BattleCarSumo.Audio
{
    /// <summary>
    /// 프로시저럴 사운드 시스템. 오디오 파일 없이 런타임에 모든 사운드를 생성합니다.
    /// 싱글톤 패턴으로 어디서든 접근 가능.
    /// </summary>
    public class AudioManager : MonoBehaviour
    {
        public static AudioManager Instance { get; private set; }

        [Header("=== 볼륨 설정 ===")]
        [Range(0f, 1f)] public float masterVolume = 0.7f;
        [Range(0f, 1f)] public float sfxVolume = 0.8f;
        [Range(0f, 1f)] public float bgmVolume = 0.3f;
        [Range(0f, 1f)] public float engineVolume = 0.15f;

        // AudioSource 풀
        private AudioSource _bgmSource;
        private AudioSource _engineSource1;
        private AudioSource _engineSource2;
        private List<AudioSource> _sfxSources = new List<AudioSource>();
        private const int SFX_POOL_SIZE = 8;

        // BGM 상태
        private bool _bgmPlaying = false;
        private Coroutine _bgmCoroutine;

        // 엔진 사운드 상태
        private float _engine1Speed = 0f;
        private float _engine2Speed = 0f;
        private float _engine1Phase = 0f;
        private float _engine2Phase = 0f;

        // 프로시저럴 오디오 클립 캐시
        private AudioClip _countdownBeep;
        private AudioClip _goSound;
        private AudioClip _winFanfare;
        private AudioClip _loseBuzz;
        private AudioClip _roundEndWhistle;
        private AudioClip _matchStartHorn;
        private AudioClip _matchEndFanfare;
        private AudioClip _punchHit;
        private AudioClip _boostWhoosh;
        private AudioClip _liftUp;
        private AudioClip _engineLoop;
        private AudioClip _bgmLoop;

        #region Initialization

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }
            Instance = this;

            CreateAudioSources();
            GenerateAllClips();
        }

        private void CreateAudioSources()
        {
            // BGM
            _bgmSource = gameObject.AddComponent<AudioSource>();
            _bgmSource.loop = true;
            _bgmSource.playOnAwake = false;
            _bgmSource.volume = bgmVolume * masterVolume;

            // 엔진 P1
            _engineSource1 = gameObject.AddComponent<AudioSource>();
            _engineSource1.loop = true;
            _engineSource1.playOnAwake = false;
            _engineSource1.volume = engineVolume * masterVolume;

            // 엔진 P2
            _engineSource2 = gameObject.AddComponent<AudioSource>();
            _engineSource2.loop = true;
            _engineSource2.playOnAwake = false;
            _engineSource2.volume = engineVolume * masterVolume * 0.6f;

            // SFX 풀
            for (int i = 0; i < SFX_POOL_SIZE; i++)
            {
                AudioSource sfx = gameObject.AddComponent<AudioSource>();
                sfx.loop = false;
                sfx.playOnAwake = false;
                sfx.volume = sfxVolume * masterVolume;
                _sfxSources.Add(sfx);
            }
        }

        #endregion

        #region Procedural Audio Generation

        private void GenerateAllClips()
        {
            _countdownBeep = GenerateTone(880f, 0.15f, ToneType.Sine, 0.6f);
            _goSound = GenerateGoSound();
            _winFanfare = GenerateWinFanfare();
            _loseBuzz = GenerateLoseBuzz();
            _roundEndWhistle = GenerateWhistle();
            _matchStartHorn = GenerateHorn();
            _matchEndFanfare = GenerateMatchEndFanfare();
            _punchHit = GenerateImpact();
            _boostWhoosh = GenerateWhoosh();
            _liftUp = GenerateLiftSound();
            _engineLoop = GenerateEngineLoop();
            _bgmLoop = GenerateBGM();
        }

        private enum ToneType { Sine, Square, Sawtooth, Triangle, Noise }

        private AudioClip GenerateTone(float freq, float duration, ToneType type, float volume = 1f,
                                         float fadeIn = 0.01f, float fadeOut = 0.05f)
        {
            int sampleRate = 44100;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float phase = t * freq;
                float sample = 0f;

                switch (type)
                {
                    case ToneType.Sine:
                        sample = Mathf.Sin(phase * 2f * Mathf.PI);
                        break;
                    case ToneType.Square:
                        sample = Mathf.Sin(phase * 2f * Mathf.PI) > 0 ? 1f : -1f;
                        sample *= 0.5f;
                        break;
                    case ToneType.Sawtooth:
                        sample = 2f * (phase % 1f) - 1f;
                        sample *= 0.5f;
                        break;
                    case ToneType.Triangle:
                        sample = 2f * Mathf.Abs(2f * (phase % 1f) - 1f) - 1f;
                        break;
                    case ToneType.Noise:
                        sample = Random.Range(-1f, 1f);
                        break;
                }

                // Envelope
                float env = 1f;
                if (t < fadeIn) env = t / fadeIn;
                if (t > duration - fadeOut) env = (duration - t) / fadeOut;
                env = Mathf.Clamp01(env);

                data[i] = sample * volume * env;
            }

            AudioClip clip = AudioClip.Create("tone", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateGoSound()
        {
            int sampleRate = 44100;
            float duration = 0.5f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                // 두 음의 화음 (C5 + E5)
                float s1 = Mathf.Sin(t * 1046.5f * 2f * Mathf.PI) * 0.4f;
                float s2 = Mathf.Sin(t * 1318.5f * 2f * Mathf.PI) * 0.3f;
                float s3 = Mathf.Sin(t * 1568f * 2f * Mathf.PI) * 0.2f; // G5

                float env = 1f;
                if (t < 0.02f) env = t / 0.02f;
                if (t > duration - 0.2f) env = (duration - t) / 0.2f;

                data[i] = (s1 + s2 + s3) * Mathf.Clamp01(env);
            }

            AudioClip clip = AudioClip.Create("go", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateWinFanfare()
        {
            int sampleRate = 44100;
            float duration = 1.5f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            // 승리 멜로디: C-E-G-C(high) 순서로 아르페지오
            float[] notes = { 523.25f, 659.25f, 783.99f, 1046.5f };
            float noteLen = duration / notes.Length;

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                int noteIdx = Mathf.Min((int)(t / noteLen), notes.Length - 1);
                float noteT = t - noteIdx * noteLen;

                float freq = notes[noteIdx];
                float s = Mathf.Sin(noteT * freq * 2f * Mathf.PI) * 0.35f;
                s += Mathf.Sin(noteT * freq * 2f * 2f * Mathf.PI) * 0.15f; // 옥타브
                s += Mathf.Sin(noteT * freq * 3f * 2f * Mathf.PI) * 0.08f; // 5th harmonic

                // 각 노트의 envelope
                float env = 1f;
                if (noteT < 0.02f) env = noteT / 0.02f;
                if (noteT > noteLen - 0.08f) env = (noteLen - noteT) / 0.08f;
                // 전체 페이드 아웃
                if (t > duration - 0.3f) env *= (duration - t) / 0.3f;

                data[i] = s * Mathf.Clamp01(env);
            }

            AudioClip clip = AudioClip.Create("win", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateLoseBuzz()
        {
            int sampleRate = 44100;
            float duration = 1.0f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                // 낮은 불협화음
                float s = Mathf.Sin(t * 110f * 2f * Mathf.PI) * 0.3f;
                s += Mathf.Sin(t * 116.5f * 2f * Mathf.PI) * 0.2f; // 미세한 차이로 비트감
                s += Mathf.Sin(t * 82.4f * 2f * Mathf.PI) * 0.15f;

                float env = 1f;
                if (t < 0.02f) env = t / 0.02f;
                if (t > duration - 0.3f) env = (duration - t) / 0.3f;

                data[i] = s * Mathf.Clamp01(env);
            }

            AudioClip clip = AudioClip.Create("lose", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateWhistle()
        {
            int sampleRate = 44100;
            float duration = 0.6f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                // 피치가 내려가는 휘슬
                float freq = Mathf.Lerp(2000f, 1200f, t / duration);
                float s = Mathf.Sin(t * freq * 2f * Mathf.PI) * 0.3f;
                s += Mathf.Sin(t * freq * 2f * 2f * Mathf.PI) * 0.1f;

                float env = 1f;
                if (t < 0.02f) env = t / 0.02f;
                if (t > duration - 0.15f) env = (duration - t) / 0.15f;

                data[i] = s * Mathf.Clamp01(env);
            }

            AudioClip clip = AudioClip.Create("whistle", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateHorn()
        {
            int sampleRate = 44100;
            float duration = 0.8f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                // 두꺼운 호른 소리
                float s = Mathf.Sin(t * 440f * 2f * Mathf.PI) * 0.25f;
                s += Mathf.Sin(t * 554.37f * 2f * Mathf.PI) * 0.2f; // C#5
                s += Mathf.Sin(t * 659.25f * 2f * Mathf.PI) * 0.15f; // E5
                // 약간의 사각파 질감
                s += (Mathf.Sin(t * 440f * 2f * Mathf.PI) > 0 ? 0.1f : -0.1f);

                float env = 1f;
                if (t < 0.05f) env = t / 0.05f;
                if (t > duration - 0.2f) env = (duration - t) / 0.2f;

                data[i] = s * Mathf.Clamp01(env);
            }

            AudioClip clip = AudioClip.Create("horn", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateMatchEndFanfare()
        {
            int sampleRate = 44100;
            float duration = 2.5f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            // 장엄한 매치 종료: G4-B4-D5-G5 (G major arpeggio)
            float[] notes = { 392f, 493.88f, 587.33f, 783.99f, 783.99f };
            float[] noteDurations = { 0.4f, 0.4f, 0.4f, 0.6f, 0.7f };

            float noteStart = 0f;
            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;

                // 현재 노트 결정
                float cumDur = 0f;
                int noteIdx = 0;
                for (int n = 0; n < notes.Length; n++)
                {
                    cumDur += noteDurations[n];
                    if (t < cumDur) { noteIdx = n; break; }
                    noteStart = cumDur;
                    if (n == notes.Length - 1) noteIdx = n;
                }
                float noteT = t - (cumDur - noteDurations[noteIdx]);

                float freq = notes[noteIdx];
                float s = Mathf.Sin(noteT * freq * 2f * Mathf.PI) * 0.3f;
                s += Mathf.Sin(noteT * freq * 2f * 2f * Mathf.PI) * 0.12f;
                s += Mathf.Sin(noteT * freq * 1.5f * 2f * Mathf.PI) * 0.08f; // 5th

                float env = 1f;
                if (noteT < 0.03f) env = noteT / 0.03f;
                float nd = noteDurations[noteIdx];
                if (noteT > nd - 0.1f) env = (nd - noteT) / 0.1f;
                if (t > duration - 0.5f) env *= (duration - t) / 0.5f;

                data[i] = s * Mathf.Clamp01(env);
            }

            AudioClip clip = AudioClip.Create("matchEnd", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateImpact()
        {
            int sampleRate = 44100;
            float duration = 0.25f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                // 짧은 충격음: 노이즈 + 낮은 톤
                float noise = Random.Range(-1f, 1f) * 0.4f;
                float tone = Mathf.Sin(t * 150f * 2f * Mathf.PI) * 0.5f;

                float env = Mathf.Exp(-t * 20f); // 빠른 감쇠

                data[i] = (noise + tone) * env * 0.7f;
            }

            AudioClip clip = AudioClip.Create("impact", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateWhoosh()
        {
            int sampleRate = 44100;
            float duration = 0.4f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                // 필터링된 노이즈 (부스트 효과)
                float noise = Random.Range(-1f, 1f);
                float sweep = Mathf.Sin(t * Mathf.Lerp(200f, 800f, t / duration) * 2f * Mathf.PI) * 0.3f;

                float env = 1f;
                if (t < 0.05f) env = t / 0.05f;
                env *= Mathf.Exp(-t * 5f);

                data[i] = (noise * 0.3f + sweep) * env;
            }

            AudioClip clip = AudioClip.Create("whoosh", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateLiftSound()
        {
            int sampleRate = 44100;
            float duration = 0.35f;
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                // 올라가는 피치
                float freq = Mathf.Lerp(300f, 1200f, t / duration);
                float s = Mathf.Sin(t * freq * 2f * Mathf.PI) * 0.35f;
                s += Mathf.Sin(t * freq * 1.5f * 2f * Mathf.PI) * 0.1f;

                float env = 1f;
                if (t < 0.02f) env = t / 0.02f;
                if (t > duration - 0.1f) env = (duration - t) / 0.1f;

                data[i] = s * Mathf.Clamp01(env);
            }

            AudioClip clip = AudioClip.Create("lift", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateEngineLoop()
        {
            int sampleRate = 44100;
            float duration = 2f; // 루프용
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                // 기본 엔진 톤 (삼각파 기반)
                float baseFreq = 80f;
                float s = 0f;
                // 기본 진동
                s += (2f * Mathf.Abs(2f * ((t * baseFreq) % 1f) - 1f) - 1f) * 0.3f;
                // 2차 하모닉
                s += (2f * Mathf.Abs(2f * ((t * baseFreq * 2f) % 1f) - 1f) - 1f) * 0.15f;
                // 약간의 불규칙
                s += Mathf.Sin(t * 23.5f * 2f * Mathf.PI) * 0.05f;
                // 느린 진폭 변조 (엔진 떨림)
                s *= 1f + Mathf.Sin(t * 7f * 2f * Mathf.PI) * 0.15f;

                data[i] = s * 0.5f;
            }

            AudioClip clip = AudioClip.Create("engine", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        private AudioClip GenerateBGM()
        {
            int sampleRate = 44100;
            float duration = 16f; // 16초 루프
            int sampleCount = Mathf.CeilToInt(sampleRate * duration);
            float[] data = new float[sampleCount];

            // 간단한 전투 BGM: 베이스 라인 + 리듬 + 패드
            float bpm = 140f;
            float beatDuration = 60f / bpm;

            // 코드 진행: Am - F - C - G (각 2비트)
            float[][] chords = {
                new float[] { 220f, 261.63f, 329.63f },   // Am
                new float[] { 174.61f, 220f, 261.63f },    // F
                new float[] { 261.63f, 329.63f, 392f },    // C
                new float[] { 196f, 246.94f, 293.66f }     // G
            };
            float[] bassNotes = { 110f, 87.31f, 130.81f, 98f };

            for (int i = 0; i < sampleCount; i++)
            {
                float t = (float)i / sampleRate;
                float beatPos = t / beatDuration;
                int measure = (int)(beatPos / 4f) % 4;
                float beatInMeasure = beatPos % 4f;
                float beatFrac = beatPos % 1f;

                float s = 0f;

                // 1. 베이스 라인 (각 비트마다)
                float bassFreq = bassNotes[measure];
                float bassEnv = Mathf.Exp(-beatFrac * 4f);
                s += Mathf.Sin(t * bassFreq * 2f * Mathf.PI) * 0.15f * bassEnv;

                // 2. 패드 (코드)
                float[] chord = chords[measure];
                for (int c = 0; c < chord.Length; c++)
                {
                    s += Mathf.Sin(t * chord[c] * 2f * Mathf.PI) * 0.04f;
                }

                // 3. 하이햇 리듬 (8분 음표)
                float hihatBeat = (beatPos * 2f) % 1f;
                if (hihatBeat < 0.1f)
                {
                    float hihatEnv = Mathf.Exp(-hihatBeat * 50f);
                    s += Random.Range(-1f, 1f) * 0.06f * hihatEnv;
                }

                // 4. 킥 드럼 (1, 3 비트)
                if (beatInMeasure < 0.1f || (beatInMeasure > 2f && beatInMeasure < 2.1f))
                {
                    float kickT = (beatInMeasure % 2f);
                    float kickEnv = Mathf.Exp(-kickT * 25f);
                    float kickFreq = Mathf.Lerp(150f, 50f, kickT * 10f);
                    s += Mathf.Sin(kickT * kickFreq * 2f * Mathf.PI) * 0.2f * kickEnv;
                }

                // 5. 스네어 (2, 4 비트)
                float snareCheck = beatInMeasure - 1f;
                if ((snareCheck >= 0f && snareCheck < 0.12f) ||
                    (beatInMeasure >= 3f && beatInMeasure < 3.12f))
                {
                    float snareT = (beatInMeasure >= 3f) ? beatInMeasure - 3f : snareCheck;
                    float snareEnv = Mathf.Exp(-snareT * 30f);
                    s += Random.Range(-1f, 1f) * 0.1f * snareEnv;
                    s += Mathf.Sin(snareT * 200f * 2f * Mathf.PI) * 0.06f * snareEnv;
                }

                data[i] = Mathf.Clamp(s, -1f, 1f);
            }

            AudioClip clip = AudioClip.Create("bgm", sampleCount, 1, sampleRate, false);
            clip.SetData(data, 0);
            return clip;
        }

        #endregion

        #region Public API

        /// <summary>카운트다운 비프 (3, 2, 1)</summary>
        public void PlayCountdownBeep()
        {
            PlaySFX(_countdownBeep, 0.5f);
        }

        /// <summary>"GO!" 사운드</summary>
        public void PlayGoSound()
        {
            PlaySFX(_goSound, 0.7f);
        }

        /// <summary>라운드 승리 시</summary>
        public void PlayRoundWin()
        {
            PlaySFX(_winFanfare, 0.8f);
        }

        /// <summary>라운드 패배 시</summary>
        public void PlayRoundLose()
        {
            PlaySFX(_loseBuzz, 0.6f);
        }

        /// <summary>라운드 종료 휘슬</summary>
        public void PlayRoundEnd()
        {
            PlaySFX(_roundEndWhistle, 0.5f);
        }

        /// <summary>매치 시작 호른</summary>
        public void PlayMatchStart()
        {
            PlaySFX(_matchStartHorn, 0.7f);
        }

        /// <summary>매치 종료 팡파레</summary>
        public void PlayMatchEnd()
        {
            PlaySFX(_matchEndFanfare, 0.9f);
        }

        /// <summary>펀치 타격</summary>
        public void PlayPunchHit()
        {
            PlaySFX(_punchHit, 0.7f);
        }

        /// <summary>부스트</summary>
        public void PlayBoost()
        {
            PlaySFX(_boostWhoosh, 0.6f);
        }

        /// <summary>리프트</summary>
        public void PlayLift()
        {
            PlaySFX(_liftUp, 0.5f);
        }

        /// <summary>BGM 시작</summary>
        public void StartBGM()
        {
            if (_bgmPlaying) return;
            _bgmPlaying = true;
            _bgmSource.clip = _bgmLoop;
            _bgmSource.volume = bgmVolume * masterVolume;
            _bgmSource.Play();
        }

        /// <summary>BGM 정지</summary>
        public void StopBGM()
        {
            if (!_bgmPlaying) return;
            _bgmPlaying = false;
            if (_bgmCoroutine != null) StopCoroutine(_bgmCoroutine);
            _bgmCoroutine = StartCoroutine(FadeOutAudio(_bgmSource, 1f));
        }

        /// <summary>엔진 사운드 시작</summary>
        public void StartEngineSound()
        {
            if (!_engineSource1.isPlaying)
            {
                _engineSource1.clip = _engineLoop;
                _engineSource1.Play();
            }
            if (!_engineSource2.isPlaying)
            {
                _engineSource2.clip = _engineLoop;
                _engineSource2.pitch = 0.9f; // 약간 다른 피치
                _engineSource2.Play();
            }
        }

        /// <summary>엔진 사운드 정지</summary>
        public void StopEngineSound()
        {
            _engineSource1.Stop();
            _engineSource2.Stop();
        }

        /// <summary>P1 차량 속도에 맞게 엔진음 조절</summary>
        public void UpdateEngineSpeed(float speed1, float speed2)
        {
            _engine1Speed = speed1;
            _engine2Speed = speed2;

            if (_engineSource1.isPlaying)
            {
                float pitch1 = 0.6f + Mathf.Clamp01(speed1 / 12f) * 1.4f;
                _engineSource1.pitch = pitch1;
                _engineSource1.volume = engineVolume * masterVolume * (0.3f + Mathf.Clamp01(speed1 / 6f) * 0.7f);
            }
            if (_engineSource2.isPlaying)
            {
                float pitch2 = 0.55f + Mathf.Clamp01(speed2 / 12f) * 1.3f;
                _engineSource2.pitch = pitch2;
                _engineSource2.volume = engineVolume * masterVolume * 0.5f * (0.3f + Mathf.Clamp01(speed2 / 6f) * 0.7f);
            }
        }

        #endregion

        #region Internal

        private void PlaySFX(AudioClip clip, float volumeScale)
        {
            if (clip == null) return;

            // 비어있는 SFX 소스 찾기
            foreach (var src in _sfxSources)
            {
                if (!src.isPlaying)
                {
                    src.clip = clip;
                    src.volume = sfxVolume * masterVolume * volumeScale;
                    src.pitch = 1f;
                    src.Play();
                    return;
                }
            }

            // 모두 사용 중이면 첫 번째 소스 재활용
            _sfxSources[0].clip = clip;
            _sfxSources[0].volume = sfxVolume * masterVolume * volumeScale;
            _sfxSources[0].pitch = 1f;
            _sfxSources[0].Play();
        }

        private IEnumerator FadeOutAudio(AudioSource source, float duration)
        {
            float startVol = source.volume;
            float t = 0f;
            while (t < duration)
            {
                t += Time.deltaTime;
                source.volume = Mathf.Lerp(startVol, 0f, t / duration);
                yield return null;
            }
            source.Stop();
            source.volume = startVol;
        }

        #endregion
    }
}
