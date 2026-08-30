using System;
using System.Collections.Generic;
using UnityEngine;

namespace ARCharacterApp
{
    /// <summary>
    /// 衣装パーツの表示/非表示をまとめて扱う。
    ///
    /// ポーズクリップの中には衣装メッシュを消すものがあり、
    /// Write Defaults を切っている都合上、放っておくと消えたまま戻らない。
    /// ここで「こう見せたい」状態を持っておき、ポーズが切り替わるたびに当て直す。
    ///
    /// あわせて、服の下で体を細らせる BlendShape(Shrink 系)も面倒を見る。
    /// アウターを脱いだのに上腕が細らせたままだと、痩せた腕がそのまま出てしまう。
    /// 正しい値は作者が決めたものなので決め打ちせず、起動時の値を覚えておいて
    /// 「脱いだら 0 / 着たら元の値」に戻す。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARCharacterWardrobe : MonoBehaviour
    {
        [Serializable]
        public struct Part
        {
            [Tooltip("UI のボタンに出す名前")]
            public string displayName;

            [Tooltip("対象メッシュの名前(FBX 内の SkinnedMeshRenderer 名)")]
            public string rendererName;

            [Tooltip("最初から着せておくか")]
            public bool defaultOn;

            [Tooltip("脱いだときに 0 に戻す BlendShape 名。" +
                     "服の下で体を細らせる Shrink 系がこれに当たる。" +
                     "着せ直すと起動時の値に戻る")]
            public string[] shrinkShapes;
        }

        [SerializeField] Part[] m_Parts = Array.Empty<Part>();

        /// <summary>いま着せている状態。ポーズが消してきても、これに戻す。</summary>
        bool[] m_On = Array.Empty<bool>();
        Renderer[] m_Targets = Array.Empty<Renderer>();

        /// <summary>着ているときの BlendShape の値。起動時に控えておく。</summary>
        readonly struct ShapeTarget
        {
            public readonly SkinnedMeshRenderer Renderer;
            public readonly int Index;
            public readonly float WornWeight;

            public ShapeTarget(SkinnedMeshRenderer renderer, int index, float wornWeight)
            {
                Renderer = renderer;
                Index = index;
                WornWeight = wornWeight;
            }
        }

        List<ShapeTarget>[] m_Shapes = Array.Empty<List<ShapeTarget>>();

        public IReadOnlyList<Part> Parts => m_Parts;

        void Awake()
        {
            m_On = new bool[m_Parts.Length];
            m_Targets = new Renderer[m_Parts.Length];
            m_Shapes = new List<ShapeTarget>[m_Parts.Length];

            var renderers = GetComponentsInChildren<Renderer>(true);
            var skinned = GetComponentsInChildren<SkinnedMeshRenderer>(true);

            for (var i = 0; i < m_Parts.Length; i++)
            {
                m_On[i] = m_Parts[i].defaultOn;

                foreach (var renderer in renderers)
                {
                    if (renderer.name == m_Parts[i].rendererName)
                    {
                        m_Targets[i] = renderer;
                        break;
                    }
                }

                if (m_Targets[i] == null)
                {
                    Debug.LogWarning($"[ARCharacterWardrobe] メッシュ '{m_Parts[i].rendererName}' が見つかりません。");
                }

                m_Shapes[i] = CollectShapes(m_Parts[i].shrinkShapes, skinned);
            }

            Apply();
        }

        /// <summary>
        /// 名前の合う BlendShape を全メッシュから拾い、いまの値を控える。
        /// 同じ名前のシェイプが複数のメッシュにあることがあるので、見つかった分だけ全部持つ。
        /// </summary>
        static List<ShapeTarget> CollectShapes(string[] names, SkinnedMeshRenderer[] renderers)
        {
            var found = new List<ShapeTarget>();

            if (names == null || names.Length == 0)
                return found;

            foreach (var name in names)
            {
                if (string.IsNullOrEmpty(name))
                    continue;

                var hit = false;

                foreach (var renderer in renderers)
                {
                    var mesh = renderer.sharedMesh;
                    if (mesh == null)
                        continue;

                    var index = mesh.GetBlendShapeIndex(name);
                    if (index < 0)
                        continue;

                    found.Add(new ShapeTarget(renderer, index, renderer.GetBlendShapeWeight(index)));
                    hit = true;
                }

                if (!hit)
                    Debug.LogWarning($"[ARCharacterWardrobe] BlendShape '{name}' が見つかりません。");
            }

            return found;
        }

        public bool IsOn(int index)
            => index >= 0 && index < m_On.Length && m_On[index];

        /// <summary>着る / 脱ぐを切り替える。</summary>
        public void Toggle(int index)
        {
            if (index < 0 || index >= m_On.Length)
                return;

            m_On[index] = !m_On[index];
            Apply();
        }

        /// <summary>
        /// いまの希望どおりに見せ直す。
        /// ポーズを切り替えた直後にも呼ぶ(ポーズが勝手に脱がせることがあるため)。
        /// </summary>
        public void Apply()
        {
            for (var i = 0; i < m_Targets.Length; i++)
            {
                if (m_Targets[i] == null)
                    continue;

                m_Targets[i].enabled = m_On[i];

                // メッシュごと無効化されている場合もあるので、そちらも戻す
                var go = m_Targets[i].gameObject;
                if (!go.activeSelf)
                    go.SetActive(true);
            }

            // 服の下で体を細らせているぶんを、脱いだときだけ戻す
            for (var i = 0; i < m_Shapes.Length; i++)
            {
                if (m_Shapes[i] == null)
                    continue;

                foreach (var shape in m_Shapes[i])
                {
                    if (shape.Renderer == null)
                        continue;

                    shape.Renderer.SetBlendShapeWeight(
                        shape.Index, m_On[i] ? shape.WornWeight : 0f);
                }
            }
        }
    }
}
