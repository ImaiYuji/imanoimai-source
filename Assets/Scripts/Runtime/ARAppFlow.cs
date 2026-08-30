using System;
using System.Collections;
using UnityEngine;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace ARCharacterApp
{
    /// <summary>
    /// アプリ全体の進行フェーズ。UI はこの値だけを見て表示を切り替える。
    /// </summary>
    public enum ARAppPhase
    {
        Booting,
        ExplainingPermission,
        RequestingPermission,
        PermissionDenied,
        CheckingSupport,
        Unsupported,
        Scanning,
        ReadyToPlace,
        Placed,
    }

    /// <summary>
    /// 起動から設置までの状態遷移を一手に引き受けるコントローラ。
    ///
    /// 設計方針(ユーザーの手間を最小化する):
    ///  - OS の権限ダイアログをいきなり出さず、理由を一枚挟んでから要求する
    ///  - AR 非対応端末でも落とさず、3D ビューアにフォールバックする
    ///  - 平面が見つかるまでの間、何をすればいいか常に画面上で示す
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARAppFlow : MonoBehaviour
    {
        [Header("AR References")]
        [SerializeField] ARSession m_Session;
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] ARPlacementController m_Placement;

        [Header("Fallback")]
        [SerializeField] NonARFallbackViewer m_Fallback;

        [Header("Tuning")]
        [Tooltip("権限の説明画面をスキップする(再起動時など、すでに許可済みなら自動でスキップされる)")]
        [SerializeField] bool m_SkipExplanationIfAlreadyGranted = true;

        [Tooltip("この秒数スキャンしても平面が見つからない場合、追加のヒントを表示する")]
        [SerializeField] float m_ScanHintDelay = 12f;

        public ARAppPhase Phase { get; private set; } = ARAppPhase.Booting;

        /// <summary>フェーズが変わるたびに発火。UI はこれを購読する。</summary>
        public event Action<ARAppPhase> PhaseChanged;

        /// <summary>スキャンが長引いているとき true。UI が追加ヒントを出すのに使う。</summary>
        public bool ScanIsTakingLong { get; private set; }

        float m_ScanStartedAt;

        void Awake()
        {
            // 平面検出は「置く場所を探す」間だけ動かす。
            // 設置後も回し続けるとバッテリーと発熱を食うだけなので止める。
            if (m_PlaneManager != null)
                m_PlaneManager.enabled = false;
        }

        void OnEnable()
        {
            if (m_Placement != null)
                m_Placement.CharacterPlaced += OnCharacterPlaced;
        }

        void OnDisable()
        {
            if (m_Placement != null)
                m_Placement.CharacterPlaced -= OnCharacterPlaced;
        }

        IEnumerator Start()
        {
            yield return StartCoroutine(RunBootSequence());
        }

        IEnumerator RunBootSequence()
        {
            // 1. カメラ権限 --------------------------------------------------
            if (!HasCameraPermission())
            {
                if (m_SkipExplanationIfAlreadyGranted)
                {
                    // 説明画面を出し、UI 側から ConfirmPermissionExplanation() が呼ばれるのを待つ
                    SetPhase(ARAppPhase.ExplainingPermission);
                    while (Phase == ARAppPhase.ExplainingPermission)
                        yield return null;
                }

                SetPhase(ARAppPhase.RequestingPermission);
                yield return StartCoroutine(RequestCameraPermission());

                if (!HasCameraPermission())
                {
                    SetPhase(ARAppPhase.PermissionDenied);
                    yield break;
                }
            }

            // 2. AR 対応チェック ----------------------------------------------
            SetPhase(ARAppPhase.CheckingSupport);
            yield return ARSession.CheckAvailability();

            if (ARSession.state == ARSessionState.NeedsInstall)
            {
                // Android: Google Play Services for AR が未導入。ストア導線に載せる。
                yield return ARSession.Install();
            }

            if (!IsSessionUsable(ARSession.state))
            {
                EnterFallback();
                yield break;
            }

            // 3. セッション開始 → スキャン ------------------------------------
            if (m_Session != null)
                m_Session.enabled = true;

            if (m_PlaneManager != null)
                m_PlaneManager.enabled = true;

            if (m_Placement != null)
                m_Placement.BeginScanning();

            m_ScanStartedAt = Time.time;
            ScanIsTakingLong = false;
            SetPhase(ARAppPhase.Scanning);
        }

        void Update()
        {
            switch (Phase)
            {
                case ARAppPhase.Scanning:
                    if (!ScanIsTakingLong && Time.time - m_ScanStartedAt > m_ScanHintDelay)
                        ScanIsTakingLong = true;

                    // レティクルが平面に乗った時点で「置ける」状態へ
                    if (m_Placement != null && m_Placement.HasValidPlacement)
                        SetPhase(ARAppPhase.ReadyToPlace);
                    break;

                case ARAppPhase.ReadyToPlace:
                    // 平面を見失ったらスキャンに戻す
                    if (m_Placement != null && !m_Placement.HasValidPlacement)
                    {
                        m_ScanStartedAt = Time.time;
                        ScanIsTakingLong = false;
                        SetPhase(ARAppPhase.Scanning);
                    }
                    break;

                case ARAppPhase.Placed:
                    // セッションがトラッキングを失ったら復帰を促す(UI 側で表示)
                    break;
            }
        }

        void OnCharacterPlaced()
        {
            // 設置が済んだら平面検出とその可視化を止める。
            // 見た目のノイズが消え、消費電力も下がる。
            if (m_PlaneManager != null)
            {
                foreach (var plane in m_PlaneManager.trackables)
                    plane.gameObject.SetActive(false);

                m_PlaneManager.enabled = false;
            }

            SetPhase(ARAppPhase.Placed);
        }

        /// <summary>設置し直す(UI のリセットボタンから呼ぶ)。</summary>
        public void ResetPlacement()
        {
            if (m_Placement != null)
                m_Placement.ClearPlacement();

            if (m_PlaneManager != null)
            {
                m_PlaneManager.enabled = true;
                foreach (var plane in m_PlaneManager.trackables)
                    plane.gameObject.SetActive(true);
            }

            if (m_Placement != null)
                m_Placement.BeginScanning();

            m_ScanStartedAt = Time.time;
            ScanIsTakingLong = false;
            SetPhase(ARAppPhase.Scanning);
        }

        /// <summary>権限の説明画面で「続ける」が押されたときに UI から呼ぶ。</summary>
        public void ConfirmPermissionExplanation()
        {
            if (Phase == ARAppPhase.ExplainingPermission)
                SetPhase(ARAppPhase.RequestingPermission);
        }

        /// <summary>権限を拒否されたあと、設定アプリから戻ってきた場合の再試行。</summary>
        public void RetryFromDenied()
        {
            if (Phase != ARAppPhase.PermissionDenied)
                return;

            StopAllCoroutines();
            StartCoroutine(RunBootSequence());
        }

        void EnterFallback()
        {
            SetPhase(ARAppPhase.Unsupported);

            if (m_Session != null)
                m_Session.enabled = false;

            if (m_Fallback != null)
                m_Fallback.Activate();
        }

        static bool IsSessionUsable(ARSessionState state)
        {
            return state == ARSessionState.Ready
                || state == ARSessionState.SessionInitializing
                || state == ARSessionState.SessionTracking;
        }

#if UNITY_IOS && !UNITY_EDITOR
        // カメラの許可は OS に直接聞く。
        // Unity の Application.HasUserAuthorization(WebCam) は
        // 「Unity 自身が要求したか」しか見ておらず、カメラを開くのが ARKit の
        // このアプリでは許可済みでも false のままになる。
        // そのせいで、許可しても説明画面から先に進めなくなっていた。
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern int ARCameraPermission_Status();

        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void ARCameraPermission_Request();

        /// <summary>0 = 未決定 / 1 = 制限 / 2 = 拒否 / 3 = 許可</summary>
        const int k_IosCameraAuthorized = 3;
        const int k_IosCameraNotDetermined = 0;
#endif

        static bool HasCameraPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            return Permission.HasUserAuthorizedPermission(Permission.Camera);
#elif UNITY_IOS && !UNITY_EDITOR
            return ARCameraPermission_Status() == k_IosCameraAuthorized;
#else
            return true;
#endif
        }

        IEnumerator RequestCameraPermission()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            var granted = false;
            var answered = false;

            var callbacks = new PermissionCallbacks();
            callbacks.PermissionGranted += _ => { granted = true;  answered = true; };
            callbacks.PermissionDenied  += _ => { granted = false; answered = true; };
            callbacks.PermissionDeniedAndDontAskAgain += _ => { granted = false; answered = true; };

            Permission.RequestUserPermission(Permission.Camera, callbacks);

            // アプリがバックグラウンドに落ちてコールバックが失われるケースの保険として、
            // 権限状態そのものも毎フレーム見る。
            while (!answered)
            {
                if (Permission.HasUserAuthorizedPermission(Permission.Camera))
                {
                    granted = true;
                    break;
                }
                yield return null;
            }

            // 許可直後は 1 フレーム待たないとカメラが掴めないことがある
            if (granted)
                yield return null;
#elif UNITY_IOS && !UNITY_EDITOR
            // すでに答えが出ているなら聞き直さない。
            // 一度拒否された状態で要求しても、iOS はダイアログを出さずに黙って拒否を返す。
            if (ARCameraPermission_Status() == k_IosCameraNotDetermined)
            {
                ARCameraPermission_Request();

                // 結果は非同期に決まる。答えが出るまで状態を見張る。
                // 応答が返らない場合に固まらないよう、上限を設ける。
                var waited = 0f;
                while (ARCameraPermission_Status() == k_IosCameraNotDetermined && waited < 60f)
                {
                    waited += Time.unscaledDeltaTime;
                    yield return null;
                }
            }

            // 許可直後は 1 フレーム待たないとカメラが掴めないことがある
            if (ARCameraPermission_Status() == k_IosCameraAuthorized)
                yield return null;
#else
            yield break;
#endif
        }

        void SetPhase(ARAppPhase next)
        {
            if (Phase == next)
                return;

            Phase = next;
            PhaseChanged?.Invoke(next);
        }
    }
}
