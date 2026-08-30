using System.Collections.Generic;
using System.IO;
using System.Linq;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using UnityEngine.XR.ARFoundation;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// AR シーンをまるごとコードから組み立てる。
    ///
    /// 手で並べたシーンは差分が読めず、壊れたときに原因を追えない。
    /// 生成手順をコードに残しておけば、いつでも同じ状態を作り直せる。
    /// </summary>
    public static class ARSceneBuilder
    {
        const string k_ScenePath = "Assets/Scenes/AR.unity";
        const string k_PrefabDir = "Assets/Prefabs";
        const string k_ArtDir = "Assets/Art/Generated";

        [MenuItem("AR Character App/2. AR シーンを生成", priority = 20)]
        public static void BuildScene()
        {
            if (EditorApplication.isPlaying)
            {
                Debug.LogWarning("[ARSceneBuilder] 再生中は実行できません。");
                return;
            }

            if (!EditorSceneManager.SaveCurrentModifiedScenesIfUserWantsTo())
                return;

            EnsureFolders();

            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            ConfigureLighting();

            // ---- AR の骨格 -------------------------------------------------
            var session = CreateARSession();
            var origin = CreateXROrigin();

            if (session == null || origin == null)
            {
                Debug.LogError("[ARSceneBuilder] AR Foundation のシーン部品を作成できませんでした。" +
                               "パッケージが正しく入っているか確認してください。");
                return;
            }

            var arCamera = origin.Camera;

            var planeManager = origin.GetComponent<ARPlaneManager>()
                               ?? origin.gameObject.AddComponent<ARPlaneManager>();
            var raycastManager = origin.GetComponent<ARRaycastManager>()
                                 ?? origin.gameObject.AddComponent<ARRaycastManager>();
            var anchorManager = origin.GetComponent<ARAnchorManager>()
                                ?? origin.gameObject.AddComponent<ARAnchorManager>();

            // 水平面だけ拾う。壁に立たせたい場合は Horizontal | Vertical にする。
            planeManager.requestedDetectionMode = UnityEngine.XR.ARSubsystems.PlaneDetectionMode.Horizontal;

            var planePrefab = CreatePlaneVisualizerPrefab();
            if (planePrefab != null)
                planeManager.planePrefab = planePrefab;

            // ---- 見た目の部品 -----------------------------------------------
            var reticle = CreateReticle();
            var characterPrefab = CreatePlaceholderCharacterPrefab();

            // キャラ切り替えを試せるように、色違いの仮モデルをもう 1 体作っておく。
            // 本物のモデルが届いたら AnomeaCharacterSetup 側の一覧を差し替える。
            CreatePlaceholderCharacterPrefab("PlaceholderCharacterB",
                new Color(0.95f, 0.55f, 0.72f), new Color(0.99f, 0.88f, 0.80f));

            // ---- フォールバック用ビューア -----------------------------------
            var fallbackRoot = new GameObject("Fallback Viewer");
            var fallbackCameraGO = new GameObject("Fallback Camera", typeof(Camera));
            fallbackCameraGO.transform.SetParent(fallbackRoot.transform, false);

            var fallbackCamera = fallbackCameraGO.GetComponent<Camera>();
            fallbackCamera.clearFlags = CameraClearFlags.SolidColor;
            fallbackCamera.backgroundColor = new Color(0.09f, 0.10f, 0.13f, 1f);
            fallbackCamera.nearClipPlane = 0.05f;

            var fallbackAnchor = new GameObject("Character Anchor");
            fallbackAnchor.transform.SetParent(fallbackRoot.transform, false);

            fallbackRoot.SetActive(false);

            // ---- UI ----------------------------------------------------------
            var uiRoot = new GameObject("UI");
            var ui = ARUIBuilder.Build(uiRoot.transform);
            CreateEventSystem();

            // ---- コントローラ群 -----------------------------------------------
            var controllers = new GameObject("App Controllers");
            var flow = controllers.AddComponent<ARAppFlow>();
            var placement = controllers.AddComponent<ARPlacementController>();
            var uiController = controllers.AddComponent<ARUIController>();
            var photoCapture = controllers.AddComponent<ARPhotoCapture>();

            var fallbackViewer = fallbackRoot.AddComponent<NonARFallbackViewer>();

            // ---- 参照の配線 --------------------------------------------------
            WirePlacement(placement, raycastManager, anchorManager, planeManager,
                          arCamera, reticle, characterPrefab);

            WirePhotoCapture(photoCapture, ui);

            WireFallback(fallbackViewer, fallbackCamera, characterPrefab,
                         fallbackAnchor.transform, fallbackRoot, origin.gameObject);

            WireFlow(flow, session, planeManager, placement, fallbackViewer);
            WireUI(uiController, flow, placement, photoCapture, ui);

            // ---- 保存 ----------------------------------------------------------
            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene, k_ScenePath);

            RegisterSceneInBuildSettings(k_ScenePath);

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            Debug.Log($"[ARSceneBuilder] {k_ScenePath} を生成しました。");
        }

        // ---- シーン部品 ---------------------------------------------------------

        static ARSession CreateARSession()
        {
            // AR Foundation のメニュー項目を使うと、その版で正しい構成が得られる。
            EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session");

            var session = Object.FindObjectOfType<ARSession>();
            if (session != null)
                return session;

            // メニュー項目名が変わっていた場合の保険
            var go = new GameObject("AR Session");
            return go.AddComponent<ARSession>();
        }

        static XROrigin CreateXROrigin()
        {
            EditorApplication.ExecuteMenuItem("GameObject/XR/XR Origin (Mobile AR)");

            var origin = Object.FindObjectOfType<XROrigin>();
            if (origin == null)
            {
                // 旧名称も試す
                EditorApplication.ExecuteMenuItem("GameObject/XR/AR Session Origin");
                origin = Object.FindObjectOfType<XROrigin>();
            }

            if (origin != null && origin.Camera != null)
            {
                var cam = origin.Camera;
                cam.tag = "MainCamera";
                cam.nearClipPlane = 0.05f;
                cam.farClipPlane = 30f;
            }

            return origin;
        }

        static GameObject CreatePlaneVisualizerPrefab()
        {
            EditorApplication.ExecuteMenuItem("GameObject/XR/AR Default Plane");

            var visualizer = Object.FindObjectOfType<ARPlaneMeshVisualizer>();
            if (visualizer == null)
                return null;

            var path = $"{k_PrefabDir}/ARPlaneVisualizer.prefab";
            var prefab = PrefabUtility.SaveAsPrefabAsset(visualizer.gameObject, path);
            Object.DestroyImmediate(visualizer.gameObject);

            return prefab;
        }

        /// <summary>設置候補地点に出すリング。中央のレイが平面に当たっている間だけ表示する。</summary>
        static GameObject CreateReticle()
        {
            var material = CreateRingMaterial();

            var root = new GameObject("Placement Reticle");

            var quad = GameObject.CreatePrimitive(PrimitiveType.Quad);
            quad.name = "Ring";
            quad.transform.SetParent(root.transform, false);
            quad.transform.localRotation = Quaternion.Euler(90f, 0f, 0f);
            quad.transform.localScale = Vector3.one * 0.28f;

            Object.DestroyImmediate(quad.GetComponent<Collider>());
            quad.GetComponent<MeshRenderer>().sharedMaterial = material;

            root.SetActive(false);
            return root;
        }

        static Material CreateRingMaterial()
        {
            var texturePath = $"{k_ArtDir}/Reticle.png";

            if (!File.Exists(texturePath))
            {
                var texture = GenerateRingTexture(256);
                File.WriteAllBytes(texturePath, texture.EncodeToPNG());
                Object.DestroyImmediate(texture);

                AssetDatabase.ImportAsset(texturePath, ImportAssetOptions.ForceUpdate);

                if (AssetImporter.GetAtPath(texturePath) is TextureImporter importer)
                {
                    importer.textureType = TextureImporterType.Default;
                    importer.alphaIsTransparency = true;
                    importer.mipmapEnabled = false;
                    importer.wrapMode = TextureWrapMode.Clamp;
                    importer.SaveAndReimport();
                }
            }

            var loaded = AssetDatabase.LoadAssetAtPath<Texture2D>(texturePath);

            var shader = Shader.Find("Unlit/Transparent");
            var material = new Material(shader) { mainTexture = loaded };

            var materialPath = $"{k_ArtDir}/ReticleMaterial.mat";
            if (AssetDatabase.LoadAssetAtPath<Material>(materialPath) == null)
                AssetDatabase.CreateAsset(material, materialPath);

            return AssetDatabase.LoadAssetAtPath<Material>(materialPath);
        }

        /// <summary>白いリング画像を手続き的に作る。外部アセットに依存させないため。</summary>
        static Texture2D GenerateRingTexture(int size)
        {
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false);
            var center = (size - 1) * 0.5f;
            var outer = size * 0.46f;
            var inner = size * 0.34f;

            var pixels = new Color32[size * size];

            for (var y = 0; y < size; y++)
            {
                for (var x = 0; x < size; x++)
                {
                    var distance = Mathf.Sqrt((x - center) * (x - center) + (y - center) * (y - center));

                    // 内外の縁を 1.5px ぶんぼかしてジャギを消す
                    var a = Mathf.Clamp01((outer - distance) / 1.5f)
                            * Mathf.Clamp01((distance - inner) / 1.5f);

                    pixels[y * size + x] = new Color32(255, 255, 255, (byte)(a * 235f));
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply();
            return texture;
        }

        /// <summary>
        /// 差し替え前提のプレースホルダー。
        /// 本物のモデルが来たら、このプレハブを置き換えるだけで動く構造にしてある。
        /// </summary>
        static GameObject CreatePlaceholderCharacterPrefab()
            => CreatePlaceholderCharacterPrefab("PlaceholderCharacter",
                new Color(0.35f, 0.62f, 0.95f), new Color(0.98f, 0.86f, 0.76f));

        static GameObject CreatePlaceholderCharacterPrefab(string prefabName, Color bodyColor, Color skinColor)
        {
            var path = $"{k_PrefabDir}/{prefabName}.prefab";

            var root = new GameObject(prefabName);
            root.AddComponent<ARCharacter>();

            // 揺れは子(Visual)に付ける。root は設置後の回転/拡大ジェスチャが触るため。
            var visual = new GameObject("Visual");
            visual.transform.SetParent(root.transform, false);
            visual.AddComponent<PlaceholderBob>();

            var bodyMaterial = CreateColorMaterial($"{prefabName}_Body", bodyColor);
            var headMaterial = CreateColorMaterial($"{prefabName}_Head", skinColor);
            var eyeMaterial = CreateColorMaterial("PlaceholderEye", new Color(0.11f, 0.12f, 0.16f));

            AddPart(visual.transform, PrimitiveType.Capsule, "Body",
                new Vector3(0f, 0.42f, 0f), new Vector3(0.34f, 0.42f, 0.34f), bodyMaterial);

            AddPart(visual.transform, PrimitiveType.Sphere, "Head",
                new Vector3(0f, 1.02f, 0f), Vector3.one * 0.34f, headMaterial);

            AddPart(visual.transform, PrimitiveType.Sphere, "EyeL",
                new Vector3(-0.08f, 1.06f, -0.15f), Vector3.one * 0.06f, eyeMaterial);

            AddPart(visual.transform, PrimitiveType.Sphere, "EyeR",
                new Vector3(0.08f, 1.06f, -0.15f), Vector3.one * 0.06f, eyeMaterial);

            var prefab = PrefabUtility.SaveAsPrefabAsset(root, path);
            Object.DestroyImmediate(root);

            return prefab;
        }

        static void AddPart(Transform parent, PrimitiveType type, string name,
            Vector3 localPosition, Vector3 localScale, Material material)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = name;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPosition;
            go.transform.localScale = localScale;

            Object.DestroyImmediate(go.GetComponent<Collider>());
            go.GetComponent<MeshRenderer>().sharedMaterial = material;
        }

        static Material CreateColorMaterial(string name, Color color)
        {
            var path = $"{k_ArtDir}/{name}.mat";
            var existing = AssetDatabase.LoadAssetAtPath<Material>(path);
            if (existing != null)
                return existing;

            var shader = Shader.Find("Standard") ?? Shader.Find("Diffuse");
            var material = new Material(shader) { color = color };
            AssetDatabase.CreateAsset(material, path);

            return material;
        }

        static void CreateEventSystem()
        {
            if (Object.FindObjectOfType<EventSystem>() != null)
                return;

            var go = new GameObject("EventSystem", typeof(EventSystem));

            // Active Input Handling が Both のため、旧 InputModule で動く。
            go.AddComponent<StandaloneInputModule>();
        }

        static void ConfigureLighting()
        {
            var lightGO = new GameObject("Directional Light");
            var light = lightGO.AddComponent<Light>();
            light.type = LightType.Directional;
            light.intensity = 1.05f;
            light.shadows = LightShadows.Soft;
            lightGO.transform.rotation = Quaternion.Euler(50f, -30f, 0f);

            // 空が無いので環境光は単色で入れる。これが無いと影側が真っ黒になる。
            RenderSettings.ambientMode = UnityEngine.Rendering.AmbientMode.Flat;
            RenderSettings.ambientLight = new Color(0.48f, 0.50f, 0.56f);
        }

        // ---- 配線 -------------------------------------------------------------

        static void WirePlacement(ARPlacementController placement,
            ARRaycastManager raycastManager, ARAnchorManager anchorManager,
            ARPlaneManager planeManager, Camera arCamera,
            GameObject reticle, GameObject characterPrefab)
        {
            var so = new SerializedObject(placement);
            SetRef(so, "m_RaycastManager", raycastManager);
            SetRef(so, "m_AnchorManager", anchorManager);
            SetRef(so, "m_PlaneManager", planeManager);
            SetRef(so, "m_ARCamera", arCamera);
            SetRef(so, "m_Reticle", reticle);
            SetRef(so, "m_CharacterPrefab", characterPrefab);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireFallback(NonARFallbackViewer viewer, Camera camera,
            GameObject characterPrefab, Transform anchor, GameObject viewerRoot, GameObject arRoot)
        {
            var so = new SerializedObject(viewer);
            SetRef(so, "m_ViewerCamera", camera);
            SetRef(so, "m_CharacterPrefab", characterPrefab);
            SetRef(so, "m_CharacterAnchor", anchor);
            SetRef(so, "m_ViewerRoot", viewerRoot);
            SetRef(so, "m_ARRootToDisable", arRoot);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireFlow(ARAppFlow flow, ARSession session, ARPlaneManager planeManager,
            ARPlacementController placement, NonARFallbackViewer fallback)
        {
            var so = new SerializedObject(flow);
            SetRef(so, "m_Session", session);
            SetRef(so, "m_PlaneManager", planeManager);
            SetRef(so, "m_Placement", placement);
            SetRef(so, "m_Fallback", fallback);
            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>
        /// 撮影時に隠すものを指定する。
        /// 選択パネル・シャッター・置きなおすボタンが写り込むと「写真」にならない。
        /// フラッシュと「保存しました」は撮影後に出すので、隠す対象には入れない。
        /// </summary>
        static void WirePhotoCapture(ARPhotoCapture capture, BuiltUI ui)
        {
            var so = new SerializedObject(capture);

            var hide = new List<UnityEngine.Object>();
            if (ui.SelectorPanel != null) hide.Add(ui.SelectorPanel);
            if (ui.ShutterButton != null) hide.Add(ui.ShutterButton.gameObject);
            if (ui.ResetButton != null) hide.Add(ui.ResetButton.gameObject);
            if (ui.OpenSelectorButton != null) hide.Add(ui.OpenSelectorButton.gameObject);
            if (ui.FaceCameraButton != null) hide.Add(ui.FaceCameraButton.gameObject);

            var list = so.FindProperty("m_HideWhileCapturing");
            list.arraySize = hide.Count;

            for (var i = 0; i < hide.Count; i++)
                list.GetArrayElementAtIndex(i).objectReferenceValue = hide[i];

            SetRef(so, "m_FlashOverlay", ui.FlashOverlay);
            SetRef(so, "m_SavedToast", ui.SavedToast);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void WireUI(ARUIController controller, ARAppFlow flow,
            ARPlacementController placement, ARPhotoCapture photoCapture, BuiltUI ui)
        {
            var so = new SerializedObject(controller);
            SetRef(so, "m_Flow", flow);
            SetRef(so, "m_Placement", placement);
            SetRef(so, "m_PhotoCapture", photoCapture);
            SetRef(so, "m_ScanLabel", ui.ScanLabel);
            SetRef(so, "m_ContinueButton", ui.ContinueButton);
            SetRef(so, "m_OpenSettingsButton", ui.OpenSettingsButton);
            SetRef(so, "m_RetryButton", ui.RetryButton);
            SetRef(so, "m_ResetButton", ui.ResetButton);
            SetRef(so, "m_FaceCameraButton", ui.FaceCameraButton);

            var panels = so.FindProperty("m_Panels");
            panels.arraySize = ui.Panels.Count;

            for (var i = 0; i < ui.Panels.Count; i++)
            {
                var element = panels.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("phase").enumValueIndex = (int)ui.Panels[i].Key;
                element.FindPropertyRelative("root").objectReferenceValue = ui.Panels[i].Value;
            }

            SetRef(so, "m_ShutterButton", ui.ShutterButton);
            SetRef(so, "m_SelectorContent", ui.SelectorContent);
            SetRef(so, "m_SelectorPanel", ui.SelectorPanel);
            SetRef(so, "m_OpenSelectorButton", ui.OpenSelectorButton);
            SetRef(so, "m_CloseSelectorButton", ui.CloseSelectorButton);

            // 選択肢ボタンは実行時に作るので、角丸スプライトだけ先に渡しておく
            SetRef(so, "m_ItemSprite",
                AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"));

            var tabs = so.FindProperty("m_TabButtons");
            var tabButtons = ui.TabButtons ?? System.Array.Empty<UnityEngine.UI.Button>();
            tabs.arraySize = tabButtons.Length;

            for (var i = 0; i < tabButtons.Length; i++)
                tabs.GetArrayElementAtIndex(i).objectReferenceValue = tabButtons[i];

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        static void SetRef(SerializedObject so, string field, Object value)
        {
            var property = so.FindProperty(field);
            if (property == null)
            {
                Debug.LogWarning($"[ARSceneBuilder] フィールド '{field}' が見つかりません。");
                return;
            }

            property.objectReferenceValue = value;
        }

        // ---- 雑務 -------------------------------------------------------------

        static void EnsureFolders()
        {
            foreach (var path in new[] { "Assets/Scenes", k_PrefabDir, "Assets/Art", k_ArtDir })
            {
                if (!Directory.Exists(path))
                    Directory.CreateDirectory(path);
            }

            AssetDatabase.Refresh();
        }

        static void RegisterSceneInBuildSettings(string scenePath)
        {
            var scenes = EditorBuildSettings.scenes.ToList();

            if (scenes.Any(s => s.path == scenePath))
            {
                // 既にあるなら有効化して先頭へ
                scenes.RemoveAll(s => s.path == scenePath);
            }

            scenes.Insert(0, new EditorBuildSettingsScene(scenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
        }
    }
}
