using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;
using UnityEngine.XR.ARSubsystems;

namespace ARCharacterApp
{
    /// <summary>
    /// 平面のスキャン → レティクル表示 → タップで設置 → 指で移動/回転/拡大 までを担当する。
    ///
    /// 「どこでもいいからキャラを出したい」用途に振っているので、
    /// マーカーや位置情報は一切使わず、検出できた水平面ならどこにでも置ける。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARPlacementController : MonoBehaviour
    {
        [Header("AR References")]
        [SerializeField] ARRaycastManager m_RaycastManager;
        [SerializeField] ARAnchorManager m_AnchorManager;
        [SerializeField] ARPlaneManager m_PlaneManager;
        [SerializeField] Camera m_ARCamera;

        [Header("Placement")]
        [Tooltip("設置候補地点に表示するレティクル(リング等)")]
        [SerializeField] GameObject m_Reticle;

        [Tooltip("設置するキャラクターのプレハブ。Animator を持たせておく。")]
        [SerializeField] GameObject m_CharacterPrefab;

        [Tooltip("切り替えられるキャラクター。空なら Character Prefab だけを使う。")]
        [SerializeField] CharacterEntry[] m_Characters = System.Array.Empty<CharacterEntry>();

        [Header("Gestures")]
        [SerializeField] bool m_AllowRotate = true;
        [SerializeField] bool m_AllowScale = true;
        [SerializeField] float m_MinScale = 0.3f;
        [SerializeField] float m_MaxScale = 3f;
        [SerializeField] float m_RotateSpeed = 0.25f;

        [Tooltip("これ以上動かしたらドラッグ扱い(dp)。タップと回転の切り分けに使う")]
        [SerializeField] float m_TapSlopDp = 12f;

        [Tooltip("これより長く触れていたらタップとみなさない(秒)")]
        [SerializeField] float m_MaxTapSeconds = 0.5f;

        [Header("Behaviour")]
        [Tooltip("キャラを常にカメラの方へ向ける(設置直後の向き決めに使用)")]
        [SerializeField] bool m_FaceCameraOnPlace = true;

        [System.Serializable]
        public struct CharacterEntry
        {
            [Tooltip("UI のボタンに出す名前")]
            public string displayName;

            public GameObject prefab;
        }

        static readonly List<ARRaycastHit> s_Hits = new List<ARRaycastHit>();

        /// <summary>キャラを設置し終えたときに発火。</summary>
        public event Action CharacterPlaced;

        /// <summary>選べるキャラクター一覧。</summary>
        public IReadOnlyList<CharacterEntry> Characters => m_Characters;

        /// <summary>いま選ばれているキャラクターの番号。</summary>
        public int SelectedCharacterIndex { get; private set; }

        /// <summary>
        /// キャラクターを差し替える。
        /// すでに置いてあれば、同じ場所・同じ向き・同じ大きさのまま入れ替える。
        /// </summary>
        public void SelectCharacter(int index)
        {
            if (index < 0 || index >= m_Characters.Length || index == SelectedCharacterIndex)
                return;

            SelectedCharacterIndex = index;

            if (Character == null)
                return;

            // 置き直しを求めずにその場で入れ替える。せっかく決めた位置を捨てさせない。
            var transformToKeep = Character.transform;
            var position = transformToKeep.position;
            var rotation = transformToKeep.rotation;
            var scale = transformToKeep.localScale;
            var parent = transformToKeep.parent;

            Destroy(Character.gameObject);
            Character = null;

            SpawnCharacter(parent, position, rotation, scale);
            CharacterPlaced?.Invoke();
        }

        GameObject ResolvePrefab()
        {
            if (m_Characters.Length > 0)
            {
                var index = Mathf.Clamp(SelectedCharacterIndex, 0, m_Characters.Length - 1);
                if (m_Characters[index].prefab != null)
                    return m_Characters[index].prefab;
            }

            return m_CharacterPrefab;
        }

        void SpawnCharacter(Transform parent, Vector3 position, Quaternion rotation, Vector3 scale)
        {
            var prefab = ResolvePrefab();
            if (prefab == null)
            {
                Debug.LogError("[ARPlacementController] 設置するプレハブがありません。");
                return;
            }

            var instance = Instantiate(prefab, position, rotation, parent);
            instance.transform.localScale = scale;

            Character = instance.GetComponent<ARCharacter>() ?? instance.AddComponent<ARCharacter>();
            m_BaseScale = scale.x;

            // 首から上を向ける処理にカメラを教える。
            // キャラを入れ替えても追従の入り切りは保つ。
            m_HeadLook = instance.GetComponentInChildren<ARCharacterHeadLook>(true);
            if (m_HeadLook != null)
            {
                if (m_ARCamera != null)
                    m_HeadLook.Target = m_ARCamera.transform;

                // 向いた状態のままキャラを入れ替えたときは、
                // いまのカメラ位置で取り直す。
                // 前のキャラで控えた座標をそのまま使うと、
                // 見当違いの方向を向いたまま出てきてしまう。
                if (m_HeadLookOn)
                    m_HeadLookOn = m_HeadLook.AimAtTarget();
            }

            Character.PlayEntrance();
        }

        /// <summary>いま画面中央がいずれかの平面に当たっているか。</summary>
        public bool HasValidPlacement { get; private set; }

        /// <summary>設置済みのキャラクター(未設置なら null)。</summary>
        public ARCharacter Character { get; private set; }

        bool m_Scanning;
        Pose m_CandidatePose;
        ARAnchor m_Anchor;

        // ピンチ用の前フレーム状態
        float m_PrevPinchDistance;
        float m_BaseScale = 1f;

        /// <summary>いま触れている指が UI の上から始まったか。タッチ開始時に一度だけ判定する。</summary>
        bool m_TouchStartedOverUI;

        ARCharacterHeadLook m_HeadLook;
        bool m_HeadLookOn;

        // タップと回転ドラッグの切り分け用。触り始めの位置と時刻を覚えておく。
        Vector2 m_TouchStartPosition;
        float m_TouchStartTime;
        bool m_TouchDragged;

        void Reset()
        {
            m_RaycastManager = FindObjectOfType<ARRaycastManager>();
            m_AnchorManager = FindObjectOfType<ARAnchorManager>();
            m_PlaneManager = FindObjectOfType<ARPlaneManager>();
        }

        void Awake()
        {
            if (m_Reticle != null)
                m_Reticle.SetActive(false);
        }

        public void BeginScanning()
        {
            m_Scanning = true;
        }

        public void ClearPlacement()
        {
            if (Character != null)
            {
                Destroy(Character.gameObject);
                Character = null;
            }

            if (m_Anchor != null)
            {
                Destroy(m_Anchor.gameObject);
                m_Anchor = null;
            }

            HasValidPlacement = false;
            m_Scanning = false;

            if (m_Reticle != null)
                m_Reticle.SetActive(false);
        }

        void Update()
        {
            if (m_Scanning)
                UpdateReticle();
            else if (Character != null)
                UpdateGestures();
        }

        // ---- スキャン中 -------------------------------------------------

        void UpdateReticle()
        {
            // 画面中央から下向きにレイを飛ばし、平面との交点を探す。
            // 「狙った場所」より「見ている場所」に置くほうが直感的なので中央固定。
            var screenCenter = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f);

            if (m_RaycastManager != null &&
                m_RaycastManager.Raycast(screenCenter, s_Hits, TrackableType.PlaneWithinPolygon))
            {
                m_CandidatePose = s_Hits[0].pose;
                HasValidPlacement = true;

                if (m_Reticle != null)
                {
                    if (!m_Reticle.activeSelf)
                        m_Reticle.SetActive(true);

                    m_Reticle.transform.SetPositionAndRotation(m_CandidatePose.position, m_CandidatePose.rotation);
                }

                if (WasScreenTappedThisFrame())
                    PlaceCharacter(s_Hits[0]);
            }
            else
            {
                HasValidPlacement = false;

                if (m_Reticle != null && m_Reticle.activeSelf)
                    m_Reticle.SetActive(false);
            }
        }

