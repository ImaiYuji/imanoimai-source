# いまのいまい — ソースコード

床に向けるとキャラクターが現れて、いっしょに写真が撮れる AR アプリのソースコードです。
Android と iOS で動きます。

アプリの入れかたと遊びかた: https://imaiyuji.github.io/imanoimai-guide/

---

## このリポジトリに入っていないもの

**キャラクターのモデル・モーション・テクスチャは含まれていません。**
第三者が制作した素材で、再配布の可否を確認できていないためです。

そのため **このリポジトリだけではビルドできません。** アプリが何をしているかを
読んで確かめるためのものです。

入っていないもの:

- `Assets/Anomea/` — アバター（モデル・マテリアル・アニメーション）
- `Assets/KawaiiPosing/` — ポーズ集
- `Assets/Art/` — アプリアイコン
- `Assets/Prefabs/` `Assets/Scenes/` `Assets/XR/` — すべてコードから生成されるもの

## 権限と通信

- 使う権限は **カメラ** だけです（Android では ARCore が `INTERNET` も要求します）
- アプリ自身に通信するコードはありません。`UnityWebRequest` 等を一切使っていません
- アクセス解析・広告・クラッシュレポートの類は入れていません
  （`ProjectSettings/UnityConnectSettings.asset` で無効）
- 撮った写真は端末内にのみ保存します
  - Android: `Pictures/いまのいまい`（MediaStore 経由）
  - iOS: カメラロール（`Assets/Plugins/iOS/ARPhotoSave.mm`）

## 作りかた

**シーンもプレハブも Animator も、すべてエディタ拡張がコードから生成します。**
手で並べたものはありません。Unity のメニューから実行します。

| メニュー | 内容 |
|---|---|
| `AR Character App / 0. すべて再構築` | 下記を正しい順序で一括実行 |
| `1. プロジェクト設定を適用` | Player Settings・XR・アイコン |
| `2. AR シーンを生成` | シーンを一から組み立て |
| `3. Android APK をビルド` | 実機確認用 |
| `4. Android AAB をビルド` | ストア提出用 |
| `5. iOS プロジェクトを書き出し` | Xcode プロジェクトを生成 |

## 構成

### 実行時 (`Assets/Scripts/Runtime/`)

| ファイル | 役割 |
|---|---|
| `ARAppFlow.cs` | 権限説明 → 平面スキャン → 設置 の流れ |
| `ARPlacementController.cs` | 平面検出・設置・回転/拡大・キャラ切り替え |
| `ARCharacterPoser.cs` | ポーズと表情の切り替え |
| `ARCharacterWardrobe.cs` | 着せ替え（メッシュ表示と BlendShape の連動） |
| `ARCharacterHeadLook.cs` | 首から上だけカメラに向ける（Animator IK） |
| `ARCharacterBreathing.cs` | 静止ポーズに呼吸の揺らぎを足す |
| `ARPhotoCapture.cs` | 撮影と保存（Android/iOS で実装が分かれる） |
| `ARUIController.cs` | 画面の出し分けとタブ |
| `NonARFallbackViewer.cs` | AR 非対応端末向けの 3D ビューア |

### エディタ拡張 (`Assets/Scripts/Editor/`)

| ファイル | 役割 |
|---|---|
| `ARProjectSetup.cs` | Player Settings・XR ローダー・アイコン |
| `ARSceneBuilder.cs` | シーンの組み立てと配線 |
| `ARUIBuilder.cs` | UI をコードから構築 |
| `AnomeaCharacterSetup.cs` | FBX からプレハブと Animator を生成 |
| `PoseClipSanitizer.cs` | ポーズクリップから表情・表示切替を除く |
| `MaterialTextureRepair.cs` | 参照切れテクスチャの修復 |
| `OverlayTransparencyFix.cs` | Overlay の透過修復 |
| `ARSceneValidator.cs` | シーンが AR として成立しているかの検証 |
| `ARIosPostProcess.cs` | Xcode プロジェクトへの追記 |

### ネイティブ (`Assets/Plugins/iOS/`)

| ファイル | 役割 |
|---|---|
| `ARPhotoSave.mm` | カメラロールへの保存（Unity に API が無いため） |
| `ARCameraPermission.mm` | カメラ許可の状態取得（後述） |

## コマンドラインからビルドするときの注意

このプロジェクトは Unity をバッチモードで動かす前提で作っています。
その過程で、GUI では起きない問題にいくつか当たりました。同じ罠にはまらないための記録です。

**lilToon のシェーダーが全滅する**
lilToon はビルド前処理で「全機能をオフにしてから、使う機能だけ戻す」最適化をします。
その処理はシーンを開き直す前提になっていて、バッチモードでは
`ArgumentException: Scene file not found: ''` で中断します。
全機能オフの直後に落ちるため、サブシェーダーが全滅した状態で焼かれ、
マテリアルは `Fallback "Unlit/Texture"` で描かれます。見た目は大きく崩れず、
**アルファだけが失われる**ので気づきにくいです。
`LILTOON_DISABLE_ASSET_MODIFICATION` を定義して回避しています。

**ARKit のネイティブライブラリが除外される**
`ARKitBuildProcessor.OnPreprocessBuild` は `#if UNITY_XR_ARKIT_LOADER_ENABLED` の中でしか
本処理を呼びません。そしてバッチモードでその定義を付けるのは、その本処理の中だけです。
定義が無いから処理が走らず、処理が走らないから定義が付かない、という循環になり、
`libUnityARKit.a` が丸ごと除外されて `Undefined symbols: _UnityARKit_*` でリンクに失敗します。
定義を先に入れて回避しています。

**iOS のカメラ許可が判定できない**
`Application.HasUserAuthorization(UserAuthorization.WebCam)` は
「Unity 自身が要求したか」しか見ません。カメラを開くのが ARKit のこのアプリでは
許可済みでも false のままで、許可画面から先に進めなくなります。
`AVCaptureDevice` に直接聞いて回避しています。

## 署名

配布用の署名は環境変数から読みます。パスワードはリポジトリに置きません。

```
IMANOIMAI_KEYSTORE       .keystore のパス
IMANOIMAI_KEYSTORE_PASS  ストアのパスワード
IMANOIMAI_KEY_ALIAS      鍵の別名
IMANOIMAI_KEY_PASS       鍵のパスワード
```

## 環境

- Unity 2022.3.22f1 / ビルトインレンダーパイプライン
- AR Foundation 5.1.5 + ARCore / ARKit
- iOS のビルドには Xcode 16 系が必要です
