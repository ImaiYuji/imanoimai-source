using System.Collections.Generic;
using UnityEditor;
using UnityEngine;
using UnityEngine.UI;

namespace ARCharacterApp.EditorTools
{
    /// <summary>ARSceneBuilder が組み上げた UI の参照をまとめて返すための入れ物。</summary>
    internal sealed class BuiltUI
    {
        public GameObject CanvasRoot;
        public readonly List<KeyValuePair<ARAppPhase, GameObject>> Panels =
            new List<KeyValuePair<ARAppPhase, GameObject>>();

        public Text ScanLabel;
        public Button ContinueButton;
        public Button OpenSettingsButton;
        public Button RetryButton;
        public Button ResetButton;

        // ---- 撮影 ----
        public Button ShutterButton;
        public CanvasGroup FlashOverlay;
        public GameObject SavedToast;

        // ---- 選択パネル ----
        /// <summary>「ポーズ / 表情 / キャラ」のタブ。</summary>
        public Button[] TabButtons;

        /// <summary>ボタンを並べる先。中身は実行時に作る(項目数が可変のため)。</summary>
        public RectTransform SelectorContent;

        /// <summary>選択パネル全体。ふだんは閉じていて、下のボタンで開く。</summary>
        public GameObject SelectorPanel;

        /// <summary>選択パネルを開くボタン(閉じているときだけ出る)。</summary>
        public Button OpenSelectorButton;

        /// <summary>キャラをカメラのほうに向け直すボタン。</summary>
        public Button FaceCameraButton;

        /// <summary>選択パネルを閉じるボタン。</summary>
        public Button CloseSelectorButton;
    }

    /// <summary>
    /// フェーズごとの UI をコードから組み立てる。
    ///
    /// レイアウト方針:
    ///  - 判断を求める画面(権限・非対応)は全画面モーダル
    ///  - 進行中のヒント(スキャン中など)は下部の細い帯だけ。AR の映像を隠さない。
    /// </summary>
    internal static class ARUIBuilder
    {
        // ---- 配色 -------------------------------------------------------------
        // 桃色を基調にした、やわらかい配色。
        // AR の映像の上に重なるので、不透明にしすぎず「白っぽい桃」を薄く敷く。

        /// <summary>判断を求める全画面。桃を含んだ暗さにして冷たくしない。</summary>
        static readonly Color k_Dim = new Color(0.16f, 0.08f, 0.12f, 0.84f);

        /// <summary>進行中のヒント帯。</summary>
        static readonly Color k_Hint = new Color(0.30f, 0.12f, 0.20f, 0.55f);

        /// <summary>主役のボタン。濃いめの桃。</summary>
        static readonly Color k_Accent = new Color(0.97f, 0.44f, 0.66f, 1f);

        /// <summary>パネルの下地。白に近い桃。</summary>
        static readonly Color k_Panel = new Color(1f, 0.90f, 0.94f, 0.94f);

        /// <summary>選択肢の通常時。</summary>
        static readonly Color k_Item = new Color(1f, 1f, 1f, 0.78f);

        /// <summary>選択中の項目。</summary>
        static readonly Color k_ItemSelected = new Color(0.98f, 0.55f, 0.73f, 1f);

        /// <summary>タブの通常時。</summary>
        static readonly Color k_Tab = new Color(1f, 1f, 1f, 0.55f);

        /// <summary>タブの選択時。</summary>
        static readonly Color k_TabSelected = new Color(0.99f, 0.68f, 0.81f, 1f);

        /// <summary>パネル上の文字。黒よりも赤みのある濃茶のほうが桃に馴染む。</summary>
        static readonly Color k_TextOnPanel = new Color(0.36f, 0.16f, 0.24f, 1f);

        public static BuiltUI Build(Transform parent)
        {
            var ui = new BuiltUI();

            // ---- Canvas -------------------------------------------------
            var canvasGO = new GameObject("UI Canvas",
                typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGO.transform.SetParent(parent, false);

            var canvas = canvasGO.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;

            var scaler = canvasGO.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1080f, 1920f);
            scaler.screenMatchMode = CanvasScaler.ScreenMatchMode.MatchWidthOrHeight;
            scaler.matchWidthOrHeight = 0.5f;

            ui.CanvasRoot = canvasGO;
            var root = canvasGO.transform;

            // ---- 権限の説明(モーダル) --------------------------------------
            {
                var panel = CreateFullScreenPanel(root, "Panel_ExplainPermission", k_Dim);
                CreateText(panel.transform, "Title", "カメラを使います",
                    56, TextAnchor.MiddleCenter, new Vector2(0f, 220f), new Vector2(900f, 90f), FontStyle.Bold);
                CreateText(panel.transform, "Body",
                    "目の前の空間にキャラクターを表示するため、\nカメラの映像を使います。\n撮影した映像が保存・送信されることはありません。",
                    36, TextAnchor.MiddleCenter, new Vector2(0f, 40f), new Vector2(900f, 260f));
                ui.ContinueButton = CreatePillButton(panel.transform, "Button_Continue", "つづける",
                    new Vector2(520f, 120f), k_Accent, Color.white, 40);
                AnchorTo(ui.ContinueButton.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -220f));

                ui.Panels.Add(Pair(ARAppPhase.ExplainingPermission, panel));
            }

