# PhonePhotoReturn Android

Android client for the PhonePhotoReturn desktop receiver.

## Flow

1. Run `PhonePhotoReturn.exe` on the PC.
2. Open this Android app.
3. Tap scan pairing and scan the PC-side "App pairing" QR code.
4. Tap camera, then tap capture.
5. The app uploads the photo directly to the PC receiver.

You can also tap gallery upload, choose one or more photos from the system picker, then check them in the exact order they should be uploaded.

The app uses CameraX for camera preview/capture, ZXing for QR scanning, and HttpURLConnection for upload.

## Build

```powershell
gradle assembleDebug
```

APK output:

```text
app\build\outputs\apk\debug\app-debug.apk
```
