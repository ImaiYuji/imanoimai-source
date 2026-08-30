using System.Collections.Generic;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// ポーズクリップから「姿勢以外」を取り除いた複製を作る。
    ///
    /// 持ち込んだポーズは VRChat のアバターギミックとして作られているため、
    /// 体の姿勢だけでなく次のものまで書き込んでいる。
    ///
    ///   - 顔と髪の BlendShape        … 表情レイヤーと取り合いになる
    ///   - 髪・スカート等のボーン直接  … ポーズごとに髪型が変わって見える
    ///   - メッシュの表示/非表示      … あるポーズを選ぶと上着が消える
    ///
    /// Anomea 付属の pose_1〜15 はとくに極端で、カーブの約半分が姿勢ではない。
    /// ポーズレイヤーは Write Defaults を切ってあるので、これらは
    /// 「次のポーズが同じカーブを持たないと元に戻らない」形で residue になる。
    /// HANDOFF の「あるポーズで衣装が消え、他に移っても戻らない」がこれ。
    ///
    /// 落とすのは、他の仕組みと担当が重なる 2 つだけにする。
    ///   - BlendShape       … 表情レイヤーの担当
    ///   - メッシュの表示/非表示 … Wardrobe の担当
    ///
    /// 髪やスカートのボーンは落とさない。
    /// これは作者がポーズごとに作り込んだ配置で、寝そべりや腕上げのときに
    /// 髪を体から逃がす役目を持っている。一度これも落としたところ、
    /// 髪がバインドポーズのまま固まって体を貫通するようになった。
    ///
    /// なお作者のデータを持つポーズは 105 件中 8 件しかない。
    /// 残りは髪が固定されたままなので、体に入り込むポーズは残っている。
    /// 揺れものを入れれば解けるが、いまは対応していない。
    ///
    /// 元のクリップは作者の資産なので触らない。複製を作ってそちらを削る。
    /// </summary>
    public static class PoseClipSanitizer
    {
        const string k_GeneratedRoot = "Assets/Anomea/Generated";
        const string k_GeneratedDir = k_GeneratedRoot + "/Poses";

        /// <summary>
        /// 姿勢以外のカーブを持つクリップだけを複製して削り、差し替えた辞書を返す。
        /// もともと姿勢しか持たないクリップは元のまま通す。
        /// </summary>
        public static Dictionary<string, AnimationClip> KeepHumanoidOnly(
            Dictionary<string, AnimationClip> poseClips)
        {
            // 毎回作り直す。元クリップが増減しても取り残しが出ないようにするため。
            if (AssetDatabase.IsValidFolder(k_GeneratedDir))
                AssetDatabase.DeleteAsset(k_GeneratedDir);

            if (!AssetDatabase.IsValidFolder(k_GeneratedRoot))
                AssetDatabase.CreateFolder("Assets/Anomea", "Generated");

            AssetDatabase.CreateFolder(k_GeneratedRoot, "Poses");

            var result = new Dictionary<string, AnimationClip>();
            var report = new StringBuilder();
            var sanitized = 0;
            var removedTotal = 0;

            foreach (var pair in poseClips)
            {
                var clip = pair.Value;

                var floats = AnimationUtility.GetCurveBindings(clip)
                    .Where(b => !IsPosture(b))
                    .ToList();
                var refs = AnimationUtility.GetObjectReferenceCurveBindings(clip).ToList();

                if (floats.Count == 0 && refs.Count == 0)
                {
                    result[pair.Key] = clip;
                    continue;
                }

                var source = AssetDatabase.GetAssetPath(clip);
                var copyPath = $"{k_GeneratedDir}/{clip.name}.anim";

                if (!AssetDatabase.CopyAsset(source, copyPath))
                {
                    // 複製できなければ元のまま使う。余計なカーブは残るが、ポーズは失わない。
                    Debug.LogWarning($"[PoseClipSanitizer] 複製できませんでした: {source}");
                    result[pair.Key] = clip;
                    continue;
                }

                var copy = AssetDatabase.LoadAssetAtPath<AnimationClip>(copyPath);

                foreach (var binding in floats)
                    AnimationUtility.SetEditorCurve(copy, binding, null);

                foreach (var binding in refs)
                    AnimationUtility.SetObjectReferenceCurve(copy, binding, null);

                EditorUtility.SetDirty(copy);

                var removed = floats.Count + refs.Count;
                result[pair.Key] = copy;
                sanitized++;
                removedTotal += removed;
                report.AppendLine($"    {clip.name}: {removed} カーブ削除"
                    + $" (残り {AnimationUtility.GetCurveBindings(copy).Length})");
            }

            AssetDatabase.SaveAssets();

            Debug.Log($"[PoseClipSanitizer] 姿勢だけに削ったポーズ: "
                + $"{sanitized} / {poseClips.Count} 本 (延べ {removedTotal} カーブ削除)\n"
                + report);

            return result;
        }

        /// <summary>
        /// ポーズが持っていてよいカーブか。
        ///
        /// 残すもの:
        ///   - Humanoid のマッスルと Root (Animator にパス無しでぶら下がる)
        ///   - ボーンの Transform (髪・スカート・アクセサリの配置)
        ///
        /// 落とすもの:
        ///   - BlendShape (表情レイヤーと取り合いになる)
        ///   - メッシュの表示/非表示 (Wardrobe と取り合いになる)
        /// </summary>
        static bool IsPosture(EditorCurveBinding binding)
        {
            if (binding.type == typeof(Animator) && string.IsNullOrEmpty(binding.path))
                return true;

            return binding.type == typeof(Transform);
        }
    }
}
