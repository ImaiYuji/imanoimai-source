using UnityEngine;

namespace ARCharacterApp
{
    /// <summary>
    /// AR 非対応端末向けのフォールバック。
    ///
    /// ここで落ちる・真っ黒になるのが一番の離脱要因なので、
    /// AR が使えない端末でも「キャラを見て、回して、モーションを見る」ところまでは必ず成立させる。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NonARFallbackViewer : MonoBehaviour
    {
        [SerializeField] Camera m_ViewerCamera;
        [SerializeField] GameObject m_CharacterPrefab;
        [SerializeField] Transform m_CharacterAnchor;
        [SerializeField] GameObject m_ViewerRoot;

        [Tooltip("フォールバック時に止める AR 側のルート(XR Origin)。カメラが二重に動くのを防ぐ。")]
        [SerializeField] GameObject m_ARRootToDisable;

        [Header("Framing")]
        [SerializeField] float m_Distance = 2.2f;
        [SerializeField] float m_Height = 1.0f;
        [SerializeField] float m_AutoRotateSpeed = 8f;

        [Header("Input")]
        [SerializeField] float m_DragRotateSpeed = 0.25f;

        ARCharacter m_Character;
        bool m_Active;
        bool m_UserInteracted;

        void Awake()
        {
            if (m_ViewerRoot != null)
                m_ViewerRoot.SetActive(false);
        }

        public void Activate()
        {
            if (m_Active)
                return;

            m_Active = true;

            if (m_ARRootToDisable != null)
                m_ARRootToDisable.SetActive(false);

            if (m_ViewerRoot != null)
                m_ViewerRoot.SetActive(true);

            if (m_ViewerCamera != null)
            {
                m_ViewerCamera.gameObject.SetActive(true);
                m_ViewerCamera.transform.position = m_CharacterAnchor.position
                                                    + new Vector3(0f, m_Height, -m_Distance);
                m_ViewerCamera.transform.LookAt(m_CharacterAnchor.position + Vector3.up * m_Height * 0.6f);
            }

            if (m_CharacterPrefab != null && m_CharacterAnchor != null)
            {
                var instance = Instantiate(m_CharacterPrefab, m_CharacterAnchor.position,
                                           m_CharacterAnchor.rotation, m_CharacterAnchor);

                m_Character = instance.GetComponent<ARCharacter>();
                if (m_Character == null)
                    m_Character = instance.AddComponent<ARCharacter>();

                m_Character.PlayEntrance();
            }
        }

        void Update()
        {
            if (!m_Active || m_CharacterAnchor == null)
                return;

            if (Input.touchCount == 1)
            {
                var touch = Input.GetTouch(0);

                if (touch.phase == TouchPhase.Moved)
                {
                    m_UserInteracted = true;
                    m_CharacterAnchor.Rotate(Vector3.up, -touch.deltaPosition.x * m_DragRotateSpeed, Space.World);
                }
                else if (touch.phase == TouchPhase.Ended && touch.deltaPosition.sqrMagnitude < 25f)
                {
                    if (m_Character != null)
                        m_Character.PlayReaction();
                }
            }
            else if (!m_UserInteracted)
            {
                // 触られるまではゆっくり自動回転させて、動いていることを示す
                m_CharacterAnchor.Rotate(Vector3.up, m_AutoRotateSpeed * Time.deltaTime, Space.World);
            }
        }
    }
}
