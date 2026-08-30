using UnityEngine;

namespace ARCharacterApp
{
    /// <summary>
    /// ポーズに微小な呼吸の動きを足す。
    ///
    /// 使っているポーズクリップはほとんどが 1 フレームの静止ポーズで、
    /// そのまま再生すると完全に固まって人形に見える。
    /// このモデルには揺れもの(PhysBone 等)も入っていないので、
    /// 胸と頭をわずかに動かして生気を出す。
    ///
    /// 実行順を遅らせているのは、Animator がポーズを適用したあとに
    /// 上乗せする必要があるため。先に動かしても Animator に上書きされる。
    /// </summary>
    [DefaultExecutionOrder(200)]
    [DisallowMultipleComponent]
    public sealed class ARCharacterBreathing : MonoBehaviour
    {
        [SerializeField] Animator m_Animator;

        [Tooltip("胸の上下(度)")]
        [SerializeField] float m_ChestAngle = 1.4f;

        [Tooltip("頭の揺れ(度)")]
        [SerializeField] float m_HeadAngle = 0.9f;

        [Tooltip("1 秒あたりの周期")]
        [SerializeField] float m_Speed = 0.85f;

        Transform m_Chest;
        Transform m_Head;
        float m_Seed;

        void Awake()
        {
            if (m_Animator == null)
                m_Animator = GetComponentInChildren<Animator>();

            if (m_Animator == null || m_Animator.avatar == null || !m_Animator.avatar.isHuman)
            {
                enabled = false;
                return;
            }

            m_Chest = m_Animator.GetBoneTransform(HumanBodyBones.Chest)
                      ?? m_Animator.GetBoneTransform(HumanBodyBones.Spine);
            m_Head = m_Animator.GetBoneTransform(HumanBodyBones.Head);

            if (m_Chest == null && m_Head == null)
                enabled = false;

            // 複数体を置いたときに呼吸が揃うと不自然なのでずらす
            m_Seed = Random.value * 10f;
        }

        void LateUpdate()
        {
            var t = Time.time * m_Speed + m_Seed;
            var wave = Mathf.Sin(t);

            if (m_Chest != null)
                m_Chest.localRotation *= Quaternion.Euler(wave * m_ChestAngle, 0f, 0f);

            if (m_Head != null)
            {
                // 頭は胸と逆位相にすると、視線の高さが保たれて自然に見える
                m_Head.localRotation *= Quaternion.Euler(-wave * m_HeadAngle * 0.6f,
                                                          Mathf.Sin(t * 0.53f) * m_HeadAngle, 0f);
            }
        }
    }
}