        void PlaceCharacter(ARRaycastHit hit)
        {
            var prefab = ResolvePrefab();
            if (prefab == null)
            {
                Debug.LogError("[ARPlacementController] CharacterPrefab が未設定です。");
                return;
            }

            var pose = hit.pose;

            // 平面にアンカーを張ると、トラッキングが揺れてもキャラが滑らない。
            var plane = m_PlaneManager != null ? m_PlaneManager.GetPlane(hit.trackableId) : null;
            if (m_AnchorManager != null && plane != null)
                m_Anchor = m_AnchorManager.AttachAnchor(plane, pose);

            var parent = m_Anchor != null ? m_Anchor.transform : null;

            SpawnCharacter(parent, pose.position, pose.rotation, prefab.transform.localScale);

            if (Character == null)
                return;

            if (m_FaceCameraOnPlace && m_ARCamera != null)
                FaceCamera(Character.transform);

            m_Scanning = false;
            HasValidPlacement = false;

            if (m_Reticle != null)
                m_Reticle.SetActive(false);

            CharacterPlaced?.Invoke();
        }

        /// <summary>
        /// 置いてあるキャラをカメラのほうに向け直す(体ごと)。
        /// 設置直後の向き決めに使う。
        /// </summary>
        public void FaceCameraNow()
        {
            if (Character == null || m_ARCamera == null)
                return;

            FaceCamera(Character.transform);
        }

