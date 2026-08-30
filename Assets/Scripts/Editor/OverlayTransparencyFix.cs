using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// Overlay 系マテリアルの透過を直す。
    ///
    /// 症状: 顔や体に重ねる Overlay が、本来アルファで抜けるはずの部分まで
    /// 塗りつぶされて板のように見える。
    ///
    /// 原因は 3 つあった。
    ///  1. Overlay_S が不透明シェーダー(lilToon / DstBlend=0)のまま
    ///     アルファ付きテクスチャを使っていた
    ///  2. 半透明側の Overlay が ZWrite を書いており、重なりの描画順が崩れていた
    ///  3. テクスチャが Crunch 圧縮で、アルファの縁が潰れていた
    ///
    /// 当初は 1 を Cutout で塞いだが、それでは足りなかった。
    /// Cutout はアルファを閾値で二値化するため、赤面や影のような
    /// なだらかなアルファを持つ Overlay が、そのままソリッドな色の板になる
    /// (顔に茶色やピンクの帯が乗る)。半透明でないと表現できないので、
    /// いまは Transparent に寄せて、代わりに ZWrite を切って描画順を保つ。
    /// </summary>
    public static class OverlayTransparencyFix
    {
        const string k_MaterialRoot = "Assets/Anomea/Materials";

        /// <summary>抜きを効かせたいマテリアル名(接頭辞で判定)。</summary>
        static readonly string[] k_OverlayNames = { "Anomea_Overlay" };

        [MenuItem("AR Character App/Overlay の透過を修復", priority = 64)]
        public static void Fix()
        {
            var report = new StringBuilder();
            report.AppendLine("=== Overlay の透過修復 ===");

            var transparent = Shader.Find("Hidden/lilToonTransparent");
            if (transparent == null)
            {
                Debug.LogError("[OverlayTransparencyFix] Hidden/lilToonTransparent が見つかりません。");
                return;
            }

            var fixedCount = 0;

            foreach (var guid in AssetDatabase.FindAssets("t:Material", new[] { k_MaterialRoot }))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var material = AssetDatabase.LoadAssetAtPath<Material>(path);

                if (material == null || !k_OverlayNames.Any(n => material.name.StartsWith(n, System.StringComparison.Ordinal)))
                    continue;

                var changed = false;
                var shaderName = material.shader != null ? material.shader.name : "";

                // 1. 不透明や Cutout のままの Overlay は半透明にする。
                //
                //    Cutout ではアルファが閾値で二値化されるため、
                //    赤面や影のようになだらかなアルファを持つ Overlay が
                //    ソリッドな板になってしまう。アルファをそのまま出すには
                //    Transparent しかない。
                if (shaderName == "lilToon" || shaderName == "Hidden/lilToonCutout")
                {
                    material.shader = transparent;

                    // ブレンドは作者が設定した相方(Anomea_Overlay)に揃える。
                    // 同じ絵を同じシェーダーで出しているので、揃えないほうが不自然。
                    // lilToon の半透明は乗算済みアルファ(One / OneMinusSrcAlpha)。
                    SetIfExists(material, "_SrcBlend", 1f);
                    SetIfExists(material, "_DstBlend", 10f);

                    // 閾値では抜かない。完全に透明な画素だけ落ちれば十分。
                    SetIfExists(material, "_Cutoff", 0.001f);

                    // 深度は書かない(下の 2 と同じ理由)。
                    // 描画順はシェーダーの既定に任せる(-1)。相方もそうなっている。
                    SetIfExists(material, "_ZWrite", 0f);
                    material.renderQueue = -1;

                    changed = true;
                    report.AppendLine($"  {material.name,-26} {shaderName} -> Hidden/lilToonTransparent");
                }

                // 2. 半透明になった Overlay の設定を揃える。
                //    ここは変換直後のマテリアルにも、もとから半透明だったものにも効かせたいので
                //    変換前の名前ではなく、いまのシェーダーを見る。
                if (material.shader != null && material.shader.name.Contains("Transparent"))
                {
                    // 深度は書かない。書くと後ろの重なりが消える。
                    if (material.HasProperty("_ZWrite") && material.GetFloat("_ZWrite") != 0f)
                    {
                        material.SetFloat("_ZWrite", 0f);
                        changed = true;
                        report.AppendLine($"  {material.name,-26} _ZWrite -> 0");
                    }

                    // lilToon の半透明は乗算済みアルファ。作者の設定に揃える。
                    if (material.HasProperty("_SrcBlend") && material.GetFloat("_SrcBlend") != 1f)
                    {
                        material.SetFloat("_SrcBlend", 1f);
                        changed = true;
                        report.AppendLine($"  {material.name,-26} _SrcBlend -> 1");
                    }

                    // 描画順はシェーダーの既定に任せる。
                    // Overlay 同士で違う値になっていると重なりが安定しない。
                    if (material.renderQueue != material.shader.renderQueue)
                    {
                        report.AppendLine($"  {material.name,-26} queue {material.renderQueue} -> 既定({material.shader.renderQueue})");
                        material.renderQueue = -1;
                        changed = true;
                    }
                }

                if (changed)
                    EditorUtility.SetDirty(material);

                // 3. アルファの縁が潰れるので Crunch 圧縮を外す
                if (FixTexture(material, report))
                    changed = true;

                if (changed)
                    fixedCount++;
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            report.AppendLine();
            report.AppendLine($"{fixedCount} 個のマテリアルを調整しました。");
            Debug.Log(report.ToString());

            // シェーダーとブレンドを直しても、アトラスの余白が不透明なままだと
            // 顔に灰色の面が乗る。余白を抜いた複製に差し替える。
            OverlayTextureAlphaFix.Fix();
        }

        /// <summary>
        /// Crunch 圧縮はアルファの階調が荒れる。
        /// Overlay は薄い縁で重ねる絵なので、ここが潰れると輪郭が板に見える。
        /// </summary>
        static bool FixTexture(Material material, StringBuilder report)
        {
            if (!material.HasProperty("_MainTex"))
                return false;

            var texture = material.GetTexture("_MainTex");
            if (texture == null)
                return false;

            var path = AssetDatabase.GetAssetPath(texture);
            if (AssetImporter.GetAtPath(path) is not TextureImporter importer)
                return false;

            var changed = false;

            if (importer.crunchedCompression)
            {
                importer.crunchedCompression = false;
                changed = true;
            }

            if (!importer.alphaIsTransparency)
            {
                importer.alphaIsTransparency = true;
                changed = true;
            }

            // Android は ETC2 だとアルファが粗い。ASTC のほうが階調を保てる。
            //
            // ここは必ず RGBA 版を指定すること。
            // TextureImporterFormat.ASTC_6x6 は RGB 版(列挙値 50)で、アルファを捨てる。
            // それに気づかず指定していたため、アルファを守るはずの処理が
            // 逆にアルファを消し、Overlay が顔の上にソリッドな板として出ていた。
            const TextureImporterFormat astcWithAlpha = TextureImporterFormat.ASTC_RGBA_6x6;

            var android = importer.GetPlatformTextureSettings("Android");
            if (!android.overridden || android.format != astcWithAlpha)
            {
                android.name = "Android";
                android.overridden = true;
                android.format = astcWithAlpha;
                android.maxTextureSize = 2048;
                android.compressionQuality = 100;
                importer.SetPlatformTextureSettings(android);
                changed = true;
            }

            if (changed)
            {
                importer.SaveAndReimport();
                report.AppendLine($"    テクスチャ {texture.name}: Crunch 解除 / ASTC 6x6");
            }

            return changed;
        }

        static void SetIfExists(Material material, string property, float value)
        {
            if (material.HasProperty(property))
                material.SetFloat(property, value);
        }
    }
}