            // ---- 権限を拒否された(モーダル) ---------------------------------
            {
                var panel = CreateFullScreenPanel(root, "Panel_PermissionDenied", k_Dim);
                CreateText(panel.transform, "Title", "カメラを許可してください",
                    52, TextAnchor.MiddleCenter, new Vector2(0f, 240f), new Vector2(900f, 90f), FontStyle.Bold);
                CreateText(panel.transform, "Body",
                    "カメラが使えないと、AR でキャラクターを\n表示できません。\n端末の設定から許可をお願いします。",
                    36, TextAnchor.MiddleCenter, new Vector2(0f, 60f), new Vector2(900f, 240f));
                ui.OpenSettingsButton = CreatePillButton(panel.transform, "Button_OpenSettings", "設定を開く",
                    new Vector2(520f, 120f), k_Accent, Color.white, 40);
                AnchorTo(ui.OpenSettingsButton.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -180f));

                ui.RetryButton = CreatePillButton(panel.transform, "Button_Retry", "もう一度試す",
                    new Vector2(520f, 110f), new Color(1f, 1f, 1f, 0.24f), Color.white, 38);
                AnchorTo(ui.RetryButton.GetComponent<RectTransform>(),
                    new Vector2(0.5f, 0.5f), new Vector2(0f, -330f));

                ui.Panels.Add(Pair(ARAppPhase.PermissionDenied, panel));
            }

            // ---- 対応状況の確認中 --------------------------------------------
            {
                var panel = CreateFullScreenPanel(root, "Panel_CheckingSupport", k_Dim);
                CreateText(panel.transform, "Body", "準備しています…",
                    42, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(900f, 120f));

                ui.Panels.Add(Pair(ARAppPhase.CheckingSupport, panel));
            }

            // ---- AR 非対応(フォールバック告知) --------------------------------
            {
                var panel = CreateFullScreenPanel(root, "Panel_Unsupported", new Color(0f, 0f, 0f, 0f));
                var band = CreateBottomBand(panel.transform, "Band", 300f, k_Hint);
                CreateText(band.transform, "Title", "この端末は AR に対応していません",
                    38, TextAnchor.MiddleCenter, new Vector2(0f, 60f), new Vector2(900f, 60f), FontStyle.Bold);
                CreateText(band.transform, "Body",
                    "かわりに 3D ビューアで表示します。\nドラッグで回転、タップでリアクション。",
                    32, TextAnchor.MiddleCenter, new Vector2(0f, -40f), new Vector2(900f, 120f));

                ui.Panels.Add(Pair(ARAppPhase.Unsupported, panel));
            }

            // ---- スキャン中(非モーダル) --------------------------------------
            {
                var panel = CreateFullScreenPanel(root, "Panel_Scanning", new Color(0f, 0f, 0f, 0f));
                var band = CreateBottomBand(panel.transform, "Band", 190f, k_Hint);
                ui.ScanLabel = CreateText(band.transform, "ScanLabel", "周りをゆっくり映してください",
                    38, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(900f, 140f));

                ui.Panels.Add(Pair(ARAppPhase.Scanning, panel));
            }

            // ---- 設置できる(非モーダル) --------------------------------------
            {
                var panel = CreateFullScreenPanel(root, "Panel_ReadyToPlace", new Color(0f, 0f, 0f, 0f));
                var band = CreateBottomBand(panel.transform, "Band", 190f, k_Hint);
                CreateText(band.transform, "Label", "画面をタップして置く",
                    42, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(900f, 140f), FontStyle.Bold);

                ui.Panels.Add(Pair(ARAppPhase.ReadyToPlace, panel));
            }

            // ---- 設置後(撮影 + ポーズ/表情/キャラ選択) --------------------------
            {
                var panel = CreateFullScreenPanel(root, "Panel_Placed", new Color(0f, 0f, 0f, 0f));
                BuildPlacedPanel(panel.transform, ui);

                ui.Panels.Add(Pair(ARAppPhase.Placed, panel));
            }

