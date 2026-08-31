using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnityEditor;
using UnityEditor.Animations;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace ARCharacterApp.EditorTools
{
    /// <summary>
    /// 持ち込んだ Anomea 一式から、AR で使うキャラクタープレハブを組み立てる。
    ///
    /// VRM から取り込んだときは 16 個のメッシュがばらけて衣装が剥がれていたが、
    /// この FBX は全メッシュが Hips を根とする 287 ボーンの単一スケルトンを共有しているので、
    /// ポーズを付けても衣装が付いてくる。
    /// マテリアルも作者が lilToon で作り込んだものがそのまま使えるため、変換もしない。
    /// </summary>
    public static class AnomeaCharacterSetup
    {
        const string k_SourcePrefab = "Assets/Anomea/Prefab/Anomea_pre_1 Variant.prefab";
        const string k_AnimationRoot = "Assets/Anomea/Animations_v1.0.1/Animation";
        const string k_PoseDir = k_AnimationRoot + "/Pose";
        const string k_OutputPrefab = "Assets/Prefabs/Character.prefab";
        const string k_ControllerPath = "Assets/Anomea/PoseController.controller";

        const string k_ScenePath = "Assets/Scenes/AR.unity";

        /// <summary>
        /// 2 体目のキャラクター。
        /// ポーズは Humanoid のリターゲットで動くのでボーン名が違っても使える。
        /// 表情は BlendShape 名の一致が要るが、実測で 184 種中 177 種が一致した
        /// (足りないのは EyePatch 系 6 種と Eye_>< の計 7 種で、このモデルには無い装備)。
        /// </summary>
        const string k_ExtraFbx = "Assets/Hiasobi/FBX/Hiasobi.fbx";
        const string k_ExtraPrefab = "Assets/Prefabs/Hiasobi.prefab";
        const string k_ExtraLabel = "ダーク今井ちゃん";

        /// <summary>
        /// 2 体目を入れるかどうか。
        ///
        /// 最初の公開は今井ちゃん 1 体だけにする。ダーク今井ちゃんは
        /// 見た目の確認が済んでいるので、出すときはこれを true に戻すだけでよい。
        /// </summary>
        const bool k_IncludeExtraCharacter = false;

        /// <summary>
        /// レイヤー 0 に載せる全身ポーズ。State 名は AnimationClip 名と同じ。
        /// 表示名は実機で見て決め直す前提の仮置き。
        /// </summary>
        static readonly (string clip, string label)[] k_Poses =
        {
            ("Anomea_stand_still",    "たつ"),
            ("Anomea_sit",            "すわる"),
            ("Anomea_crouch_still",   "しゃがむ"),
            ("Anomea_low_crawl_still","ふせる"),
            ("Anomea_AFK",            "おやすみ"),
            ("pose_1",  "ポーズ1"),  ("pose_2",  "ポーズ2"),
            ("pose_3",  "ポーズ3"),  ("pose_4",  "ポーズ4"),
            ("pose_5",  "ポーズ5"),  ("pose_6",  "ポーズ6"),
            ("pose_7",  "ポーズ7"),  ("pose_8",  "ポーズ8"),
            ("pose_9",  "ポーズ9"),  ("pose_10", "ポーズ10"),
            ("pose_11", "ポーズ11"), ("pose_12", "ポーズ12"),
            ("pose_13", "ポーズ13"), ("pose_14", "ポーズ14"),
            ("pose_15", "ポーズ15"),
        };

        /// <summary>
        /// レイヤー 1 に載せる表情。BlendShape しか動かさないので、
        /// ポーズと同時に成立する(アバターマスクも不要)。
        /// </summary>
        static readonly string[] k_ExpressionDirs =
        {
            k_AnimationRoot + "/Face",          // FaceEmote__Default(素の顔)がここにある
            k_AnimationRoot + "/Face/Set_1",
            k_AnimationRoot + "/Face/Set_2",
            k_AnimationRoot + "/Face/Set_3",
        };

        /// <summary>
        /// 素の顔にあたるクリップ。
        ///
        /// このアバターは「BlendShape が全部 0」が素の顔ではない。
        /// 何も当てないと目や口のモーフが中途半端な位置に残り、
        /// 顔に赤い矩形が出たり目が閉じたままになる。
        /// VRChat では常に表情レイヤーが効いている前提で作られているため、
        /// 既定の顔を作る専用クリップ(BlendShape カーブ 458 本)が用意されている。
        /// </summary>
        const string k_NeutralFaceClip = "FaceEmote__Default";

        /// <summary>
        /// 「ふつう」を表す、クリップを持たない State の名前。
        /// 何も書かないので、素の顔レイヤーの結果がそのまま出る。
        /// </summary>
        const string k_NoExpressionState = "NoExpression";

        /// <summary>追加のポーズ集(UniSakiStudio KawaiiPosing)。</summary>
        const string k_KawaiiPoseDir = "Assets/KawaiiPosing/Animations";

        /// <summary>
        /// 着せ替えできるパーツ。FBX 内の SkinnedMeshRenderer 名と対応させる。
        /// 体と下着(Body / Body_2 / Bikini / Pants / Tops)は外せないようにしてある。
        /// </summary>
        /// <summary>
        /// 着せ替えできるパーツ。
        ///
        /// shapes は「そのパーツを脱いだら 0 に戻す BlendShape」。
        /// この手のアバターは服の下で体を細らせて貫通を防いでいるので、
        /// 脱いだときに戻さないと痩せたままの体が出てしまう。
        /// アウターは袖で上腕を覆うため Shrink_UpperArm が効いている。
        /// (前腕もおかしければ Shrink_LowerArm / Shrink_Elbow を足せばよい)
        /// </summary>
        static readonly (string renderer, string label, bool on, string[] shapes)[] k_WardrobeParts =
        {
            ("Outer",      "アウター",   true, new[] { "Shrink_UpperArm" }),
            ("Shoes",      "くつ",       true, null),
            ("Socks",      "ソックス",   true, null),
            ("BandAid",    "ばんそうこう", true, null),
            ("Collar",     "チョーカー", true, null),
            ("EyePatch",   "アイパッチ", true, null),
            ("HairRibbon", "髪リボン",   true, null),
            ("HeadDress",  "ヘッドドレス", true, null),
            ("Halo",       "ヘイロー",   true, null),
            ("Wing",       "はね",       true, null),
        };

        /// <summary>
        /// KawaiiPosing のクリップ名は "分類_ローマ字" になっている。
        /// 分類は日本語の見出しに、後半はよく使うものだけ日本語に置き換える。
        /// 表に無いものはローマ字のまま出す(読めれば十分なので全部は訳さない)。
        /// </summary>
        static readonly Dictionary<string, string> k_KawaiiCategory = new()
        {
            { "SitStand",      "立" },
            { "SitShallow",    "座" },
            { "SitDeep",       "座深" },
            { "SitSleepUp",    "寝" },
            { "SitSleepDown",  "伏" },
        };

        static readonly Dictionary<string, string> k_KawaiiName = new()
        {
            // 立ち
            { "Banzai", "ばんざい" },   { "Bikkuri", "びっくり" },
            { "Idol", "アイドル" },     { "Tere", "てれ" },
            { "Osumashi", "おすまし" }, { "Nonbiri", "のんびり" },
            { "Keikai", "けいかい" },   { "Hukigen", "ふきげん" },
            { "Kiritsu", "きりつ" },    { "Machibouke", "まちぼうけ" },
            { "Zombie", "ゾンビ" },     { "Boukansha", "ぼうかん" },
            { "Haritsuke", "はりつけ" },{ "Teibou", "ていぼう" },
            { "KouhouKareshi", "こうはい" },

            // 座り
            { "Agura", "あぐら" },      { "Agura2", "あぐら2" },
            { "Girl", "女の子ずわり" }, { "Ashikumi", "あしくみ" },
            { "Ashikumi2", "あしくみ2" },{ "Relax", "リラックス" },
            { "Relax2", "リラックス2" },{ "Relax3", "リラックス3" },
            { "Uchimata", "うちまた" }, { "Hizatate", "ひざたて" },
            { "Gasseki", "がっせき" },  { "Sonkyo", "そんきょ" },
            { "Yorikakari", "よりかかり" },
            { "Ashiburan", "あしぶらん" },
            { "Nekorobi", "ねころび" },

            // 寝
            { "Hirune", "ひるね" },     { "Sleep", "すやすや" },
            { "Dakishime", "だきしめ" },{ "Otsukare", "おつかれ" },
            { "Straight", "まっすぐ" }, { "Kaeru", "かえる" },
            { "Gudari", "ぐだり" },     { "Nonbiri2", "のんびり2" },
            { "Kutsurogi", "くつろぎ" },{ "Uzukumari", "うずくまり" },
        };

        [MenuItem("AR Character App/Anomea をキャラクターとして組み込む", priority = 61)]
        public static void Setup()
        {
            var controller = BuildPoseController();
            if (controller == null)
                return;

            var prefab = BuildCharacterPrefab(controller);
            if (prefab == null)
                return;

            // 2 体目。シーンを開く前に作っておく。
            var extra = BuildExtraCharacter(controller);

            WireIntoScene(prefab, extra);
        }

        /// <summary>
        /// ポーズ(レイヤー 0)と表情(レイヤー 1)を持つ Animator Controller を作る。
        ///
        /// 分けている理由: 表情は BlendShape だけを動かすクリップなので、
        /// 全身ポーズと同じレイヤーに置くと片方しか再生できない。
        /// レイヤーを分けると「このポーズでこの表情」が自由に組める。
        /// </summary>
        static AnimatorController BuildPoseController()
        {
            var poseClips = CollectClips(new[] { k_PoseDir, k_KawaiiPoseDir });
            if (poseClips.Count == 0)
            {
                Debug.LogError($"[AnomeaCharacterSetup] {k_PoseDir} にポーズクリップが見つかりません。");
                return null;
            }

            // ポーズは体の姿勢だけを持つべきなので、Humanoid のマッスルと Root 以外を
            // 落とした複製に差し替える。持ち込んだポーズは VRChat 向けで、
            // 表情の BlendShape・髪のボーン・メッシュの表示/非表示まで書き込んでおり、
            // そのままだと表情レイヤーや Wardrobe と取り合いになる。
            poseClips = PoseClipSanitizer.KeepHumanoidOnly(poseClips);

            var expressionClips = CollectClips(k_ExpressionDirs);

            if (File.Exists(k_ControllerPath))
                AssetDatabase.DeleteAsset(k_ControllerPath);

            var controller = AnimatorController.CreateAnimatorControllerAtPath(k_ControllerPath);

            var report = new StringBuilder();
            report.AppendLine("=== ポーズ / 表情 Controller ===");

            // ---- レイヤー 0: ポーズ ------------------------------------------
            var layers = controller.layers;
            layers[0].name = "Pose";

            // 首から上だけをカメラに向ける処理(ARCharacterHeadLook)が
            // OnAnimatorIK を使うので、このレイヤーで IK Pass を通す。
            layers[0].iKPass = true;

            controller.layers = layers;

            // ポーズ側は writeDefaults を切る。
            // 有効なままだと、切り替えの補間中に「クリップが持たない値 = 既定値」が
            // 混ざり込み、肘と膝を曲げて地面に埋まった中間姿勢を経由してしまう。
            // ポーズクリップは全身のマッスルを持っているので、切っても抜けは出ない。
            var poseAdded = AddStates(controller.layers[0].stateMachine,
                PoseOrder(poseClips).Select(p => p.clip), poseClips, writeDefaults: false);
            report.AppendLine($"  ポーズ  : {poseAdded} 件");

            // ---- レイヤー 1: 素の顔 / レイヤー 2: 選んだ表情 --------------------
            //
            // 表情を「切り替え」にするために 2 枚に分けている。
            //
            //   レイヤー 1 … FaceEmote__Default を常に流す。Write Defaults は ON。
            //                毎フレーム顔を素の状態に戻す土台になる。
            //   レイヤー 2 … 選んだ表情。Write Defaults は OFF。
            //                そのクリップが持つ BlendShape だけを上書きし、
            //                持たない分はレイヤー 1 の素の顔が出る。
            //
            // 1 枚でやると、前に選んだ表情の口や目が残って混ざってしまう。
            var expressionNames = ExpressionOrder(expressionClips);
            var expressionAdded = 0;

            if (expressionClips.ContainsKey(k_NeutralFaceClip))
            {
                controller.AddLayer("FaceBase");

                layers = controller.layers;
                layers[1].defaultWeight = 1f;
                controller.layers = layers;

                AddStates(controller.layers[1].stateMachine,
                    new[] { k_NeutralFaceClip }, expressionClips, writeDefaults: true);
            }

            if (expressionNames.Count > 0)
            {
                controller.AddLayer("FaceExpression");

                layers = controller.layers;
                layers[^1].defaultWeight = 1f;
                controller.layers = layers;

                // 先頭は「ふつう」用の空 State。
                // 何も書かないので、レイヤー 1 の素の顔がそのまま出る。
                var stateMachine = controller.layers[^1].stateMachine;
                var neutral = stateMachine.AddState(k_NoExpressionState);
                neutral.writeDefaultValues = false;
                stateMachine.defaultState = neutral;
                expressionAdded = 1;

                expressionAdded += AddStates(stateMachine,
                    expressionNames.Where(n => n != k_NeutralFaceClip),
                    expressionClips, writeDefaults: false);
            }

            report.AppendLine($"  表情    : {expressionAdded} 件");

            EditorUtility.SetDirty(controller);
            AssetDatabase.SaveAssets();

            Debug.Log(report.ToString());
            return controller;
        }

        static Dictionary<string, AnimationClip> CollectClips(IEnumerable<string> folders)
        {
            var clips = new Dictionary<string, AnimationClip>();
            var valid = folders.Where(AssetDatabase.IsValidFolder).ToArray();

            if (valid.Length == 0)
                return clips;

            foreach (var guid in AssetDatabase.FindAssets("t:AnimationClip", valid))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var clip = AssetDatabase.LoadAssetAtPath<AnimationClip>(path);

                if (clip != null && !clips.ContainsKey(clip.name))
                    clips[clip.name] = clip;
            }

            return clips;
        }

        /// <summary>
        /// 表情の並び。素の顔を必ず先頭に置き、そのあとをクリップ名順にする。
        /// 先頭が Animator の既定 State になるので、起動直後の顔がこれで決まる。
        /// </summary>
        static List<string> ExpressionOrder(Dictionary<string, AnimationClip> clips)
        {
            var ordered = new List<string>();

            if (clips.ContainsKey(k_NeutralFaceClip))
                ordered.Add(k_NeutralFaceClip);

            ordered.AddRange(clips.Keys
                .Where(n => n != k_NeutralFaceClip)
                // 素の顔を打ち消すためだけの補助クリップは選択肢に出さない
                .Where(n => !n.StartsWith("FaceEmote_AFK", System.StringComparison.Ordinal))
                .Where(n => !n.StartsWith("Blink_", System.StringComparison.Ordinal))
                .Where(n => !n.StartsWith("MouthMorphCanceller", System.StringComparison.Ordinal))
                .OrderBy(n => n, System.StringComparer.Ordinal));

            return ordered;
        }

        /// <summary>
        /// ポーズの並び順と表示名を決める。
        /// もともと入っていた素立ちなどを先頭に置き、そのあとに KawaiiPosing を分類順で続ける。
        /// </summary>
        static List<(string clip, string label)> PoseOrder(Dictionary<string, AnimationClip> clips)
        {
            var ordered = new List<(string clip, string label)>();
            var used = new HashSet<string>();

            foreach (var (clip, label) in k_Poses)
            {
                if (clips.ContainsKey(clip) && used.Add(clip))
                    ordered.Add((clip, label));
            }

            // KawaiiPosing は分類ごとにまとめる。立ち → 座り → 寝 の順が使いやすい。
            var categoryOrder = new[] { "SitStand", "SitShallow", "SitDeep", "SitSleepUp", "SitSleepDown" };

            foreach (var category in categoryOrder)
            {
                var inCategory = clips.Keys
                    .Where(n => n.StartsWith(category + "_", System.StringComparison.Ordinal))
                    .OrderBy(n => n, System.StringComparer.Ordinal);

                foreach (var name in inCategory)
                {
                    if (used.Add(name))
                        ordered.Add((name, ToKawaiiLabel(name)));
                }
            }

            return ordered;
        }

        /// <summary>"SitStand_Banzai" → "立 ばんざい" のように読める形にする。</summary>
        static string ToKawaiiLabel(string clipName)
        {
            var split = clipName.IndexOf('_');
            if (split < 0)
                return clipName;

            var category = clipName.Substring(0, split);
            var name = clipName.Substring(split + 1);

            var prefix = k_KawaiiCategory.TryGetValue(category, out var c) ? c : category;
            var body = k_KawaiiName.TryGetValue(name, out var j) ? j : name;

            return $"{prefix} {body}";
        }

        static int AddStates(AnimatorStateMachine stateMachine, IEnumerable<string> names,
            Dictionary<string, AnimationClip> clips, bool writeDefaults)
        {
            AnimatorState first = null;
            var added = 0;

            foreach (var name in names)
            {
                if (!clips.TryGetValue(name, out var clip))
                    continue;

                var state = stateMachine.AddState(name);
                state.motion = clip;
                state.speed = 1f;
                state.writeDefaultValues = writeDefaults;

                first ??= state;
                added++;
            }

            // 既に既定 State が決まっていれば触らない
            // (表情レイヤーは「ふつう」を先に置いてあるため)
            if (first != null && stateMachine.defaultState == null)
                stateMachine.defaultState = first;

            return added;
        }

        static GameObject BuildCharacterPrefab(AnimatorController controller)
        {
            if (AssetDatabase.LoadAssetAtPath<GameObject>(k_SourcePrefab) == null)
            {
                Debug.LogError($"[AnomeaCharacterSetup] {k_SourcePrefab} が見つかりません。");
                return null;
            }

            // 元プレハブは Anomea.fbx → Anomea Variant → Anomea_pre_1 Variant という
            // 3 段のバリアントになっている。インスタンスを作って SaveAsPrefabAsset すると
            // さらにその子バリアントを作ろうとして失敗するので、
            // アセットとして複製してから中身を直接編集する。
            if (File.Exists(k_OutputPrefab))
                AssetDatabase.DeleteAsset(k_OutputPrefab);

            if (!AssetDatabase.CopyAsset(k_SourcePrefab, k_OutputPrefab))
            {
                Debug.LogError($"[AnomeaCharacterSetup] {k_SourcePrefab} を複製できませんでした。");
                return null;
            }

            AssetDatabase.Refresh();

            var root = PrefabUtility.LoadPrefabContents(k_OutputPrefab);
            if (root == null)
            {
                Debug.LogError($"[AnomeaCharacterSetup] {k_OutputPrefab} を開けませんでした。");
                return null;
            }

            try
            {
                root.name = "Character";
                root.transform.position = Vector3.zero;
                root.transform.rotation = Quaternion.identity;

                // 元は VRChat 用のアバターなので、VRC SDK のコンポーネント
                // (AvatarDescriptor / PhysBone など)が付いている。
                // このプロジェクトには SDK が無いため missing script になり、
                // Unity はそれを含むプレハブの保存を拒否する。先に落とす。
                var removed = RemoveMissingScripts(root);
                if (removed > 0)
                    Debug.Log($"[AnomeaCharacterSetup] VRChat 用の missing script を {removed} 件除去しました。");

                var animator = root.GetComponentInChildren<Animator>();
                if (animator == null)
                {
                    Debug.LogError("[AnomeaCharacterSetup] Animator が見つかりません。");
                    return null;
                }

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                // AR 側から触るコンポーネントを載せる
                if (root.GetComponent<ARCharacter>() == null)
                    root.AddComponent<ARCharacter>();

                var poser = root.GetComponent<ARCharacterPoser>() ?? root.AddComponent<ARCharacterPoser>();
                WirePoser(poser, animator);

                var breathing = root.GetComponent<ARCharacterBreathing>() ?? root.AddComponent<ARCharacterBreathing>();
                var breathSo = new SerializedObject(breathing);
                breathSo.FindProperty("m_Animator").objectReferenceValue = animator;
                breathSo.ApplyModifiedPropertiesWithoutUndo();

                var wardrobe = root.GetComponent<ARCharacterWardrobe>() ?? root.AddComponent<ARCharacterWardrobe>();
                WireWardrobe(wardrobe, root);

                // OnAnimatorIK は Animator と同じ GameObject でしか呼ばれないので、
                // ルートではなく Animator が載っているオブジェクトに付ける。
                var headLookGo = animator.gameObject;
                var headLook = headLookGo.GetComponent<ARCharacterHeadLook>()
                               ?? headLookGo.AddComponent<ARCharacterHeadLook>();
                var headSo = new SerializedObject(headLook);
                headSo.FindProperty("m_Animator").objectReferenceValue = animator;
                headSo.ApplyModifiedPropertiesWithoutUndo();


                PrefabUtility.SaveAsPrefabAsset(root, k_OutputPrefab, out var ok);
                if (!ok)
                {
                    Debug.LogError($"[AnomeaCharacterSetup] {k_OutputPrefab} の保存に失敗しました。");
                    return null;
                }
            }
            finally
            {
                PrefabUtility.UnloadPrefabContents(root);
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();

            var saved = AssetDatabase.LoadAssetAtPath<GameObject>(k_OutputPrefab);
            Debug.Log($"[AnomeaCharacterSetup] {k_OutputPrefab} を作成しました " +
                      $"(Renderer {saved.GetComponentsInChildren<Renderer>(true).Length} 個)。");

            return saved;
        }

        /// <summary>
        /// FBX からもう 1 体キャラクターを組み立てる。
        ///
        /// Anomea と違って元プレハブが無いので、FBX から直接プレハブを作る。
        /// Animator Controller は共有する。ポーズは Humanoid のマッスルなので
        /// ボーン名が違っても効き、表情は BlendShape 名で当たる。
        /// </summary>
        static GameObject BuildExtraCharacter(AnimatorController controller)
        {
            if (!k_IncludeExtraCharacter)
            {
                Debug.Log($"[AnomeaCharacterSetup] 2 体目「{k_ExtraLabel}」は今回入れません。");
                return null;
            }

            if (AssetImporter.GetAtPath(k_ExtraFbx) is not ModelImporter importer)
            {
                Debug.LogWarning($"[AnomeaCharacterSetup] {k_ExtraFbx} がありません。2 体目は作りません。");
                return null;
            }

            // ポーズを使うには Humanoid でないといけない。
            //
            // メッシュ圧縮はかけない。一度 High を試したが、頂点座標が量子化されて
            // 表面がシワシワに潰れた。このモデルは服のディテールが細かいので、
            // 量子化の誤差がそのまま見た目に出る。容量より形を優先する。
            //
            // BlendShape の法線・接線の差分だけは捨てる。305 個あるので
            // (305 × 頂点数 × 位置・法線・接線) で差分データが 1.1GB になり、
            // 位置だけに絞ると 3 分の 1 で済む。トゥーン表現なので、
            // 表情で法線が変わらなくても見た目にはほぼ出ない。
            var needsReimport =
                importer.animationType != ModelImporterAnimationType.Human
                || importer.importBlendShapeNormals != ModelImporterNormals.None
                || importer.meshCompression != ModelImporterMeshCompression.Off
                || importer.optimizeMeshPolygons
                || importer.optimizeMeshVertices;

            if (needsReimport)
            {
                importer.animationType = ModelImporterAnimationType.Human;
                importer.importBlendShapeNormals = ModelImporterNormals.None;
                importer.meshCompression = ModelImporterMeshCompression.Off;
                importer.optimizeMeshPolygons = false;
                importer.optimizeMeshVertices = false;
                importer.SaveAndReimport();
                Debug.Log($"[AnomeaCharacterSetup] {k_ExtraFbx} を Humanoid で取り込みました（メッシュ圧縮なし）。");
            }

            var source = AssetDatabase.LoadAssetAtPath<GameObject>(k_ExtraFbx);
            if (source == null)
            {
                Debug.LogError($"[AnomeaCharacterSetup] {k_ExtraFbx} を読み込めません。");
                return null;
            }

            var instance = (GameObject)PrefabUtility.InstantiatePrefab(source);
            if (instance == null)
                return null;

            try
            {
                var animator = instance.GetComponentInChildren<Animator>();
                if (animator == null || animator.avatar == null || !animator.avatar.isHuman)
                {
                    Debug.LogError("[AnomeaCharacterSetup] 2 体目が Humanoid になっていません。");
                    return null;
                }

                // FBX に残っているのはマテリアル名だけで、中身は Standard になっている。
                // 1 体目と同じく lilToon のものに貼り替える。
                HiasobiMaterialSetup.Apply(instance);

                animator.runtimeAnimatorController = controller;
                animator.applyRootMotion = false;
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;

                if (instance.GetComponent<ARCharacter>() == null)
                    instance.AddComponent<ARCharacter>();

                var poser = instance.GetComponent<ARCharacterPoser>() ?? instance.AddComponent<ARCharacterPoser>();
                WirePoser(poser, animator);

                var breathing = instance.GetComponent<ARCharacterBreathing>() ?? instance.AddComponent<ARCharacterBreathing>();
                var breathSo = new SerializedObject(breathing);
                breathSo.FindProperty("m_Animator").objectReferenceValue = animator;
                breathSo.ApplyModifiedPropertiesWithoutUndo();

                // 着せ替えは、このモデルに実在するメッシュだけが登録される
                var wardrobe = instance.GetComponent<ARCharacterWardrobe>() ?? instance.AddComponent<ARCharacterWardrobe>();
                WireWardrobe(wardrobe, instance);

                var headLookGo = animator.gameObject;
                var headLook = headLookGo.GetComponent<ARCharacterHeadLook>()
                               ?? headLookGo.AddComponent<ARCharacterHeadLook>();
                var headSo = new SerializedObject(headLook);
                headSo.FindProperty("m_Animator").objectReferenceValue = animator;
                headSo.ApplyModifiedPropertiesWithoutUndo();

                var saved = PrefabUtility.SaveAsPrefabAsset(instance, k_ExtraPrefab, out var ok);
                if (!ok)
                {
                    Debug.LogError($"[AnomeaCharacterSetup] {k_ExtraPrefab} の保存に失敗しました。");
                    return null;
                }

                var renderers = instance.GetComponentsInChildren<SkinnedMeshRenderer>(true);
                var shapes = renderers.Sum(r => r.sharedMesh != null ? r.sharedMesh.blendShapeCount : 0);
                Debug.Log($"[AnomeaCharacterSetup] 2 体目「{k_ExtraLabel}」を作成: "
                    + $"メッシュ {renderers.Length} / BlendShape {shapes}");

                return saved;
            }
            finally
            {
                Object.DestroyImmediate(instance);
            }
        }

        /// <summary>
        /// 参照先スクリプトが解決できないコンポーネントを全階層から取り除く。
        /// 戻り値は取り除いた数。
        /// </summary>
        static int RemoveMissingScripts(GameObject root)
        {
            var removed = 0;

            foreach (var transform in root.GetComponentsInChildren<Transform>(true))
                removed += GameObjectUtility.RemoveMonoBehavioursWithMissingScript(transform.gameObject);

            return removed;
        }

        static void WirePoser(ARCharacterPoser poser, Animator animator)
        {
            var so = new SerializedObject(poser);
            so.FindProperty("m_Animator").objectReferenceValue = animator;

            // ポーズ: Controller に載せたのと同じ並びで
            var poseClips = CollectClips(new[] { k_PoseDir, k_KawaiiPoseDir });
            FillEntries(so.FindProperty("m_Poses"), PoseOrder(poseClips).ToArray());

            // 表情。
            //
            // 「ふつう」(クリップを持たない State)は一覧に出さない。
            // Animator の既定 State としては残るので、起動直後は素の顔で始まる。
            //
            // 顔に重ねる Overlay の板は、表情によっては不透明な面として出てしまうが、
            // 選別はしない。どれが使えるかは実際に見て決める。
            var expressionClips = CollectClips(k_ExpressionDirs);
            var expressions = ExpressionOrder(expressionClips)
                .Where(name => name != k_NeutralFaceClip)
                .Select(name => (clip: name, label: ToExpressionLabel(name)))
                .ToArray();

            FillEntries(so.FindProperty("m_Expressions"), expressions);

            so.ApplyModifiedPropertiesWithoutUndo();
        }

        /// <summary>着せ替えできるパーツを登録する。実在するメッシュだけを載せる。</summary>
        static void WireWardrobe(ARCharacterWardrobe wardrobe, GameObject root)
        {
            var present = root.GetComponentsInChildren<Renderer>(true)
                .Select(r => r.name)
                .ToHashSet();

            var parts = k_WardrobeParts.Where(p => present.Contains(p.renderer)).ToArray();

            var so = new SerializedObject(wardrobe);
            var list = so.FindProperty("m_Parts");
            list.arraySize = parts.Length;

            for (var i = 0; i < parts.Length; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("displayName").stringValue = parts[i].label;
                element.FindPropertyRelative("rendererName").stringValue = parts[i].renderer;
                element.FindPropertyRelative("defaultOn").boolValue = parts[i].on;

                var shapes = element.FindPropertyRelative("shrinkShapes");
                var names = parts[i].shapes ?? System.Array.Empty<string>();
                shapes.arraySize = names.Length;

                for (var j = 0; j < names.Length; j++)
                    shapes.GetArrayElementAtIndex(j).stringValue = names[j];
            }

            so.ApplyModifiedPropertiesWithoutUndo();

            var missing = k_WardrobeParts.Where(p => !present.Contains(p.renderer)).ToArray();
            if (missing.Length > 0)
            {
                Debug.LogWarning("[AnomeaCharacterSetup] 見つからなかったパーツ: " +
                                 string.Join(", ", missing.Select(m => m.renderer)));
            }
        }

        static void FillEntries(SerializedProperty list, (string clip, string label)[] entries)
        {
            list.arraySize = entries.Length;

            for (var i = 0; i < entries.Length; i++)
            {
                var element = list.GetArrayElementAtIndex(i);
                element.FindPropertyRelative("stateName").stringValue = entries[i].clip;
                element.FindPropertyRelative("displayName").stringValue = entries[i].label;
            }
        }

        /// <summary>
        /// "Set1_ThumbsUp_L" のようなクリップ名を、ボタンに出せる短い名前にする。
        /// 元の名前は VRChat のジェスチャ由来で、そのままだと長すぎて読めない。
        /// </summary>
        static string ToExpressionLabel(string clipName)
        {
            var name = clipName;

            var underscore = name.IndexOf('_');
            if (underscore >= 0 && underscore + 1 < name.Length)
                name = name.Substring(underscore + 1);

            // 左右違いは末尾の _L / _R を記号に置き換えて短くする
            if (name.EndsWith("_L", System.StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 2) + " 左";
            else if (name.EndsWith("_R", System.StringComparison.Ordinal))
                name = name.Substring(0, name.Length - 2) + " 右";

            return name.Replace('_', ' ');
        }

        static void WireIntoScene(GameObject prefab, GameObject extra)
        {
            var scene = EditorSceneManager.OpenScene(k_ScenePath, OpenSceneMode.Single);

            var placement = Object.FindObjectOfType<ARPlacementController>();
            if (placement != null)
            {
                var so = new SerializedObject(placement);
                so.FindProperty("m_CharacterPrefab").objectReferenceValue = prefab;

                // 差し替えられるキャラクター一覧。
                var entries = new List<(string label, GameObject prefab)> { ("今井ちゃん", prefab) };

                // 2 体目。ポーズと表情は今井ちゃんと共有する。
                if (extra != null)
                    entries.Add((k_ExtraLabel, extra));

                var list = so.FindProperty("m_Characters");
                list.arraySize = entries.Count;

                for (var i = 0; i < entries.Count; i++)
                {
                    var element = list.GetArrayElementAtIndex(i);
                    element.FindPropertyRelative("displayName").stringValue = entries[i].label;
                    element.FindPropertyRelative("prefab").objectReferenceValue = entries[i].prefab;
                }

                so.ApplyModifiedPropertiesWithoutUndo();
            }

            var fallback = Object.FindObjectOfType<NonARFallbackViewer>(true);
            if (fallback != null)
            {
                var so = new SerializedObject(fallback);
                so.FindProperty("m_CharacterPrefab").objectReferenceValue = prefab;
                so.ApplyModifiedPropertiesWithoutUndo();
            }

            EditorSceneManager.MarkSceneDirty(scene);
            EditorSceneManager.SaveScene(scene);

            Debug.Log("[AnomeaCharacterSetup] シーンのキャラクター参照を差し替えました。");
        }
    }
}
