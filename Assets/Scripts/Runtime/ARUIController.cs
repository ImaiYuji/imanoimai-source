using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace ARCharacterApp
{
    /// <summary>
    /// フェーズに応じて画面を出し分け、設置後は撮影と選択(ポーズ/表情/キャラ)を受け持つ。
    ///
    /// 選択肢はポーズ 21・表情 39 と数が多く、数もモデル次第で変わるので、
    /// ボタンは固定で持たず実行時に必要な数だけ作る。
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARUIController : MonoBehaviour
    {
        [Serializable]
        public sealed class PhasePanel
        {
            public ARAppPhase phase;
            public GameObject root;
        }

        enum Tab
        {
            Pose,
            Expression,
            Wardrobe,
            Character,
        }

        [Header("Flow")]
        [SerializeField] ARAppFlow m_Flow;
        [SerializeField] ARPlacementController m_Placement;
        [SerializeField] ARPhotoCapture m_PhotoCapture;

        [Header("Panels")]
        [SerializeField] List<PhasePanel> m_Panels = new List<PhasePanel>();

        [Header("Scanning")]
        [SerializeField] Text m_ScanLabel;
        [SerializeField] string m_ScanText = "周りをゆっくり映してください";
        [SerializeField] string m_ScanHintText = "床や机など、模様のある平らな面に向けてみてください";

        [Header("Buttons")]
        [SerializeField] Button m_ContinueButton;
        [SerializeField] Button m_OpenSettingsButton;
        [SerializeField] Button m_RetryButton;
        [SerializeField] Button m_ResetButton;
        [SerializeField] Button m_ShutterButton;
        [SerializeField] Button m_FaceCameraButton;

        [Header("Selector")]
        [SerializeField] Button[] m_TabButtons = Array.Empty<Button>();
        [SerializeField] RectTransform m_SelectorContent;
        [SerializeField] GameObject m_SelectorPanel;
        [SerializeField] Button m_OpenSelectorButton;
        [SerializeField] Button m_CloseSelectorButton;

        [Header("Colors")]
        [SerializeField] Color m_NormalColor = new Color(1f, 1f, 1f, 0.78f);
        [SerializeField] Color m_SelectedColor = new Color(0.98f, 0.55f, 0.73f, 1f);
        [SerializeField] Color m_TabNormalColor = new Color(1f, 1f, 1f, 0.55f);
        [SerializeField] Color m_TabSelectedColor = new Color(0.99f, 0.68f, 0.81f, 1f);
        [SerializeField] Color m_ItemTextColor = new Color(0.36f, 0.16f, 0.24f, 1f);

        [Tooltip("選択肢ボタンの角丸スプライト。実行時に作るのでここから渡す。")]
        [SerializeField] Sprite m_ItemSprite;

        ARCharacterPoser m_Poser;
        ARCharacterWardrobe m_Wardrobe;
        Tab m_Tab = Tab.Pose;

        /// <summary>いま並んでいるボタン。タブを切り替えるたびに作り直す。</summary>
        readonly List<Button> m_ItemButtons = new List<Button>();

        int m_LastPoseIndex = -1;
        int m_LastExpressionIndex = -1;

        void Awake()
        {
            if (m_Flow == null)
                m_Flow = FindObjectOfType<ARAppFlow>();

            if (m_ContinueButton != null)
                m_ContinueButton.onClick.AddListener(() => m_Flow.ConfirmPermissionExplanation());

            if (m_OpenSettingsButton != null)
                m_OpenSettingsButton.onClick.AddListener(OpenAppSettings);

            if (m_RetryButton != null)
                m_RetryButton.onClick.AddListener(() => m_Flow.RetryFromDenied());

            if (m_ResetButton != null)
                m_ResetButton.onClick.AddListener(() => m_Flow.ResetPlacement());

            if (m_ShutterButton != null && m_PhotoCapture != null)
                m_ShutterButton.onClick.AddListener(m_PhotoCapture.Capture);

            if (m_FaceCameraButton != null && m_Placement != null)
                m_FaceCameraButton.onClick.AddListener(ToggleHeadLook);

            for (var i = 0; i < m_TabButtons.Length; i++)
            {
                if (m_TabButtons[i] == null)
                    continue;

                var tab = (Tab)i;
                m_TabButtons[i].onClick.AddListener(() => SelectTab(tab));
            }

            if (m_OpenSelectorButton != null)
                m_OpenSelectorButton.onClick.AddListener(() => SetSelectorOpen(true));

            if (m_CloseSelectorButton != null)
                m_CloseSelectorButton.onClick.AddListener(() => SetSelectorOpen(false));

            SetSelectorOpen(false);
        }

        /// <summary>
        /// 選択パネルの開閉。
        /// ふだんは閉じておき、AR の映像をなるべく覆わないようにする。
        /// </summary>
        /// <summary>
        /// 首から上でカメラを追う動きの入り切り。
        /// 入っているあいだが分かるよう、ボタンの色を変える。
        /// </summary>
        void ToggleHeadLook()
        {
            var on = m_Placement.ToggleHeadLook();

            if (m_FaceCameraButton != null && m_FaceCameraButton.image != null)
                m_FaceCameraButton.image.color = on ? m_SelectedColor : m_NormalColor;
        }

        void SetSelectorOpen(bool open)
        {
            if (m_SelectorPanel != null)
                m_SelectorPanel.SetActive(open);

            // 開いている間は「きせかえ」ボタンを引っ込める(重なって押しにくいため)
            if (m_OpenSelectorButton != null)
                m_OpenSelectorButton.gameObject.SetActive(!open);

            if (open)
                RebuildItems();
        }

        void OnEnable()
        {
            if (m_Flow != null)
            {
                m_Flow.PhaseChanged += OnPhaseChanged;
                OnPhaseChanged(m_Flow.Phase);
            }

            if (m_Placement != null)
                m_Placement.CharacterPlaced += OnCharacterPlaced;
        }

        void OnDisable()
        {
            if (m_Flow != null)
                m_Flow.PhaseChanged -= OnPhaseChanged;

            if (m_Placement != null)
                m_Placement.CharacterPlaced -= OnCharacterPlaced;
        }

        void Update()
        {
            // スキャンが長引いたら文言を差し替える。
            // 「反応してない?」と思わせない ための保険。
            if (m_Flow != null && m_Flow.Phase == ARAppPhase.Scanning && m_ScanLabel != null)
                m_ScanLabel.text = m_Flow.ScanIsTakingLong ? m_ScanHintText : m_ScanText;

            // キャラを直接タップしてもポーズが進むので、UI 以外の経路にも表示を追従させる
            if (m_Poser == null)
                return;

            if (m_Tab == Tab.Pose && m_Poser.CurrentIndex != m_LastPoseIndex)
            {
                m_LastPoseIndex = m_Poser.CurrentIndex;
                RefreshItemSelection();
            }
            else if (m_Tab == Tab.Expression && m_Poser.CurrentExpressionIndex != m_LastExpressionIndex)
            {
                m_LastExpressionIndex = m_Poser.CurrentExpressionIndex;
                RefreshItemSelection();
            }
        }

        void OnPhaseChanged(ARAppPhase phase)
        {
            foreach (var panel in m_Panels)
            {
                if (panel.root != null)
                    panel.root.SetActive(panel.phase == phase);
            }
        }

        /// <summary>キャラクターは設置されるまで存在しないので、中身は設置のタイミングで組み立てる。</summary>
        void OnCharacterPlaced()
        {
            var character = m_Placement != null ? m_Placement.Character : null;

            m_Poser = character != null ? character.GetComponent<ARCharacterPoser>() : null;
            m_Wardrobe = character != null ? character.GetComponent<ARCharacterWardrobe>() : null;

            m_LastPoseIndex = m_Poser != null ? m_Poser.CurrentIndex : -1;
            m_LastExpressionIndex = m_Poser != null ? m_Poser.CurrentExpressionIndex : -1;

            // パネルが開いているときだけ中身を作り直す。閉じていれば開いた時に作られる。
            if (m_SelectorPanel != null && m_SelectorPanel.activeSelf)
                SelectTab(m_Tab);
        }

        void SelectTab(Tab tab)
        {
            m_Tab = tab;

            for (var i = 0; i < m_TabButtons.Length; i++)
            {
                if (m_TabButtons[i] != null && m_TabButtons[i].targetGraphic is Image image)
                    image.color = (Tab)i == tab ? m_TabSelectedColor : m_TabNormalColor;
            }

            RebuildItems();
        }

        void RebuildItems()
        {
            if (m_SelectorContent == null)
                return;

            foreach (var button in m_ItemButtons)
            {
                if (button != null)
                    Destroy(button.gameObject);
            }
            m_ItemButtons.Clear();

            switch (m_Tab)
            {
                case Tab.Pose:
                    BuildPoseItems();
                    break;
                case Tab.Expression:
                    BuildExpressionItems();
                    break;
                case Tab.Wardrobe:
                    BuildWardrobeItems();
                    break;
                case Tab.Character:
                    BuildCharacterItems();
                    break;
            }

            RefreshItemSelection();
        }

        void BuildPoseItems()
        {
            if (m_Poser == null)
                return;

            for (var i = 0; i < m_Poser.Poses.Count; i++)
            {
                var index = i;
                var entry = m_Poser.Poses[i];

                AddItem(Label(entry.displayName, $"ポーズ{i + 1}"), () =>
                {
                    m_Poser.SetPose(index);
                    m_LastPoseIndex = index;
                    RefreshItemSelection();
                });
            }
        }

        void BuildExpressionItems()
        {
            if (m_Poser == null)
                return;

            for (var i = 0; i < m_Poser.Expressions.Count; i++)
            {
                var index = i;
                var entry = m_Poser.Expressions[i];

                AddItem(Label(entry.displayName, $"表情{i + 1}"), () =>
                {
                    m_Poser.SetExpression(index);
                    m_LastExpressionIndex = index;
                    RefreshItemSelection();
                });
            }
        }

        /// <summary>
        /// 着せ替えは「選択」ではなく「オン/オフ」なので、
        /// いま着ているものすべてに色が付く。
        /// </summary>
        void BuildWardrobeItems()
        {
            if (m_Wardrobe == null)
                return;

            for (var i = 0; i < m_Wardrobe.Parts.Count; i++)
            {
                var index = i;
                var part = m_Wardrobe.Parts[i];

                AddItem(Label(part.displayName, $"パーツ{i + 1}"), () =>
                {
                    m_Wardrobe.Toggle(index);
                    RefreshItemSelection();
                });
            }
        }

        void BuildCharacterItems()
        {
            if (m_Placement == null)
                return;

            for (var i = 0; i < m_Placement.Characters.Count; i++)
            {
                var index = i;
                var entry = m_Placement.Characters[i];

                AddItem(Label(entry.displayName, $"キャラ{i + 1}"), () =>
                {
                    m_Placement.SelectCharacter(index);
                    RefreshItemSelection();
                });
            }
        }

        static string Label(string value, string fallback)
            => string.IsNullOrEmpty(value) ? fallback : value;

        void AddItem(string label, UnityEngine.Events.UnityAction onClick)
        {
            var go = new GameObject("Item", typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            go.transform.SetParent(m_SelectorContent, false);

            var image = go.GetComponent<Image>();
            image.color = m_NormalColor;

            if (m_ItemSprite != null)
            {
                image.sprite = m_ItemSprite;
                image.type = Image.Type.Sliced;
            }

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;
            button.onClick.AddListener(onClick);

            var textGO = new GameObject("Label", typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            textGO.transform.SetParent(go.transform, false);

            var rect = textGO.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = new Vector2(6f, 0f);
            rect.offsetMax = new Vector2(-6f, 0f);

            var text = textGO.GetComponent<Text>();
            text.text = label;
            text.font = GetDefaultFont();
            text.fontSize = 26;
            text.fontStyle = FontStyle.Bold;
            text.alignment = TextAnchor.MiddleCenter;
            text.color = m_ItemTextColor;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Truncate;
            text.raycastTarget = false;
            text.resizeTextForBestFit = true;
            text.resizeTextMinSize = 16;
            text.resizeTextMaxSize = 26;

            m_ItemButtons.Add(button);
        }

        /// <summary>いま選ばれている項目だけ色を変える。</summary>
        void RefreshItemSelection()
        {
            // 着せ替えだけは複数同時に「オン」になりうる
            if (m_Tab == Tab.Wardrobe)
            {
                for (var i = 0; i < m_ItemButtons.Count; i++)
                {
                    if (m_ItemButtons[i] != null && m_ItemButtons[i].targetGraphic is Image wardrobeImage)
                        wardrobeImage.color = m_Wardrobe != null && m_Wardrobe.IsOn(i)
                            ? m_SelectedColor
                            : m_NormalColor;
                }

                return;
            }

            var selected = m_Tab switch
            {
                Tab.Pose => m_Poser != null ? m_Poser.CurrentIndex : -1,
                Tab.Expression => m_Poser != null ? m_Poser.CurrentExpressionIndex : -1,
                Tab.Character => m_Placement != null ? m_Placement.SelectedCharacterIndex : -1,
                _ => -1,
            };

            for (var i = 0; i < m_ItemButtons.Count; i++)
            {
                if (m_ItemButtons[i] != null && m_ItemButtons[i].targetGraphic is Image image)
                    image.color = i == selected ? m_SelectedColor : m_NormalColor;
            }
        }

        /// <summary>
        /// 旧 UI の既定フォント。実機ではシステムフォントが日本語を補完してくれる。
        /// </summary>
        static Font GetDefaultFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }

        static void OpenAppSettings()
        {
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var uriClass = new AndroidJavaClass("android.net.Uri");
                using var intent = new AndroidJavaObject(
                    "android.content.Intent",
                    "android.settings.APPLICATION_DETAILS_SETTINGS");

                var packageName = activity.Call<string>("getPackageName");
                using var uri = uriClass.CallStatic<AndroidJavaObject>("fromParts", "package", packageName, null);

                intent.Call<AndroidJavaObject>("setData", uri);
                activity.Call("startActivity", intent);
            }
            catch (System.Exception e)
            {
                Debug.LogWarning($"[ARUIController] 設定画面を開けませんでした: {e.Message}");
            }
#elif UNITY_IOS && !UNITY_EDITOR
            Application.OpenURL("app-settings:");
#else
            Debug.Log("[ARUIController] OpenAppSettings は実機でのみ動作します。");
#endif
        }
    }
}
