using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// 顔に重なる Overlay を描かないようにする。
    ///
    /// この Overlay は Body メッシュのサブメッシュとして入っていて、
    /// 赤面や暗い表情のときに顔の上へ乗る作りになっている。
    /// ところが実機では、抜けるはずの部分まで不透明な面として出てしまい、
    /// 顔に茶色やピンクの帯がかかる。原因を追い切れていないため、
    /// いったん描画そのものを止めておく。
    ///
    /// 作者のマテリアルは触らない。生成した透明マテリアルを
    /// プレハブの該当スロットにだけ割り当てる。
    /// 戻したくなったら ARRebuildAll からこの呼び出しを外せばよい。
    /// </summary>
    public static class OverlayHider
    {
        const string k_PrefabPath = "Assets/Prefabs/Character.prefab";
        const string k_GeneratedRoot = "Assets/Anomea/Generated";
        const string k_GeneratedDir = k_GeneratedRoot + "/Materials";
        const string k_MaterialPath = k_GeneratedDir + "/Invisible.mat";
        const string k_TexturePath = k_GeneratedDir + "/Invisible.png";

        /// <summary>非表示にしたいマテリアル名(接頭辞で判定)。</summary>
        static readonly string[] k_HideTargets = { "Anomea_Overlay" };

        [MenuItem("AR Character App/Overlay を非表示にする", priority = 66)]
        public static void Hide()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath);
            if (prefab == null)
            {
                Debug.LogWarning($"[OverlayHider] {k_PrefabPath} がありません。先にキャラクターを生成してください。");
                return;
            }

            var invisible = GetOrCreateInvisibleMaterial();
            if (invisible == null)
                return;

            var report = new StringBuilder();
            report.AppendLine("=== Overlay の非表示 ===");

            var contents = PrefabUtility.LoadPrefabContents(k_PrefabPath);
            var replaced = 0;

            try
            {
                foreach (var renderer in contents.GetComponentsInChildren<Renderer>(true))
                {
                    var materials = renderer.sharedMaterials;
                    var changed = false;

                    for (var i = 0; i < materials.Length; i++)
                    {
                        var material = materials[i];
                        if (material == null)
                            continue;

                        if (!k_HideTargets.Any(n => material.name.StartsWith(n, System.StringComparison.Ordinal)))
                            continue;

                        report.AppendLine($"  {renderer.name}[{i}] {material.name} -> Invisible");
                        materials[i] = invisible;
                        changed = true;
                        replaced++;
                    }

                    if (changed)
                        renderer.sharedMaterials = materials;
                }

                if (replaced > 0)
                    PrefabUtility.SaveAsPrefabAsset(contents, k_PrefabPath);
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(contents);
            }

            report.AppendLine($"  {replaced} スロットを差し替えました。");
            Debug.Log(report.ToString());
        }

        /// <summary>
        /// 何も描かない材質を用意する。
        /// lilToon が効いているかに関係なく消えてほしいので、
        /// Unity 組み込みの Unlit/Transparent に完全透明のテクスチャを当てる。
        /// </summary>
        static Material GetOrCreateInvisibleMaterial()
        {
            EnsureFolder();

            var existing = AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
            if (existing != null)
                return existing;

            var texture = AssetDatabase.LoadAssetAtPath<Texture2D>(k_TexturePath);
            if (texture == null)
            {
                var generated = new Texture2D(1, 1, TextureFormat.RGBA32, false);
                generated.SetPixel(0, 0, new Color(0f, 0f, 0f, 0f));
                generated.Apply();
                File.WriteAllBytes(k_TexturePath, generated.EncodeToPNG());
                Object.DestroyImmediate(generated);

                AssetDatabase.ImportAsset(k_TexturePath, ImportAssetOptions.ForceUpdate);

                if (AssetImporter.GetAtPath(k_TexturePath) is TextureImporter importer)
                {
                    importer.alphaIsTransparency = true;
                    importer.textureCompression = TextureImporterCompression.Uncompressed;
                    importer.SaveAndReimport();
                }

                texture = AssetDatabase.LoadAssetAtPath<Texture2D>(k_TexturePath);
            }

            var shader = Shader.Find("Unlit/Transparent");
            if (shader == null)
            {
                Debug.LogError("[OverlayHider] Unlit/Transparent が見つかりません。");
                return null;
            }

            var material = new Material(shader) { name = "Invisible" };
            material.SetTexture("_MainTex", texture);

            AssetDatabase.CreateAsset(material, k_MaterialPath);
            AssetDatabase.SaveAssets();

            return AssetDatabase.LoadAssetAtPath<Material>(k_MaterialPath);
        }

        static void EnsureFolder()
        {
            if (!AssetDatabase.IsValidFolder(k_GeneratedRoot))
                AssetDatabase.CreateFolder("Assets/Anomea", "Generated");

            if (!AssetDatabase.IsValidFolder(k_GeneratedDir))
                AssetDatabase.CreateFolder(k_GeneratedRoot, "Materials");
        }
    }
}
