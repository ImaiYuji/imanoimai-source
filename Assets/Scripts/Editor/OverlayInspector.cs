using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// Overlay まわりのマテリアルと、それを使っているメッシュを洗い出す調査用ツール。
    /// 透過の不具合を追うために、描画順・ブレンド設定・アルファの有無をまとめて見る。
    /// </summary>
    public static class OverlayInspector
    {
        const string k_PrefabPath = "Assets/Prefabs/Character.prefab";

        [MenuItem("AR Character App/Overlay の透過設定を確認", priority = 63)]
        public static void Inspect()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[OverlayInspector] {k_PrefabPath} が見つかりません。");
                return;
            }

            var report = new StringBuilder();
            report.AppendLine("=== メッシュとマテリアルの対応 ===");

            foreach (var renderer in prefab.GetComponentsInChildren<Renderer>(true))
            {
                var names = renderer.sharedMaterials
                    .Select(m => m != null ? m.name : "(null)");

                report.AppendLine($"  {renderer.name,-14} -> {string.Join(", ", names)}");
            }

            report.AppendLine();
            report.AppendLine("=== 透過に関わる設定 ===");

            var materials = prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null)
                .Distinct();

            foreach (var material in materials)
            {
                report.AppendLine();
                report.AppendLine($"[{material.name}]  {material.shader.name}  queue={material.renderQueue}");

                foreach (var key in new[]
                         {
                             "_TransparentMode", "_ZWrite", "_ZTest", "_Cutoff",
                             "_SrcBlend", "_DstBlend", "_BlendOp", "_Cull",
                             "_AlphaMaskMode", "_AlphaMaskValue", "_AlphaBoostFA",
                             "_UseDither", "_lilToonVersion",
                         })
                {
                    if (material.HasProperty(key))
                        report.AppendLine($"    {key,-18} = {material.GetFloat(key)}");
                }

                var main = material.HasProperty("_MainTex") ? material.GetTexture("_MainTex") as Texture2D : null;
                if (main != null)
                {
                    var path = AssetDatabase.GetAssetPath(main);
                    var importer = AssetImporter.GetAtPath(path) as TextureImporter;

                    report.AppendLine($"    _MainTex           = {main.name} ({main.width}x{main.height}) {main.format}");

                    if (importer != null)
                    {
                        report.AppendLine($"      alphaIsTransparency = {importer.alphaIsTransparency}");
                        report.AppendLine($"      alphaSource         = {importer.alphaSource}");
                        report.AppendLine($"      textureType         = {importer.textureType}");
                        report.AppendLine($"      DoesSourceHaveAlpha = {importer.DoesSourceTextureHaveAlpha()}");
                    }
                }

                var alphaMask = material.HasProperty("_AlphaMask") ? material.GetTexture("_AlphaMask") : null;
                if (alphaMask != null)
                    report.AppendLine($"    _AlphaMask         = {alphaMask.name}");
            }

            Debug.Log(report.ToString());
        }
    }
}
