using System.IO;
using UnityEditor;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// キャラクターの見た目を PNG に書き出す。実機で AR を立ち上げなくても
    /// マテリアルの当たり具合を確かめられるようにするための確認用。
    /// </summary>
    static class CharacterPreviewShot
    {
        [MenuItem("AR Character App/デバッグ/キャラクターの見た目を書き出す")]
        public static void Shoot()
        {
            var outDir = System.Environment.GetEnvironmentVariable("IMANOIMAI_SHOT_DIR");
            if (string.IsNullOrEmpty(outDir))
                outDir = "Build";
            Directory.CreateDirectory(outDir);

            Render("Assets/Prefabs/Hiasobi.prefab", Path.Combine(outDir, "dark.png"));
            Render("Assets/Prefabs/Character.prefab", Path.Combine(outDir, "imai.png"));
        }

        static void Render(string prefabPath, string outPath)
        {
            var prefab = AssetDatabase.LoadAssetAtPath<GameObject>(prefabPath);
            if (prefab == null)
            {
                Debug.LogError($"[Shot] {prefabPath} が読めません。");
                return;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(prefab);
            instance.transform.position = Vector3.zero;
            instance.transform.rotation = Quaternion.identity;

            // 影の出かたを見たいので、太陽と環境光をそれらしく置く
            var lightGo = new GameObject("ShotLight");
            var light = lightGo.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.1f;
            light.color = Color.white;
            lightGo.transform.rotation = Quaternion.Euler(38f, 160f, 0f);
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.55f, 0.55f, 0.62f);

            var camGo = new GameObject("ShotCamera");
            var cam = camGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color(0.16f, 0.15f, 0.20f);
            cam.fieldOfView = 30f;
            cam.nearClipPlane = 0.05f;
            cam.farClipPlane = 30f;

            // 全身が収まる位置にカメラを置く
            var bounds = Bounds(instance);
            var height = Mathf.Max(bounds.size.y, 0.2f);
            var center = bounds.center;
            var distance = height / (2f * Mathf.Tan(cam.fieldOfView * 0.5f * Mathf.Deg2Rad)) * 1.18f;
            camGo.transform.position = center + new Vector3(0f, 0f, distance);
            camGo.transform.LookAt(center);

            const int w = 720, h = 1280;
            var rt = new RenderTexture(w, h, 24, RenderTextureFormat.ARGB32) { antiAliasing = 4 };
            cam.targetTexture = rt;
            cam.Render();

            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();
            RenderTexture.active = null;

            File.WriteAllBytes(outPath, tex.EncodeToPNG());
            Debug.Log($"[Shot] {outPath} に書き出しました ({prefabPath})");

            cam.targetTexture = null;
            Object.DestroyImmediate(rt);
            Object.DestroyImmediate(tex);
            Object.DestroyImmediate(camGo);
            Object.DestroyImmediate(lightGo);
            Object.DestroyImmediate(instance);
        }

        static Bounds Bounds(GameObject go)
        {
            var renderers = go.GetComponentsInChildren<Renderer>(false);
            if (renderers.Length == 0)
                return new Bounds(go.transform.position, Vector3.one);

            var b = renderers[0].bounds;
            for (var i = 1; i < renderers.Length; i++)
                b.Encapsulate(renderers[i].bounds);
            return b;
        }
    }
}
