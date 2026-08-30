using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// Overlay のアトラスから「不透明な余白」を抜いた複製を作り、そちらを参照させる。
    ///
    /// 症状: 赤面や暗い表情を選ぶと、顔に茶色やピンクの帯が乗る。
    ///
    /// 原因はブレンドでもテクスチャ圧縮でもなく、アトラスそのものだった。
    /// 絵の描かれていない余白が、透明ではなく不透明な灰色 (147,147,147,255) で
    /// 塗られている。Overlay の板がその余白を拾うと、灰色の面がそのまま顔に乗る。
    /// アルファが 255 なので、Cutout でも Transparent でも抜けない。
    ///
    /// 元のテクスチャは作者の資産なので触らない。余白だけ抜いた複製を作る。
    /// </summary>
    public static class OverlayTextureAlphaFix
    {
        const string k_GeneratedRoot = "Assets/Anomea/Generated";
        const string k_GeneratedDir = k_GeneratedRoot + "/Textures";
        const string k_MaterialRoot = "Assets/Anomea/Materials";

        static readonly string[] k_OverlayNames = { "Anomea_Overlay" };

        /// <summary>余白とみなす色の許容差(0-255)。圧縮前の原本なので小さくてよい。</summary>
        const int k_Tolerance = 2;

        [MenuItem("AR Character App/Overlay の余白を抜く", priority = 65)]
        public static void Fix()
        {
            var report = new StringBuilder();
            report.AppendLine("=== Overlay アトラスの余白抜き ===");

            EnsureFolder();

            // 同じテクスチャを複数のマテリアルが共有しているので、まず対象を集める
            var materials = CollectOverlayMaterials();
            if (materials.Count == 0)
            {
                Debug.LogWarning("[OverlayTextureAlphaFix] 対象の Overlay マテリアルがありません。");
                return;
            }

            var replaced = new Dictionary<string, Texture2D>();

            foreach (var material in materials)
            {
                if (!material.HasProperty("_MainTex"))
                    continue;

                var texture = material.GetTexture("_MainTex");
                if (texture == null)
                    continue;

                var source = AssetDatabase.GetAssetPath(texture);

                // すでに差し替え済みならそのまま
                if (source.StartsWith(k_GeneratedDir, System.StringComparison.Ordinal))
                    continue;

                if (!replaced.TryGetValue(source, out var generated))
                {
                    generated = BuildTransparentCopy(source, report);
                    replaced[source] = generated;
                }

                if (generated != null)
                {
                    material.SetTexture("_MainTex", generated);
                    EditorUtility.SetDirty(material);
                    report.AppendLine($"  {material.name,-26} _MainTex -> {generated.name}");
                }
            }

            AssetDatabase.SaveAssets();
            Debug.Log(report.ToString());
        }

        static List<Material> CollectOverlayMaterials()
        {
            return AssetDatabase.FindAssets("t:Material", new[] { k_MaterialRoot })
                .Select(AssetDatabase.GUIDToAssetPath)
                .Select(AssetDatabase.LoadAssetAtPath<Material>)
                .Where(m => m != null && k_OverlayNames.Any(
                    n => m.name.StartsWith(n, System.StringComparison.Ordinal)))
                .ToList();
        }

        /// <summary>
        /// 余白を透明にした複製を書き出す。
        /// 余白の色は「不透明な画素のうち最も多い色」で決める。
        /// アトラスの余白は一色で塗り潰されているので、これで狙いどおり当たる。
        /// </summary>
        static Texture2D BuildTransparentCopy(string sourcePath, StringBuilder report)
        {
            // インポート設定(圧縮・リサイズ)を経由しない生の画素が欲しいので、
            // ファイルを直接読んで展開する。
            var bytes = File.ReadAllBytes(sourcePath);
            var texture = new Texture2D(2, 2, TextureFormat.RGBA32, false);

            if (!texture.LoadImage(bytes))
            {
                Debug.LogError($"[OverlayTextureAlphaFix] 読み込めません: {sourcePath}");
                return null;
            }

            var pixels = texture.GetPixels32();

            // 不透明な画素の色を数える
            var counts = new Dictionary<int, int>();
            foreach (var p in pixels)
            {
                if (p.a != 255)
                    continue;

                var key = (p.r << 16) | (p.g << 8) | p.b;
                counts.TryGetValue(key, out var n);
                counts[key] = n + 1;
            }

            if (counts.Count == 0)
            {
                report.AppendLine($"  {Path.GetFileName(sourcePath)}: 不透明な画素なし。そのまま。");
                return null;
            }

            var top = counts.OrderByDescending(kv => kv.Value).First();
            var br = (byte)((top.Key >> 16) & 0xFF);
            var bg = (byte)((top.Key >> 8) & 0xFF);
            var bb = (byte)(top.Key & 0xFF);

            var cleared = 0;
            for (var i = 0; i < pixels.Length; i++)
            {
                var p = pixels[i];
                if (p.a != 255)
                    continue;

                if (Mathf.Abs(p.r - br) > k_Tolerance) continue;
                if (Mathf.Abs(p.g - bg) > k_Tolerance) continue;
                if (Mathf.Abs(p.b - bb) > k_Tolerance) continue;

                p.a = 0;
                pixels[i] = p;
                cleared++;
            }

            texture.SetPixels32(pixels);
            texture.Apply();

            var name = Path.GetFileNameWithoutExtension(sourcePath) + "_cutbg.png";
            var outPath = $"{k_GeneratedDir}/{name}";
            File.WriteAllBytes(outPath, texture.EncodeToPNG());
            Object.DestroyImmediate(texture);

            AssetDatabase.ImportAsset(outPath, ImportAssetOptions.ForceUpdate);
            ApplyImportSettings(outPath);

            var pct = 100f * cleared / pixels.Length;
            report.AppendLine($"  {Path.GetFileName(sourcePath)}: 余白 RGB({br},{bg},{bb}) を "
                + $"{cleared} 画素 ({pct:0.0}%) 透明化 -> {name}");

            return AssetDatabase.LoadAssetAtPath<Texture2D>(outPath);
        }

        /// <summary>複製にも本体と同じ取り込み設定を当てる(アルファを保つため)。</summary>
        static void ApplyImportSettings(string path)
        {
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                return;

            importer.alphaIsTransparency = true;
            importer.crunchedCompression = false;
            importer.maxTextureSize = 2048;

            var android = importer.GetPlatformTextureSettings("Android");
            android.name = "Android";
            android.overridden = true;
            android.format = TextureImporterFormat.ASTC_6x6;
            android.maxTextureSize = 2048;
            android.compressionQuality = 100;
            android.crunchedCompression = false;
            importer.SetPlatformTextureSettings(android);

            importer.SaveAndReimport();
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(k_GeneratedRoot))
                AssetDatabase.CreateFolder("Assets/Anomea", "Generated");

            if (!AssetDatabase.IsValidFolder(k_GeneratedDir))
                AssetDatabase.CreateFolder(k_GeneratedRoot, "Textures");
        }
    }
}
