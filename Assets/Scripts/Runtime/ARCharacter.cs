using System.Collections;
using UnityEngine;

namespace ARCharacterApp
{
    /// <summary>
    /// 設置されたキャラクター本体。モーション再生と登場演出を受け持つ。
    ///
    /// モデル側の要件はゆるく作ってある:
    ///  - Animator があれば State 名でモーションを鳴らす
    ///  - Animator が無くても登場演出だけは動く(致命的に壊れない)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARCharacter : MonoBehaviour
    {
        [Header("Animator")]
        [SerializeField] Animator m_Animator;

        [Tooltip("待機モーションの State 名 / Trigger 名")]
        [SerializeField] string m_IdleState = "Idle";

        [Tooltip("タップされたときに再生する Trigger 名")]
        [SerializeField] string m_ReactionTrigger = "Reaction";

        [Header("Entrance")]
        [Tooltip("設置時にスケールを 0 から立ち上げる演出時間(秒)。0 で無効。")]
        [SerializeField] float m_EntranceDuration = 0.45f;

        [SerializeField] AudioSource m_EntranceSound;

        Vector3 m_TargetScale;
        ARCharacterPoser m_Poser;

        void Awake()
        {
            if (m_Animator == null)
                m_Animator = GetComponentInChildren<Animator>();

            m_Poser = GetComponent<ARCharacterPoser>();

            if (m_Animator != null && m_Animator.runtimeAnimatorController == null && m_Poser == null)
            {
                Debug.LogWarning(
                    $"[ARCharacter] '{name}' に Animator Controller が設定されていません。" +
                    "モデルは表示されますが、モーションは再生されません。");
            }

            m_TargetScale = transform.localScale;
        }

        /// <summary>設置された瞬間の登場演出。</summary>
        public void PlayEntrance()
        {
            m_TargetScale = transform.localScale;

            if (m_EntranceSound != null)
                m_EntranceSound.Play();

            if (m_EntranceDuration > 0f)
                StartCoroutine(EntranceRoutine());

            PlayIdle();
        }

        IEnumerator EntranceRoutine()
        {
            var elapsed = 0f;
            transform.localScale = Vector3.zero;

            while (elapsed < m_EntranceDuration)
            {
                elapsed += Time.deltaTime;
                var t = Mathf.Clamp01(elapsed / m_EntranceDuration);

                // 軽くオーバーシュートさせて「ポンッ」と出てくる感じにする
                var eased = 1f - Mathf.Pow(1f - t, 3f);
                var overshoot = 1f + 0.08f * Mathf.Sin(t * Mathf.PI);

                transform.localScale = m_TargetScale * eased * overshoot;
                yield return null;
            }

            transform.localScale = m_TargetScale;
        }

        public void PlayIdle()
        {
            if (m_Animator == null || string.IsNullOrEmpty(m_IdleState))
                return;

            if (HasState(m_IdleState))
                m_Animator.CrossFade(m_IdleState, 0.2f);
        }

        /// <summary>タップされたときのリアクション。</summary>
        public void PlayReaction()
        {
            // リアクション専用のモーションが用意されていればそれを再生する
            if (m_Animator != null && !string.IsNullOrEmpty(m_ReactionTrigger))
            {
                if (HasParameter(m_ReactionTrigger, AnimatorControllerParameterType.Trigger))
                {
                    m_Animator.SetTrigger(m_ReactionTrigger);
                    return;
                }

                if (HasState(m_ReactionTrigger))
                {
                    m_Animator.CrossFade(m_ReactionTrigger, 0.15f);
                    return;
                }
            }

            // 無ければポーズ送りをリアクションの代わりにする。
            // いまのモデルは専用のリアクションを持たないので、実際はこちらが動く。
            if (m_Poser == null)
                m_Poser = GetComponent<ARCharacterPoser>();

            if (m_Poser != null)
                m_Poser.NextPose();
        }

        /// <summary>任意のモーションを名前で再生する(UI のモーション切替ボタン用)。</summary>
        public void PlayMotion(string stateName)
        {
            if (m_Animator == null || string.IsNullOrEmpty(stateName))
                return;

            if (HasState(stateName))
                m_Animator.CrossFade(stateName, 0.2f);
            else
                Debug.LogWarning($"[ARCharacter] State '{stateName}' が Animator に見つかりません。");
        }

        bool HasState(string stateName)
        {
            // AnimatorController が未設定のモデル(インポート直後の VRM など)では
            // HasState を呼べないので先に弾く。
            if (m_Animator.runtimeAnimatorController == null)
                return false;

            // レイヤー 0 のみ見る。多層構成なら必要に応じて拡張。
            return m_Animator.HasState(0, Animator.StringToHash(stateName));
        }

        bool HasParameter(string parameterName, AnimatorControllerParameterType type)
        {
            foreach (var p in m_Animator.parameters)
            {
                if (p.type == type && p.name == parameterName)
                    return true;
            }
            return false;
        }
    }
}
