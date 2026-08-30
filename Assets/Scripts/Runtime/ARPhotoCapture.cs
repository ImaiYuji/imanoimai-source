using System;
using System.Collections;
using System.IO;
using UnityEngine;

namespace ARCharacterApp
{
    /// <summary>
    /// いま画面に映っているものをそのまま写真として保存する。
    ///
    /// 写真アプリと同じ感覚で使えるように、次の点を守っている:
    ///  - 操作 UI は写り込まない(撮る直前に隠す)
    ///  - 撮れたことがその場で分かる(フラッシュ + 文言)
    ///  - 端末のギャラリーから見られる(アプリ内に隠さない)
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ARPhotoCapture : MonoBehaviour
    {
        [Tooltip("撮影時に一時的に隠す UI。ここに入れたものは写真に写らない。")]
        [SerializeField] GameObject[] m_HideWhileCapturing = Array.Empty<GameObject>();

        [Tooltip("シャッターの白フラッシュに使う全画面 Image")]
        [SerializeField] CanvasGroup m_FlashOverlay;

        [Tooltip("「保存しました」を出すためのオブジェクト")]
        [SerializeField] GameObject m_SavedToast;

        [SerializeField] float m_FlashDuration = 0.25f;
        [SerializeField] float m_ToastDuration = 1.6f;

        [Tooltip("ギャラリー内のフォルダ名")]
        [SerializeField] string m_AlbumName = "いまのいまい";

        [SerializeField] AudioSource m_ShutterSound;

        bool m_Capturing;

        /// <summary>撮影が終わったときに発火。引数は成功したかどうか。</summary>
        public event Action<bool> CaptureFinished;

        void Awake()
        {
            if (m_FlashOverlay != null)
                m_FlashOverlay.alpha = 0f;

            if (m_SavedToast != null)
                m_SavedToast.SetActive(false);
        }

        /// <summary>シャッターボタンから呼ぶ。</summary>
        public void Capture()
        {
            if (m_Capturing)
                return;

            StartCoroutine(CaptureRoutine());
        }

        IEnumerator CaptureRoutine()
        {
            m_Capturing = true;

            // UI を隠す。元の状態を覚えておいて、必ず戻す。
            var restore = new bool[m_HideWhileCapturing.Length];
            for (var i = 0; i < m_HideWhileCapturing.Length; i++)
            {
                var target = m_HideWhileCapturing[i];
                if (target == null)
                    continue;

                restore[i] = target.activeSelf;
                target.SetActive(false);
            }

            // 非表示を画面に反映させてから撮る
            yield return new WaitForEndOfFrame();

            Texture2D shot = null;
            byte[] png = null;

            try
            {
                shot = ScreenCapture.CaptureScreenshotAsTexture();
                png = shot.EncodeToPNG();
            }
            catch (Exception e)
            {
                Debug.LogError($"[ARPhotoCapture] 画面の取得に失敗しました: {e.Message}");
            }
            finally
            {
                if (shot != null)
                    Destroy(shot);

                for (var i = 0; i < m_HideWhileCapturing.Length; i++)
                {
                    if (m_HideWhileCapturing[i] != null)
                        m_HideWhileCapturing[i].SetActive(restore[i]);
                }
            }

            if (m_ShutterSound != null)
                m_ShutterSound.Play();

            StartCoroutine(FlashRoutine());

            var saved = false;
            if (png != null)
                saved = Save(png);

            if (saved && m_SavedToast != null)
                StartCoroutine(ToastRoutine());

            CaptureFinished?.Invoke(saved);
            m_Capturing = false;
        }

        IEnumerator FlashRoutine()
        {
            if (m_FlashOverlay == null)
                yield break;

            m_FlashOverlay.alpha = 1f;

            var elapsed = 0f;
            while (elapsed < m_FlashDuration)
            {
                elapsed += Time.deltaTime;
                m_FlashOverlay.alpha = 1f - Mathf.Clamp01(elapsed / m_FlashDuration);
                yield return null;
            }

            m_FlashOverlay.alpha = 0f;
        }

        IEnumerator ToastRoutine()
        {
            m_SavedToast.SetActive(true);
            yield return new WaitForSeconds(m_ToastDuration);

            if (m_SavedToast != null)
                m_SavedToast.SetActive(false);
        }

        bool Save(byte[] png)
        {
            var fileName = $"AR_{DateTime.Now:yyyyMMdd_HHmmss}.png";

#if UNITY_ANDROID && !UNITY_EDITOR
            if (SaveToAndroidGallery(png, fileName))
                return true;
#elif UNITY_IOS && !UNITY_EDITOR
            if (SaveToCameraRoll(png))
                return true;
#endif
            // ギャラリーに入れられなかった場合でも、写真そのものは失わないようにする
            try
            {
                var path = Path.Combine(Application.persistentDataPath, fileName);
                File.WriteAllBytes(path, png);
                Debug.Log($"[ARPhotoCapture] ギャラリーに入れられなかったため {path} に保存しました。");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogError($"[ARPhotoCapture] 保存に失敗しました: {e.Message}");
                return false;
            }
        }

#if UNITY_IOS && !UNITY_EDITOR
        [System.Runtime.InteropServices.DllImport("__Internal")]
        static extern void ARPhotoSave_SaveToCameraRoll(byte[] data, int length);

        /// <summary>
        /// カメラロールに保存する。
        ///
        /// iOS には Android の MediaStore にあたる C# API が無いので、
        /// Assets/Plugins/iOS/ARPhotoSave.mm に置いたネイティブ側へ渡す。
        /// 保存は非同期なので、ここでは「渡せたか」までしか分からない。
        /// 許可が下りなかった場合はネイティブ側がログを出す。
        ///
        /// Android と違ってアルバム名は指定していない。
        /// iOS でアルバムを作るには写真の読み取り許可まで必要になり、
        /// 写真を撮るだけのアプリが求める権限としては重すぎるため。
        /// </summary>
        bool SaveToCameraRoll(byte[] png)
        {
            try
            {
                ARPhotoSave_SaveToCameraRoll(png, png.Length);
                Debug.Log("[ARPhotoCapture] カメラロールへ保存を依頼しました。");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ARPhotoCapture] カメラロールへの保存に失敗しました: {e.Message}");
                return false;
            }
        }
#endif

#if UNITY_ANDROID && !UNITY_EDITOR
        /// <summary>
        /// MediaStore に書き込んで、端末のギャラリーから見えるようにする。
        /// Android 10 以降は外部ストレージへの直接書き込みができないため、この経路を使う。
        /// </summary>
        bool SaveToAndroidGallery(byte[] png, string fileName)
        {
            try
            {
                using var unityPlayer = new AndroidJavaClass("com.unity3d.player.UnityPlayer");
                using var activity = unityPlayer.GetStatic<AndroidJavaObject>("currentActivity");
                using var resolver = activity.Call<AndroidJavaObject>("getContentResolver");

                using var values = new AndroidJavaObject("android.content.ContentValues");
                values.Call("put", "_display_name", fileName);
                values.Call("put", "mime_type", "image/png");
                values.Call("put", "relative_path", $"Pictures/{m_AlbumName}");

                using var mediaClass = new AndroidJavaClass("android.provider.MediaStore$Images$Media");
                using var collection = mediaClass.GetStatic<AndroidJavaObject>("EXTERNAL_CONTENT_URI");

                using var uri = resolver.Call<AndroidJavaObject>("insert", collection, values);
                if (uri == null)
                {
                    Debug.LogWarning("[ARPhotoCapture] MediaStore に登録できませんでした。");
                    return false;
                }

                using var stream = resolver.Call<AndroidJavaObject>("openOutputStream", uri);
                if (stream == null)
                {
                    Debug.LogWarning("[ARPhotoCapture] 書き込み先を開けませんでした。");
                    return false;
                }

                stream.Call("write", png);
                stream.Call("flush");
                stream.Call("close");

                Debug.Log($"[ARPhotoCapture] ギャラリー(Pictures/{m_AlbumName})に保存しました: {fileName}");
                return true;
            }
            catch (Exception e)
            {
                Debug.LogWarning($"[ARPhotoCapture] ギャラリー保存に失敗しました: {e.Message}");
                return false;
            }
        }
#endif
    }
}
