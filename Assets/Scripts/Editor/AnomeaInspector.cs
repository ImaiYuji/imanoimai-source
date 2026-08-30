using System.Linq;
using System.Text;
using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// 持ち込んだ Anomea 一式(FBX / プレハブ / ポーズ)が
    /// このプロジェクトで正しく解決できているかを確認する調査用ツール。
    /// </summary>
    public static class AnomeaInspector
    {
        const string k_FbxPath = "Assets/Anomea/FBX/Anomea.fbx";
        const string k_PrefabPath = "Assets/Anomea/Prefab/Anomea_pre_1 Variant.prefab";
        const string k_PoseDir = "Assets/Anomea/Animations_v1.0.1/Animation/Pose";

        [MenuItem("AR Character App/Anomea の状態を確認", priority = 60)]
        public static void Inspect()
        {
            var report = new StringBuilder();

            // ---- FBX ---------------------------------------------------------
            report.AppendLine("=== FBX ===");
            var importer = AssetImporter.GetAtPath(k_FbxPath) as ModelImporter;

            if (importer == null)
            {
                report.AppendLine($"  NG  {k_FbxPath} を ModelImporter として読めません");
            }
            else
            {
                report.AppendLine($"  animationType = {importer.animationType}  (Humanoid であること)");
                report.AppendLine($"  avatarSetup   = {importer.avatarSetup}");
                report.AppendLine($"  scaleFactor   = {importer.globalScale}");
            }

            var fbx = AssetDatabase.LoadMainAssetAtPath(k_FbxPath) as GameObject;
            if (fbx != null)
            {
                var smrs = fbx.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var tri = smrs.Where(s => s.sharedMesh != null).Sum(s => s.sharedMesh.triangles.Length / 3);
                report.AppendLine($"  SkinnedMeshRenderer = {smrs.Length}, 三角形 = {tri:N0}");
                report.AppendLine($"  Transform 数 = {fbx.GetComponentsInChildren<Transform>(true).Length}");

                foreach (var smr in smrs)
                    report.AppendLine($"    - {smr.name}  bones={smr.bones.Length}  root={(smr.rootBone != null ? smr.rootBone.name : "(null)")}");
            }

            // ---- プレハブ -------------------------------------------------------
            report.AppendLine();
            report.AppendLine("=== プレハブ ===");
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(k_PrefabPath);

            if (prefab == null)
            {
                report.AppendLine($"  NG  {k_PrefabPath} が読めません");
            }
            else
            {
                var animator = prefab.GetComponentInChildren<Animator>();
                report.AppendLine($"  Animator = {(animator != null ? "あり" : "なし")}" +
                                  $"  avatar={(animator != null && animator.avatar != null ? animator.avatar.name : "(null)")}" +
                                  $"  isHuman={(animator != null && animator.avatar != null && animator.avatar.isHuman)}");

                var renderers = prefab.GetComponentsInChildren<Renderer>(true);
                report.AppendLine($"  Renderer = {renderers.Length}");

                var materials = renderers.SelectMany(r => r.sharedMaterials).Where(m => m != null).Distinct().ToList();
                report.AppendLine($"  マテリアル = {materials.Count}");

                foreach (var m in materials)
                {
                    var shaderName = m.shader != null ? m.shader.name : "(null)";
                    var broken = m.shader == null || shaderName == "Hidden/InternalErrorShader";
                    report.AppendLine($"    {(broken ? "NG " : "OK ")} {m.name,-30} {shaderName}");
                }

                var bounds = new Bounds();
                var has = false;
                foreach (var r in renderers)
                {
                    if (!has) { bounds = r.bounds; has = true; }
                    else bounds.Encapsulate(r.bounds);
                }
                if (has)
                    report.AppendLine($"  身長の目安 = {bounds.size.y:F2} m");
            }

            // ---- ポーズ ---------------------------------------------------------
            report.AppendLine();
            report.AppendLine("=== ポーズアニメーション ===");

            var clipGuids = AssetDatabase.FindAssets("t:AnimationClip", new[] { k_PoseDir });
            report.AppendLine($"  {clipGuids.Length} 件");

            foreach (var guid in clipGuids.Take(25))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);
                if (clip == null)
                    continue;

                report.AppendLine($"    {clip.name,-26} humanMotion={clip.humanMotion,-5} " +
                                  $"length={clip.length:F2}s loop={clip.isLooping}");
            }

            Debug.Log(report.ToString());
        }
    }
}
