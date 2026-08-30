using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// キャラクターが使っているマテリアルとシェーダーを一覧する。
    /// シェーダーを差し替える前後で、何がどう変わったかを確認するために使う。
    /// </summary>
    public static class MaterialInspectorTool
    {
        const string k_PrefabPath = "Assets/Prefabs/Character.prefab";

        [MenuItem("AR Character App/マテリアル構成を表示", priority = 43)]
        public static void Dump()
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[MaterialInspector] {k_PrefabPath} が見つかりません。");
                return;
            }

            var materials = prefab.GetComponentsInChildren<Renderer>(true)
                .SelectMany(r => r.sharedMaterials)
                .Where(m => m != null)
                .Distinct()
                .ToList();

            var report = new StringBuilder();
            report.AppendLine($"=== {k_PrefabPath} のマテリアル ({materials.Count} 個) ===");

            foreach (var material in materials)
            {
                report.AppendLine();
                report.AppendLine($"[{material.name}]  shader = {material.shader.name}");

                // レンダリングモードの判定材料になるものを拾う
                foreach (var key in new[] { "_BlendMode", "_Cutoff", "_CullMode", "_ZWrite" })
                {
                    if (material.HasProperty(key))
                        report.AppendLine($"    {key} = {material.GetFloat(key)}");
                }

                foreach (var key in new[] { "_Color", "_ShadeColor", "_EmissionColor", "_OutlineColor", "_RimColor" })
                {
                    if (material.HasProperty(key))
                        report.AppendLine($"    {key} = {material.GetColor(key)}");
                }

                foreach (var key in new[] { "_MainTex", "_ShadeTexture", "_BumpMap", "_EmissionMap", "_SphereAdd", "_OutlineWidthTexture" })
                {
                    if (!material.HasProperty(key))
                        continue;

                    var tex = material.GetTexture(key);
                    if (tex != null)
                        report.AppendLine($"    {key} = {tex.name} ({tex.width}x{tex.height})");
                }

                foreach (var key in new[] { "_OutlineWidth", "_ShadeShift", "_ShadeToony", "_RimFresnelPower" })
                {
                    if (material.HasProperty(key))
                        report.AppendLine($"    {key} = {material.GetFloat(key)}");
                }

                report.AppendLine($"    renderQueue = {material.renderQueue}, keywords = {string.Join(",", material.shaderKeywords)}");
            }

            Debug.Log(report.ToString());
        }

        /// <summary>lilToon 本体と Lite 版のプロパティを列挙する(変換マッピングを書くための調査用)。</summary>
        [MenuItem("AR Character App/lilToon のプロパティを一覧", priority = 45)]
        public static void ListLilToonProperties()
        {
            foreach (var shaderName in new[] { "lilToon", "Hidden/lilToonLite" })
            {
                var shader = Shader.Find(shaderName);
                if (shader == null)
                {
                    Debug.LogWarning($"[MaterialInspector] {shaderName} が見つかりません。");
                    continue;
                }

                var report = new StringBuilder();
                report.AppendLine($"=== {shaderName} のプロパティ ({ShaderUtil.GetPropertyCount(shader)}) ===");

                for (var i = 0; i < ShaderUtil.GetPropertyCount(shader); i++)
                {
                    var name = ShaderUtil.GetPropertyName(shader, i);

                    // 変換で使いそうなものだけに絞る。全部出すと数百行になる。
                    var interesting = name.Contains("Main") || name.Contains("Color")
                        || name.Contains("Shadow") || name.Contains("Bump") || name.Contains("Normal")
                        || name.Contains("Outline") || name.Contains("Emission")
                        || name.Contains("Cutoff") || name.Contains("Cull")
                        || name.Contains("Rim") || name.Contains("MatCap")
                        || name.Contains("ZWrite") || name.Contains("Blend");

                    if (!interesting)
                        continue;

                    report.AppendLine($"  {name}  [{ShaderUtil.GetPropertyType(shader, i)}]");
                }

                Debug.Log(report.ToString());
            }
        }

        /// <summary>プロジェクトで見つかる lilToon 系シェーダーを列挙する。</summary>
        [MenuItem("AR Character App/lilToon シェーダーを一覧", priority = 44)]
        public static void ListLilToonShaders()
        {
            var guids = AssetDatabase.FindAssets("t:Shader");
            var report = new StringBuilder();
            report.AppendLine("=== lilToon 系シェーダー ===");

            var count = 0;
            foreach (var guid in guids)
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);

                if (shader == null || !shader.name.Contains("lilToon"))
                    continue;

                report.AppendLine($"  {shader.name}");
                count++;
            }

            report.AppendLine($"({count} 個)");
            Debug.Log(report.ToString());
        }
    }
}
