using UnityEngine;

namespace ARCharacterApp
{
    /// <summary>
    /// 首から上だけをカメラのほうに向ける。
    ///
    /// 体ごと回すとポーズの見せ場が崩れるので、Animator の IK LookAt を使って
    /// 頭(と首)にだけ配分する。bodyWeight を 0 にしてあるため胴は動かない。
    ///
    /// 見る先は「ボタンを押した瞬間のカメラ位置」で固定する。
    /// 追従し続けると撮る側の手ブレで首が揺れて落ち着かないため、
    /// 一度向いたらそこで静止させる。
    ///
    /// ただし当て込み自体は毎フレーム必要になる。
    /// ポーズクリップが毎フレーム全身のマッスルを書くので、
    /// 一度向けただけでは次のフレームで元に戻ってしまう。
    ///
    /// IK は Animator の更新中に走るので、LateUpdate で上乗せしている
    /// 呼吸(ARCharacterBreathing)とは衝突しない。呼吸の揺れはこの上に乗る。
    ///
    /// この仕組みを効かせるには、Animator Controller のレイヤー 0 で
    /// IK Pass が有効になっている必要がある(AnomeaCharacterSetup で設定済み)。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARCharacterHeadLook : MonoBehaviour
    {
        [SerializeField] Animator m_Animator;

        [Tooltip("見つめる先。ふつうは AR カメラ")]
        [SerializeField] Transform m_Target;

        [Tooltip("胴をどれだけ連動させるか。0 なら首から上だけ")]
        [Range(0f, 1f)]
        [SerializeField] float m_BodyWeight;

        [Tooltip("頭をどれだけ向けるか")]
        [Range(0f, 1f)]
        [SerializeField] float m_HeadWeight = 1f;

        [Tooltip("目をどれだけ向けるか。目ボーンが無いモデルでは効かない")]
        [Range(0f, 1f)]
        [SerializeField] float m_EyesWeight;

        [Tooltip("可動域の制限。大きいほど無理に振り向かなくなる")]
        [Range(0f, 1f)]
        [SerializeField] float m_ClampWeight = 0.55f;

        [Tooltip("向き始め / 戻りにかける秒数")]
        [SerializeField] float m_BlendSeconds = 0.25f;

        [Tooltip("この距離より近い場所から押し直したら、向くのをやめる(m)")]
        [SerializeField] float m_ReleaseDistance = 0.3f;

        float m_Weight;

        /// <summary>押した瞬間に控えた、見る先のワールド座標。</summary>
        Vector3 m_LookPoint;

        /// <summary>いま向いた状態を保っているか。</summary>
        public bool Active { get; private set; }

        public Transform Target
        {
            get => m_Target;
            set => m_Target = value;
        }

        void Awake()
        {
            if (m_Animator == null)
                m_Animator = GetComponent<Animator>();

            // Humanoid でないと LookAt は効かない
            if (m_Animator == null || m_Animator.avatar == null || !m_Animator.avatar.isHuman)
                enabled = false;
        }

        public void SetActive(bool active) => Active = active;

        /// <summary>
        /// いまのカメラ位置に向き直して、そこで止める。
        ///
        /// 同じ場所から押し直したときだけ解除にする。
        /// 撮る側が動いてから押せば向き直し、その場でもう一度押せば元に戻る。
        /// 解除専用のボタンを増やさずに済ませるための割り切り。
        /// </summary>
        public bool AimAtTarget()
        {
            if (m_Target == null)
                return Active;

            var point = m_Target.position;

            if (Active && (point - m_LookPoint).sqrMagnitude <= m_ReleaseDistance * m_ReleaseDistance)
            {
                Active = false;
                return false;
            }

            m_LookPoint = point;
            Active = true;
            return true;
        }

        void OnAnimatorIK(int layerIndex)
        {
            // IK Pass はレイヤーごとに呼ばれる。ポーズのレイヤーでだけ処理する。
            if (layerIndex != 0)
                return;

            var goal = Active ? 1f : 0f;

            m_Weight = m_BlendSeconds > 0f
                ? Mathf.MoveTowards(m_Weight, goal, Time.deltaTime / m_BlendSeconds)
                : goal;

            if (m_Weight <= 0.0001f)
                return;

            // 押した瞬間の座標を見続ける。カメラが動いても追わない。
            m_Animator.SetLookAtWeight(m_Weight, m_BodyWeight, m_HeadWeight, m_EyesWeight, m_ClampWeight);
            m_Animator.SetLookAtPosition(m_LookPoint);
        }
    }
}
