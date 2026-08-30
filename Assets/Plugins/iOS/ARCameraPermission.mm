// iOS のカメラ許可状態を OS に直接聞く。
//
// Unity の Application.HasUserAuthorization(WebCam) は使えない。
// あれは「Unity 自身が RequestUserAuthorization で要求したか」を見ており、
// このアプリでカメラを開くのは ARKit なので、実際には許可済みでも
// 永久に false を返す。結果、許可画面から先に進めなくなる。
//
// AVCaptureDevice に聞けば、誰が要求したかに関係なく本当の状態が分かる。

#import <Foundation/Foundation.h>
#import <AVFoundation/AVFoundation.h>

extern "C" {

/// 0 = 未決定 / 1 = 制限 / 2 = 拒否 / 3 = 許可
int ARCameraPermission_Status()
{
    switch ([AVCaptureDevice authorizationStatusForMediaType:AVMediaTypeVideo]) {
        case AVAuthorizationStatusNotDetermined: return 0;
        case AVAuthorizationStatusRestricted:    return 1;
        case AVAuthorizationStatusDenied:        return 2;
        case AVAuthorizationStatusAuthorized:    return 3;
    }
    return 0;
}

/// 許可を求める。結果は非同期に決まるので、呼び出し側は Status を見張る。
void ARCameraPermission_Request()
{
    [AVCaptureDevice requestAccessForMediaType:AVMediaTypeVideo
                             completionHandler:^(BOOL granted) {
        NSLog(@"[ARCameraPermission] granted = %@", granted ? @"YES" : @"NO");
    }];
}

}
