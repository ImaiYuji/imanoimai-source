using UnityEngine;

namespace ARCharacterApp
{
    /// <summary>
    /// 差し替え前のプレースホルダー用の簡易モーション。
    ///
    /// 本物のモデルを入れる前でも「動いている」ことが見えるようにするためのもの。
    /// Animator 付きのモデルに差し替えたら、このコンポーネントは外してよい。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class PlaceholderBob : MonoBehaviour
    {
        [SerializeField] float m_Amplitude = 0.03f;
        [SerializeField] float m_Speed = 1.6f;
        [SerializeField] float m_SwaySpeed = 0.7f;
        [SerializeField] float m_SwayAngle = 6f;

        Vector3 m_BaseLocalPosition;
        float m_Seed;

        void Awake()
        {
            m_BaseLocalPosition = transform.localPosition;
            m_Seed = Random.value * 10f;
        }

        void Update()
        {
            var t = Time.time + m_Seed;

            transform.localPosition = m_BaseLocalPosition
                                      + Vector3.up * (Mathf.Sin(t * m_Speed) * m_Amplitude);

            transform.localRotation = Quaternion.Euler(0f, 0f, Mathf.Sin(t * m_SwaySpeed) * m_SwayAngle);
        }
    }
}