            // 起動直後と OS ダイアログ表示中は何も出さない(素通し)
            return ui;
        }

        /// <summary>
        /// 設置後の画面。写真アプリの構えに寄せている。
        ///
        ///   上:  置きなおす
        ///   中:  AR の映像(何も置かない)
        ///   下:  シャッター / タブ(ポーズ・表情・キャラ)/ スクロールする選択肢
        ///
        /// 選択肢はポーズ 21・表情 39 と数が多いので、固定グリッドではなく
        /// スクロールできる領域に入れている。
        /// </summary>
        static void BuildPlacedPanel(Transform panel, BuiltUI ui)
        {
            const float selectorHeight = 560f;

            // ---- 置きなおす(右上) --------------------------------------------
            ui.ResetButton = CreatePillButton(panel, "Button_Reset", "おきなおす",
                new Vector2(220f, 74f), new Color(1f, 1f, 1f, 0.86f), k_TextOnPanel, 26);
            AnchorTo(ui.ResetButton.GetComponent<RectTransform>(),
                new Vector2(1f, 1f), new Vector2(-134f, -80f));

            // ---- シャッター(下部中央) ------------------------------------------
            // 写真アプリと同じ位置にあると、説明しなくても押せる。
            ui.ShutterButton = CreateCircleButton(panel, "Button_Shutter", 176f);
            AnchorTo(ui.ShutterButton.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f), new Vector2(0f, 150f));

            // ---- 選択パネルを開くボタン(シャッターの右) --------------------------
            ui.OpenSelectorButton = CreatePillButton(panel, "Button_OpenSelector", "きせかえ",
                new Vector2(220f, 84f), k_Accent, Color.white, 28);
            AnchorTo(ui.OpenSelectorButton.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f), new Vector2(310f, 150f));

            // ---- こっちを見させるボタン(シャッターの左) --------------------------
            // 体は動かさず、首から上だけカメラを追う。押すたびに入り切りする。
            // 「きせかえ」と左右対称に置く。
            ui.FaceCameraButton = CreatePillButton(panel, "Button_FaceCamera", "こっちむいて",
                new Vector2(220f, 84f), new Color(1f, 1f, 1f, 0.86f), k_TextOnPanel, 26);
            AnchorTo(ui.FaceCameraButton.GetComponent<RectTransform>(),
                new Vector2(0.5f, 0f), new Vector2(-310f, 150f));

            // ---- 選択パネル(ふだんは閉じている) ---------------------------------
            var selector = new GameObject("Selector", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            selector.transform.SetParent(panel, false);
            ui.SelectorPanel = selector;

            var selectorRect = selector.GetComponent<RectTransform>();
            selectorRect.anchorMin = new Vector2(0f, 0f);
            selectorRect.anchorMax = new Vector2(1f, 0f);
            selectorRect.pivot = new Vector2(0.5f, 0f);
            selectorRect.offsetMin = Vector2.zero;
            selectorRect.offsetMax = new Vector2(0f, selectorHeight);
            selectorRect.anchoredPosition = Vector2.zero;

            var selectorImage = selector.GetComponent<Image>();
            selectorImage.sprite = GetRoundedSprite();
            selectorImage.type = Image.Type.Sliced;
            selectorImage.color = k_Panel;

            // ---- 閉じるボタン(パネル右上) ---------------------------------------
            ui.CloseSelectorButton = CreateCircleButton(selector.transform, "Button_Close", 76f,
                fill: new Color(1f, 1f, 1f, 0.9f), showInner: false);
            AnchorTo(ui.CloseSelectorButton.GetComponent<RectTransform>(),
                new Vector2(1f, 1f), new Vector2(-64f, -56f));
            CreateText(ui.CloseSelectorButton.transform, "Label", "×",
                44, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(76f, 76f), FontStyle.Bold, k_TextOnPanel);

            // ---- タブ ----------------------------------------------------------
            // ARCharacterApp.ARUIController.Tab の並びと一致させること
            var tabLabels = new[] { "ポーズ", "ひょうじょう", "きもの", "キャラ" };
            ui.TabButtons = new Button[tabLabels.Length];

            const float tabWidth = 214f;
            for (var i = 0; i < tabLabels.Length; i++)
            {
                var x = (i - (tabLabels.Length - 1) * 0.5f) * (tabWidth + 10f) - 34f;

                ui.TabButtons[i] = CreatePillButton(selector.transform, $"Tab_{i}", tabLabels[i],
                    new Vector2(tabWidth, 76f), k_Tab, k_TextOnPanel, 25);

                AnchorTo(ui.TabButtons[i].GetComponent<RectTransform>(),
                    new Vector2(0.5f, 1f), new Vector2(x, -56f));
            }

            // ---- スクロール領域 --------------------------------------------------
            var scrollGO = new GameObject("ScrollView",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image),
                typeof(Mask), typeof(ScrollRect));
            scrollGO.transform.SetParent(selector.transform, false);

            var scrollRect = scrollGO.GetComponent<RectTransform>();
            scrollRect.anchorMin = new Vector2(0f, 0f);
            scrollRect.anchorMax = new Vector2(1f, 1f);
            scrollRect.offsetMin = new Vector2(26f, 22f);
            scrollRect.offsetMax = new Vector2(-26f, -108f);

            // Mask を効かせるために Image が要る。見た目は透明でよい。
            var scrollImage = scrollGO.GetComponent<Image>();
            scrollImage.color = new Color(1f, 1f, 1f, 0.01f);
            scrollGO.GetComponent<Mask>().showMaskGraphic = false;

            var content = new GameObject("Content",
                typeof(RectTransform), typeof(GridLayoutGroup), typeof(ContentSizeFitter));
            content.transform.SetParent(scrollGO.transform, false);

            var contentRect = content.GetComponent<RectTransform>();
            contentRect.anchorMin = new Vector2(0f, 1f);
            contentRect.anchorMax = new Vector2(1f, 1f);
            contentRect.pivot = new Vector2(0.5f, 1f);
            contentRect.offsetMin = Vector2.zero;
            contentRect.offsetMax = Vector2.zero;

            var grid = content.GetComponent<GridLayoutGroup>();
            grid.cellSize = new Vector2(232f, 80f);
            grid.spacing = new Vector2(12f, 12f);
            grid.constraint = GridLayoutGroup.Constraint.FixedColumnCount;
            grid.constraintCount = 4;
            grid.childAlignment = TextAnchor.UpperCenter;

            var fitter = content.GetComponent<ContentSizeFitter>();
            fitter.verticalFit = ContentSizeFitter.FitMode.PreferredSize;

            var scroll = scrollGO.GetComponent<ScrollRect>();
            scroll.horizontal = false;
            scroll.vertical = true;
            scroll.movementType = ScrollRect.MovementType.Elastic;
            scroll.elasticity = 0.1f;
            scroll.inertia = true;
            scroll.decelerationRate = 0.135f;
            scroll.scrollSensitivity = 30f;
            scroll.viewport = scrollRect;
            scroll.content = contentRect;

            ui.SelectorContent = contentRect;

            selector.SetActive(false);

            // ---- 撮影時のフラッシュ(全画面) ------------------------------------
            var flash = new GameObject("Overlay_Flash",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image), typeof(CanvasGroup));
            flash.transform.SetParent(panel, false);
            Stretch(flash.GetComponent<RectTransform>());

            var flashImage = flash.GetComponent<Image>();
            flashImage.color = Color.white;
            flashImage.raycastTarget = false;

            ui.FlashOverlay = flash.GetComponent<CanvasGroup>();
            ui.FlashOverlay.alpha = 0f;
            ui.FlashOverlay.blocksRaycasts = false;
            ui.FlashOverlay.interactable = false;

            // ---- 「保存しました」 -------------------------------------------------
            var toast = new GameObject("Toast_Saved",
                typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            toast.transform.SetParent(panel, false);

            var toastRect = toast.GetComponent<RectTransform>();
            toastRect.anchorMin = toastRect.anchorMax = new Vector2(0.5f, 0.5f);
            toastRect.pivot = new Vector2(0.5f, 0.5f);
            toastRect.sizeDelta = new Vector2(560f, 108f);
            toastRect.anchoredPosition = new Vector2(0f, 0f);

            var toastImage = toast.GetComponent<Image>();
            toastImage.color = new Color(0f, 0f, 0f, 0.78f);
            toastImage.raycastTarget = false;

            CreateText(toast.transform, "Label", "写真を保存しました",
                34, TextAnchor.MiddleCenter, Vector2.zero, new Vector2(560f, 108f), FontStyle.Bold);

            toast.SetActive(false);
            ui.SavedToast = toast;
        }

        /// <summary>シャッター用の丸ボタン。写真アプリと同じく二重丸にする。</summary>
        static Button CreateCircleButton(Transform parent, string name, float diameter,
            Color? fill = null, bool showInner = true)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = new Vector2(diameter, diameter);

            var outer = go.GetComponent<Image>();
            outer.sprite = GetKnobSprite();
            outer.type = Image.Type.Simple;
            outer.color = fill ?? new Color(1f, 0.78f, 0.87f, 0.85f);

            if (showInner)
            {
                var inner = new GameObject("Inner", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
                inner.transform.SetParent(go.transform, false);

                var innerRect = inner.GetComponent<RectTransform>();
                innerRect.anchorMin = innerRect.anchorMax = new Vector2(0.5f, 0.5f);
                innerRect.sizeDelta = new Vector2(diameter - 28f, diameter - 28f);

                var innerImage = inner.GetComponent<Image>();
                innerImage.sprite = GetKnobSprite();
                innerImage.color = Color.white;
                innerImage.raycastTarget = false;
            }

            var button = go.GetComponent<Button>();
            button.targetGraphic = outer;

            return button;
        }

        /// <summary>角丸のボタン。四角い箱より柔らかく見える。</summary>
        static Button CreatePillButton(Transform parent, string name, string label,
            Vector2 size, Color fill, Color textColor, int fontSize)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rect = go.GetComponent<RectTransform>();
            rect.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.sprite = GetRoundedSprite();
            image.type = Image.Type.Sliced;
            image.color = fill;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            if (!string.IsNullOrEmpty(label))
            {
                var text = CreateText(go.transform, "Label", label, fontSize, TextAnchor.MiddleCenter,
                    Vector2.zero, size, FontStyle.Bold, textColor);
                Stretch(text.rectTransform);
            }

            return button;
        }

        /// <summary>Unity 組み込みの丸スプライト。外部アセットに頼らずに丸を出すため。</summary>
        static Sprite GetKnobSprite()
            => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd");

        /// <summary>Unity 組み込みの角丸スプライト(9 スライス)。</summary>
        static Sprite GetRoundedSprite()
            => AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd");

        static void AnchorTo(RectTransform rect, Vector2 anchor, Vector2 position)
        {
            rect.anchorMin = rect.anchorMax = anchor;
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.anchoredPosition = position;
        }

        // ---- 部品づくり -----------------------------------------------------

        static KeyValuePair<ARAppPhase, GameObject> Pair(ARAppPhase phase, GameObject go)
            => new KeyValuePair<ARAppPhase, GameObject>(phase, go);

        static GameObject CreateFullScreenPanel(Transform parent, string name, Color background)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            Stretch(go.GetComponent<RectTransform>());

            var image = go.GetComponent<Image>();
            image.color = background;

            // 透明なパネルは AR のタップを遮らないようにする。
            // これを忘れると「タップしても置けない」不具合になる。
            image.raycastTarget = background.a > 0.01f;

            go.SetActive(false);
            return go;
        }

        static GameObject CreateBottomBand(Transform parent, string name, float height, Color color)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 0f);
            rt.anchorMax = new Vector2(1f, 0f);
            rt.pivot = new Vector2(0.5f, 0f);
            rt.offsetMin = new Vector2(0f, 0f);
            rt.offsetMax = new Vector2(0f, height);
            rt.anchoredPosition = new Vector2(0f, 90f);

            var image = go.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = false;

            return go;
        }

        static Text CreateText(Transform parent, string name, string content, int fontSize,
            TextAnchor anchor, Vector2 position, Vector2 size, FontStyle style = FontStyle.Normal,
            Color? color = null)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Text));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            var text = go.GetComponent<Text>();
            text.text = content;
            text.font = GetDefaultFont();
            text.fontSize = fontSize;
            text.fontStyle = style;
            text.alignment = anchor;
            text.color = color ?? Color.white;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.raycastTarget = false;

            return text;
        }

        static Button CreateButton(Transform parent, string name, string label,
            Vector2 position, Vector2 size, Color color, int labelFontSize = 40)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer),
                typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 0.5f);
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = position;
            rt.sizeDelta = size;

            var image = go.GetComponent<Image>();
            image.color = color;

            var button = go.GetComponent<Button>();
            button.targetGraphic = image;

            var text = CreateText(go.transform, "Label", label, labelFontSize, TextAnchor.MiddleCenter,
                Vector2.zero, size, FontStyle.Bold);
            Stretch(text.rectTransform);

            return button;
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        /// <summary>
        /// 旧 UI の既定フォント。Unity のバージョンで組み込み名が変わっているため両方試す。
        /// このフォントは実機(Android/iOS)では日本語をシステムフォントで補完してくれる。
        /// </summary>
        static Font GetDefaultFont()
        {
            var font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf");
            if (font == null)
                font = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return font;
        }
    }
}
