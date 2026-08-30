using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARCharacterApp
{
    /// <summary>
    /// Animator に登録された Humanoid のポーズクリップを切り替える。
    ///
    /// 以前はマッスル値を直接書き換えていたが、その方式だと
    /// モデル付属の正規のポーズが使えず、姿勢も破綻しやすかった。
    /// モデル側にポーズクリップが揃っているなら、それを再生するほうが
    /// 作者の意図した見た目になり、衣装のズレも起きない。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARCharacterPoser : MonoBehaviour
    {
        [Serializable]
        public struct PoseEntry
        {
            [Tooltip("Animator Controller 上の State 名")]
            public string stateName;

            [Tooltip("UI のボタンに出す名前")]
            public string displayName;
        }

        [SerializeField] Animator m_Animator;

        [Tooltip("選択できるポーズ(全身)。Animator のレイヤー 0 で再生する。")]
        [SerializeField] PoseEntry[] m_Poses = Array.Empty<PoseEntry>();

        [Tooltip("選択できる表情。BlendShape だけを動かすので、レイヤー 1 でポーズと同時に再生できる。")]
        [SerializeField] PoseEntry[] m_Expressions = Array.Empty<PoseEntry>();

        [Tooltip("切り替えるときの補間時間(秒)。長くすると中間姿勢の粗が見えやすくなる。")]
        [SerializeField] float m_BlendDuration = 0.18f;

        int m_CurrentIndex = -1;
        int m_CurrentExpression = -1;
        bool m_Ready;

        // ポーズクリップの一部は衣装メッシュの表示/非表示まで動かす。
        // 元の状態を覚えておいて、ポーズを変えるたびに戻せるようにする。
        Renderer[] m_Renderers = Array.Empty<Renderer>();
        bool[] m_RendererEnabled = Array.Empty<bool>();
        bool[] m_RendererActive = Array.Empty<bool>();

        /// <summary>着せ替えの希望状態。あればこちらを優先して戻す。</summary>
        ARCharacterWardrobe m_Wardrobe;

        public IReadOnlyList<PoseEntry> Poses => m_Poses;
        public IReadOnlyList<PoseEntry> Expressions => m_Expressions;
        public int CurrentIndex => m_CurrentIndex;
        public int CurrentExpressionIndex => m_CurrentExpression;

        void Awake()
        {
            if (m_Animator == null)
                m_Animator = GetComponentInChildren<Animator>();

            if (m_Animator == null)
            {
                Debug.LogWarning("[ARCharacterPoser] Animator が見つからないため、ポーズ機能は無効です。");
                enabled = false;
                return;
            }

            if (m_Animator.runtimeAnimatorController == null)
            {
                Debug.LogWarning("[ARCharacterPoser] Animator Controller が未設定のため、ポーズ機能は無効です。");
                enabled = false;
                return;
            }

            // ポーズは静止画に近い 1 フレームのクリップなので、
            // 画面外に出たからと更新を止められると姿勢が崩れたままになる。
            m_Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

            CaptureRendererState();

            m_Ready = true;
        }

        void CaptureRendererState()
        {
            m_Wardrobe = GetComponent<ARCharacterWardrobe>();
            m_Renderers = GetComponentsInChildren<Renderer>(true);
            m_RendererEnabled = new bool[m_Renderers.Length];
            m_RendererActive = new bool[m_Renderers.Length];

            for (var i = 0; i < m_Renderers.Length; i++)
            {
                m_RendererEnabled[i] = m_Renderers[i].enabled;
                m_RendererActive[i] = m_Renderers[i].gameObject.activeSelf;
            }
        }

        /// <summary>
        /// 衣装メッシュの表示状態を元に戻す。
        ///
        /// ポーズクリップの中には、衣装を脱がせるために
        /// メッシュの有効/無効を書き換えるものがある。
        /// Write Defaults を切っている都合上、次のポーズがその値を持っていないと
        /// 消えたままになってしまうので、切り替えの直前に必ず戻す。
        /// そのポーズ自身が消す指定を持っていれば、このあと Animator が上書きする。
        /// </summary>
        void RestoreRenderers()
        {
            for (var i = 0; i < m_Renderers.Length; i++)
            {
                if (m_Renderers[i] == null)
                    continue;

                m_Renderers[i].enabled = m_RendererEnabled[i];

                var go = m_Renderers[i].gameObject;
                if (go.activeSelf != m_RendererActive[i])
                    go.SetActive(m_RendererActive[i]);
            }

            // 着せ替えで脱がせている分は、そちらの希望を優先する
            if (m_Wardrobe != null)
                m_Wardrobe.Apply();
        }

        void Start()
        {
            if (!m_Ready)
                return;

            if (m_Poses.Length > 0)
                SetPose(0, immediate: true);

            if (m_Expressions.Length > 0)
                SetExpression(0, immediate: true);
        }

        /// <summary>ポーズ(全身)を切り替える。</summary>
        public void SetPose(int index, bool immediate = false)
        {
            // 前のポーズが衣装を消していることがあるので、先に戻しておく
            RestoreRenderers();

            if (Play(m_Poses, index, layer: 0, immediate))
                m_CurrentIndex = index;
        }

        /// <summary>
        /// 表情を切り替える。
        ///
        /// レイヤー 1 が素の顔を作り続け、レイヤー 2 に選んだ表情を乗せる構成。
        /// 表情クリップが持たない BlendShape はレイヤー 1 の値が出るので、
        /// 前に選んだ表情が混ざらず、きちんと「切り替え」になる。
        /// </summary>
        public void SetExpression(int index, bool immediate = false)
        {
            const int expressionLayer = 2;

            if (!m_Ready || m_Animator.layerCount <= expressionLayer)
                return;

            if (Play(m_Expressions, index, expressionLayer, immediate))
            {
                m_Animator.SetLayerWeight(expressionLayer, 1f);
                m_CurrentExpression = index;
            }
        }

        bool Play(PoseEntry[] entries, int index, int layer, bool immediate)
        {
            if (!m_Ready || entries == null || index < 0 || index >= entries.Length)
                return false;

            var stateName = entries[index].stateName;
            if (string.IsNullOrEmpty(stateName))
                return false;

            if (layer >= m_Animator.layerCount)
            {
                Debug.LogWarning($"[ARCharacterPoser] レイヤー {layer} がありません。");
                return false;
            }

            var hash = Animator.StringToHash(stateName);
            if (!m_Animator.HasState(layer, hash))
            {
                Debug.LogWarning($"[ARCharacterPoser] State '{stateName}' がレイヤー {layer} に見つかりません。");
                return false;
            }

            if (immediate || m_BlendDuration <= 0f)
                m_Animator.Play(hash, layer, 0f);
            else
                m_Animator.CrossFadeInFixedTime(hash, m_BlendDuration, layer, 0f);

            return true;
        }

        /// <summary>次のポーズへ(キャラクターをタップしたときに呼ぶ)。</summary>
        public void NextPose()
        {
            if (m_Poses.Length == 0)
                return;

            SetPose((m_CurrentIndex + 1) % m_Poses.Length);
        }
    }
}
