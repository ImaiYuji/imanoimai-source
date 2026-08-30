using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// マテリアルの参照切れテクスチャを、同じフォルダにある実体で埋め直す。
    ///
    /// 持ち込んだ preset_1 の一部マテリアルは、_MainTex が元プロジェクトにも
    /// 存在しない GUID を指していた(服が正しく貼られていなかった原因)。
    /// テクスチャ本体はフォルダに揃っているので、名前から推測して繋ぎ直す。
    /// </summary>
    public static class MaterialTextureRepair
    {
        const string k_MaterialRoot = "Assets/Anomea/Materials";

        /// <summary>
        /// スロットごとに、テクスチャ名がどんな特徴を持つかの手がかり。
        ///
        /// <c>allowSingleFallback</c> は「候補が 1 枚しか残らなければそれを使う」かどうか。
        /// ベースカラーはそれで当たるが、法線やアウトラインマスクで同じことをすると
        /// 関係のない絵を掴んでしまい、かえって表示が壊れる
        /// (アウトラインマスクに色テクスチャが入り、透過が破綻したことがある)。
        /// </summary>
        static readonly (string slot, string[] prefer, string[] avoid, bool allowSingleFallback)[] k_Slots =
        {
            // ベースカラー: 法線・アウトライン・マスク以外の、いちばん素直な名前
            ("_MainTex", new[] { "set1", "base", "color" },
                         new[] { "_nm", "normal", "outline", "mask", "_mm", "matcap" }, true),

            ("_BumpMap", new[] { "_nm", "normal" },
                         new[] { "_nm_2", "outline" }, false),

            ("_OutlineWidthMask", new[] { "outline" },
                                  new[] { "normal", "_nm" }, false),
        };

        [MenuItem("AR Character App/マテリアルのテクスチャ切れを修復", priority = 62)]
        public static void Repair()
        {
            var report = new StringBuilder();
            report.AppendLine("=== マテリアルのテクスチャ修復 ===");

            var repaired = 0;
            var checkedCount = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { k_MaterialRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null)
                    continue;

                checkedCount++;

                var folder = Path.GetDirectoryName(path)?.Replace('\\', '/');
                if (string.IsNullOrEmpty(folder))
                    continue;

                var candidates = LoadTexturesIn(folder);
                if (candidates.Count == 0)
                    continue;

                var changed = false;

                foreach (var (slot, prefer, avoid, allowSingleFallback) in k_Slots)
                {
                    if (!material.HasProperty(slot))
                        continue;

                    var current = material.GetTexture(slot);

                    if (current != null)
                    {
                        // 過去の修復で見当違いの絵が入っていることがある。
                        // 明らかにそのスロット向きでないものは外す。
                        var name = current.name.ToLowerInvariant();
                        var wrong = avoid.Any(a => name.Contains(a))
                                    || (prefer.Length > 0 && !prefer.Any(p => name.Contains(p)));

                        if (!wrong || allowSingleFallback)
                            continue;

                        material.SetTexture(slot, null);
                        changed = true;
                        report.AppendLine($"  {material.name,-28} {slot,-18} 解除 ({current.name})");
                        continue;
                    }

                    var pick = Pick(candidates, prefer, avoid, allowSingleFallback);
                    if (pick == null)
                        continue;

                    material.SetTexture(slot, pick);
                    changed = true;

                    report.AppendLine($"  {material.name,-28} {slot,-18} <- {pick.name}");
                }

                if (changed)
                {
                    EditorUtility.SetDirty(material);
                    repaired++;
                }
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.AppendLine();
            report.AppendLine($"{checkedCount} 個を確認し、{repaired} 個を修復しました。");

            if (repaired > 0)
                Debug.Log(report.ToString());
            else
                Debug.Log(report + "  参照切れは見つかりませんでした。");
        }

        static List<Texture2D> LoadTexturesIn(string folder)
        {
            return AssetDatabase.FindAssets("t:Texture2D", new[] { folder })
                .Select(AssetDatabase.GUIDToAssetPath)
                // サブフォルダまで拾うと別パーツの絵を掴むので、直下だけに限る
                .Where(p => string.Equals(Path.GetDirectoryName(p)?.Replace('\\', '/'), folder,
                    System.StringComparison.Ordinal))
                .Select(AssetDatabase.LoadAssetAtPath<Texture2D>)
                .Where(t => t != null)
                .ToList();
        }

        /// <summary>
        /// 名前の手がかりから、そのスロットに合いそうなテクスチャを選ぶ。
        /// 候補が 1 枚しかなくベースカラーを探している場合は、それを使う。
        /// </summary>
        static Texture2D Pick(List<Texture2D> candidates, string[] prefer, string[] avoid,
            bool allowSingleFallback)
        {
            var filtered = candidates
                .Where(t => !avoid.Any(a => t.name.ToLowerInvariant().Contains(a)))
                .ToList();

            if (filtered.Count == 0)
                return null;

            var preferred = filtered
                .FirstOrDefault(t => prefer.Any(p => t.name.ToLowerInvariant().Contains(p)));

            if (preferred != null)
                return preferred;

            // 手がかりに当たらない場合、当てずっぽうで入れると表示が壊れる。
            // ベースカラーのように「1 枚しかないならそれで確実」なスロットだけ許す。
            return allowSingleFallback && filtered.Count == 1 ? filtered[0] : null;
        }
    }
}
