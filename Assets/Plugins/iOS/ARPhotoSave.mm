// 撮った写真を iOS のカメラロールに保存する。
//
// Unity には「カメラロールに入れる」API が無いので、ここだけネイティブで書く。
// Android は MediaStore を使っており(ARPhotoCapture.SaveToAndroidGallery)、
// これはその iOS 版にあたる。
//
// 保存にはユーザーの許可が要る。Info.plist の
// NSPhotoLibraryAddUsageDescription は ARBuildPostProcess が書き込む。

#import <Foundation/Foundation.h>
#import <UIKit/UIKit.h>
#import <Photos/Photos.h>

extern "C" {

/// PNG のバイト列をカメラロールへ保存する。
/// 追加のみの許可(addOnly)で足りるので、写真の読み取り許可は求めない。
void ARPhotoSave_SaveToCameraRoll(const void* data, int length)
{
    if (data == NULL || length <= 0) {
        NSLog(@"[ARPhotoSave] 空のデータが渡されました");
        return;
    }

    NSData* png = [NSData dataWithBytes:data length:length];

    void (^save)(void) = ^{
        [[PHPhotoLibrary sharedPhotoLibrary] performChanges:^{
            PHAssetCreationRequest* request = [PHAssetCreationRequest creationRequestForAsset];
            [request addResourceWithType:PHAssetResourceTypePhoto data:png options:nil];
        } completionHandler:^(BOOL success, NSError* error) {
            if (success) {
                NSLog(@"[ARPhotoSave] カメラロールに保存しました");
            } else {
                NSLog(@"[ARPhotoSave] 保存に失敗しました: %@", error);
            }
        }];
    };

    if (@available(iOS 14, *)) {
        PHAuthorizationStatus status =
            [PHPhotoLibrary authorizationStatusForAccessLevel:PHAccessLevelAddOnly];

        if (status == PHAuthorizationStatusAuthorized || status == PHAuthorizationStatusLimited) {
            save();
        } else if (status == PHAuthorizationStatusNotDetermined) {
            [PHPhotoLibrary requestAuthorizationForAccessLevel:PHAccessLevelAddOnly
                                                       handler:^(PHAuthorizationStatus granted) {
                if (granted == PHAuthorizationStatusAuthorized || granted == PHAuthorizationStatusLimited) {
                    save();
                } else {
                    NSLog(@"[ARPhotoSave] 写真の追加を許可されませんでした");
                }
            }];
        } else {
            NSLog(@"[ARPhotoSave] 写真の追加を許可されていません");
        }
    } else {
        [PHPhotoLibrary requestAuthorization:^(PHAuthorizationStatus granted) {
            if (granted == PHAuthorizationStatusAuthorized) {
                save();
            } else {
                NSLog(@"[ARPhotoSave] 写真の追加を許可されませんでした");
            }
        }];
    }
}

}
