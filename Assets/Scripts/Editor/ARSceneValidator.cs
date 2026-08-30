using System.Collections.Generic;
using System.Linq;
using System.Text;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.XR.ARFoundation;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// 生成したシーンが AR アプリとして成立しているかを検査する。
    ///
    /// AR は実機に持っていくまで壊れているか分からないことが多い。
    /// 「カメラが動かない」「タップしても置けない」といった典型的な欠落を、
    /// ここで先に潰しておく。
    /// </summary>
    public static class ARSceneValidator
    {
        const string k_ScenePath = "Assets/Scenes/AR.unity";

        [MenuItem("AR Character App/シーンを検証", priority = 50)]
        public static void Validate()
        {
            var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);
            var problems = new List<string>();
            var report = new StringBuilder();

            report.AppendLine($"=== {k_ScenePath} の検証 ===");

            // ---- AR の骨格 -------------------------------------------------
            var session = Object.FindObjectOfType<ARSession>();
            Check(report, problems, session != null, "ARSession");

            var origin = Object.FindObjectOfType<XROrigin>();
            Check(report, problems, origin != null, "XROrigin");

            if (origin != null)
            {
                Check(report, problems, origin.Camera != null, "XROrigin.Camera");

                if (origin.Camera != null)
                {
                    var cam = origin.Camera;

                    Check(report, problems, cam.GetComponent<ARCameraManager>() != null,
                        "ARCameraManager (カメラ映像の取得)");
                    Check(report, problems, cam.GetComponent<ARCameraBackground>() != null,
                        "ARCameraBackground (背景描画)");

                    // これが無いとカメラが現実の動きに追従しない
                    var hasPoseDriver = cam.GetComponents<MonoBehaviour>()
                        .Any(c => c != null && c.GetType().Name.Contains("TrackedPoseDriver"));
                    Check(report, problems, hasPoseDriver, "TrackedPoseDriver (カメラの姿勢追従)");

                    Check(report, problems, cam.CompareTag("MainCamera"), "カメラの MainCamera タグ");
                }

                Check(report, problems, origin.GetComponent<ARPlaneManager>() != null, "ARPlaneManager");
                Check(report, problems, origin.GetComponent<ARRaycastManager>() != null, "ARRaycastManager");
                Check(report, problems, origin.GetComponent<ARAnchorManager>() != null, "ARAnchorManager");

                var planeManager = origin.GetComponent<ARPlaneManager>();
                if (planeManager != null)
                {
                    Check(report, problems, planeManager.planePrefab != null,
                        "ARPlaneManager.planePrefab (検出した平面の可視化)");
                }
            }

            // ---- アプリ側 ---------------------------------------------------
            var flow = Object.FindObjectOfType<ARAppFlow>();
            Check(report, problems, flow != null, "ARAppFlow");

            var placement = Object.FindObjectOfType<ARPlacementController>();
            Check(report, problems, placement != null, "ARPlacementController");

            var uiController = Object.FindObjectOfType<ARUIController>();
            Check(report, problems, uiController != null, "ARUIController");

            var fallback = Object.FindObjectOfType<NonARFallbackViewer>(true);
            Check(report, problems, fallback != null, "NonARFallbackViewer");

            Check(report, problems, Object.FindObjectOfType<EventSystem>() != null,
                "EventSystem (UI のタップ判定)");

            // ---- 参照の穴を探す ---------------------------------------------
            foreach (var component in new Component[] { flow, placement, uiController, fallback })
            {
                if (component == null)
                    continue;

                CheckSerializedRefs(report, problems, component);
            }

            // ---- キャラクタープレハブの中身 --------------------------------
            // 参照が非 null でも中身が壊れていることがあるので、実体まで見る。
            if (placement != null)
            {
                var so = new SerializedObject(placement);
                var characterPrefab = so.FindProperty("m_CharacterPrefab").objectReferenceValue as GameObject;

                if (characterPrefab == null)
                {
                    Check(report, problems, false, "Character Prefab");
                }
                else
                {
                    var renderers = characterPrefab.GetComponentsInChildren<Renderer>(true);
                    Check(report, problems, renderers.Length > 0,
                        $"Character Prefab の Renderer ({renderers.Length})");

                    var brokenMaterials = renderers
                        .SelectMany(r => r.sharedMaterials)
                        .Count(m => m == null || m.shader == null || m.shader.name == "Hidden/InternalErrorShader");
                    Check(report, problems, brokenMaterials == 0,
                        $"Character Prefab のマテリアル(壊れ {brokenMaterials} 件)");

                    var animator = characterPrefab.GetComponentInChildren<Animator>();
                    Check(report, problems, animator != null && animator.runtimeAnimatorController != null,
                        "Character Prefab の Animator Controller");

                    var wardrobe = characterPrefab.GetComponent<ARCharacterWardrobe>();
                    if (wardrobe != null)
                    {
                        var wardrobeSo = new SerializedObject(wardrobe);
                        var partCount = wardrobeSo.FindProperty("m_Parts").arraySize;
                        Check(report, problems, partCount > 0, $"着せ替えパーツ数 ({partCount})");
                    }
                    else
                    {
                        Check(report, problems, false, "Character Prefab の ARCharacterWardrobe");
                    }

                    var animatorLayers = characterPrefab.GetComponentInChildren<Animator>();
                    var controller = animatorLayers != null
                        ? animatorLayers.runtimeAnimatorController as UnityEditor.Animations.AnimatorController
                        : null;
                    Check(report, problems, controller != null && controller.layers.Length >= 3,
                        $"Animator のレイヤー数 ({(controller != null ? controller.layers.Length : 0)}) " +
                        "— ポーズ / 素の顔 / 表情 の 3 枚が必要");

                    var poser = characterPrefab.GetComponent<ARCharacterPoser>();
                    if (poser != null)
                    {
                        var poserSo = new SerializedObject(poser);
                        var poseCount = poserSo.FindProperty("m_Poses").arraySize;
                        var expressionCount = poserSo.FindProperty("m_Expressions").arraySize;

                        Check(report, problems, poseCount > 0, $"ポーズ登録数 ({poseCount})");
                        Check(report, problems, expressionCount > 0, $"表情登録数 ({expressionCount})");

                        var animator2 = characterPrefab.GetComponentInChildren<Animator>();
                        var layers = animator2 != null && animator2.runtimeAnimatorController != null
                            ? animator2.runtimeAnimatorController.animationClips.Length
                            : 0;
                        Check(report, problems, layers > 0, $"Animator のクリップ数 ({layers})");
                    }
                    else
                    {
                        Check(report, problems, false, "Character Prefab の ARCharacterPoser");
                    }
                }

                var characters = so.FindProperty("m_Characters");
                Check(report, problems, characters.arraySize > 0,
                    $"選べるキャラクター数 ({characters.arraySize})");
            }

            // ---- 撮影 -----------------------------------------------------------
            var capture = Object.FindObjectOfType<ARPhotoCapture>();
            Check(report, problems, capture != null, "ARPhotoCapture");

            if (capture != null)
            {
                var captureSo = new SerializedObject(capture);
                var hidden = captureSo.FindProperty("m_HideWhileCapturing").arraySize;
                Check(report, problems, hidden > 0, $"撮影時に隠す UI ({hidden} 件)");
            }

            // ---- UI パネルの網羅性 ------------------------------------------
            if (uiController != null)
            {
                var so = new SerializedObject(uiController);
                var panels = so.FindProperty("m_Panels");
                var covered = new HashSet<int>();

                for (var i = 0; i < panels.arraySize; i++)
                {
                    var element = panels.GetArrayElementAtIndex(i);
                    if (element.FindPropertyRelative("root").objectReferenceValue != null)
                        covered.Add(element.FindPropertyRelative("phase").enumValueIndex);
                }

                // Booting と RequestingPermission は「何も出さない」が正しいので除外
                var required = new[]
                {
                    ARAppPhase.ExplainingPermission, ARAppPhase.PermissionDenied,
                    ARAppPhase.CheckingSupport, ARAppPhase.Unsupported,
                    ARAppPhase.Scanning, ARAppPhase.ReadyToPlace, ARAppPhase.Placed,
                };

                foreach (var phase in required)
                    Check(report, problems, covered.Contains((int)phase), $"UI パネル: {phase}");
            }

            // ---- ビルド設定 ---------------------------------------------------
            var inBuild = EditorBuildSettings.scenes.Any(s => s.path == k_ScenePath && s.enabled);
            Check(report, problems, inBuild, "Build Settings に登録済み");

            // ---- 結果 -----------------------------------------------------------
            report.AppendLine();
            if (problems.Count == 0)
            {
                report.AppendLine("問題は見つかりませんでした。");
                Debug.Log(report.ToString());
            }
            else
            {
                report.AppendLine($"{problems.Count} 件の問題があります:");
                foreach (var p in problems)
                    report.AppendLine($"  - {p}");

                Debug.LogError(report.ToString());
            }
        }

        static void CheckSerializedRefs(StringBuilder report, List<string> problems, Component component)
        {
            var so = new SerializedObject(component);
            var property = so.GetIterator();
            var typeName = component.GetType().Name;

            var enterChildren = true;
            while (property.NextVisible(enterChildren))
            {
                enterChildren = false;

                if (property.propertyType != SerializedPropertyType.ObjectReference)
                    continue;

                // 任意項目(音など)は未設定でも構わない
                if (property.name is "m_Script" or "m_EntranceSound")
                    continue;

                if (property.objectReferenceValue == null)
                {
                    var label = $"{typeName}.{property.name} が未設定";
                    problems.Add(label);
                    report.AppendLine($"  NG  {label}");
                }
            }
        }

        static void Check(StringBuilder report, List<string> problems, bool ok, string label)
        {
            report.AppendLine(ok ? $"  OK  {label}" : $"  NG  {label}");

            if (!ok)
                problems.Add(label);
        }
    }
}