        /// <summary>
        /// 首から上だけを、押した瞬間のカメラ位置に向けて止める。
        /// 体ごと回すと決めたポーズが崩れるので、頭だけ動かす。
        /// 同じ場所から押し直すと元に戻る。
        /// </summary>
        public bool ToggleHeadLook()
        {
            if (m_HeadLook == null)
                return m_HeadLookOn;

            m_HeadLookOn = m_HeadLook.AimAtTarget();
            return m_HeadLookOn;
        }

        /// <summary>いま首から上がカメラを追っているか。</summary>
        public bool HeadLookOn => m_HeadLookOn;

        void FaceCamera(Transform target)
        {
            // 水平成分だけ見てヨー回転させる。上下に傾けるとキャラが埋まる/浮く。
            var toCamera = m_ARCamera.transform.position - target.position;
            toCamera.y = 0f;

            if (toCamera.sqrMagnitude > 0.0001f)
                target.rotation = Quaternion.LookRotation(toCamera, Vector3.up);
        }

        // ---- 設置後のジェスチャ -------------------------------------------

        /// <summary>
        /// タップとみなせる移動量(px の 2 乗)。
        /// 端末の解像度で見え方が変わらないよう dp から換算する。
        /// 生ピクセルの固定値だと、高解像度の端末でタップがほぼ成立しなくなる。
        /// </summary>
        float TapSlopSqr
        {
            get
            {
                var dpi = Screen.dpi > 0f ? Screen.dpi : 160f;
                var slop = m_TapSlopDp * dpi / 160f;
                return slop * slop;
            }
        }

        void UpdateGestures()
        {
            var touchCount = Input.touchCount;

            if (touchCount == 1 && m_AllowRotate)
            {
                var touch = Input.GetTouch(0);

                // UI の上かどうかは「触り始めた瞬間」に判定して覚えておく。
                // 指を離したあとの Ended で聞くと EventSystem の情報が既に消えていて
                // 常に「UI の外」と返るため、ボタンを押しただけでキャラのタップ操作まで
                // 走ってしまう(ポーズボタンが勝手に次へ進む原因だった)。
                if (touch.phase == TouchPhase.Began)
                {
                    m_TouchStartedOverUI = IsPointerOverUI(touch.fingerId);
                    m_TouchStartPosition = touch.position;
                    m_TouchStartTime = Time.unscaledTime;
                    m_TouchDragged = false;
                }

                if (!m_TouchStartedOverUI)
                {
                    // 触り始めからの総移動量で見る。
                    // 直前フレームの deltaPosition で見ていたときは、
                    // 大きく回したあとに指を止めてから離すと移動量が 0 になり、
                    // タップと誤判定してポーズが進んでしまっていた。
                    if ((touch.position - m_TouchStartPosition).sqrMagnitude > TapSlopSqr)
                        m_TouchDragged = true;

                    if (touch.phase == TouchPhase.Moved)
                    {
                        // 横方向のドラッグ = その場で回す
                        Character.transform.Rotate(Vector3.up, -touch.deltaPosition.x * m_RotateSpeed, Space.World);
                    }
                    else if (touch.phase == TouchPhase.Ended
                             && !m_TouchDragged
                             && Time.unscaledTime - m_TouchStartTime <= m_MaxTapSeconds)
                    {
                        // 動かさず、短く触れた = タップ扱い → リアクション再生
                        Character.PlayReaction();
                    }
                }

                if (touch.phase == TouchPhase.Ended || touch.phase == TouchPhase.Canceled)
                {
                    m_TouchStartedOverUI = false;
                    m_TouchDragged = false;
                }

                m_PrevPinchDistance = 0f;
            }
            else if (touchCount == 2 && m_AllowScale)
            {
                var a = Input.GetTouch(0);
                var b = Input.GetTouch(1);
                var distance = Vector2.Distance(a.position, b.position);

                if (m_PrevPinchDistance > 0f)
                {
                    var ratio = distance / m_PrevPinchDistance;
                    var next = Mathf.Clamp(Character.transform.localScale.x * ratio,
                                           m_BaseScale * m_MinScale,
                                           m_BaseScale * m_MaxScale);
                    Character.transform.localScale = Vector3.one * next;
                }

                m_PrevPinchDistance = distance;
            }
            else
            {
                m_PrevPinchDistance = 0f;
            }
        }

        // ---- 入力ユーティリティ ---------------------------------------------

        static bool WasScreenTappedThisFrame()
        {
            if (Input.touchCount == 0)
                return false;

            var touch = Input.GetTouch(0);
            if (touch.phase != TouchPhase.Began)
                return false;

            return !IsPointerOverUI(touch.fingerId);
        }

        static bool IsPointerOverUI(int fingerId)
        {
            var es = EventSystem.current;
            return es != null && es.IsPointerOverGameObject(fingerId);
        }
    }
}
