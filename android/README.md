# PhonePhotoReturn Android

Android client for the PhonePhotoReturn desktop receiver.

## Flow

1. Run `PhonePhotoReturn.exe` on the PC.
2. Open this Android app.
3. Tap scan pairing and scan the PC-side "App pairing" QR code.
4. Tap camera, then tap capture.
5. The app uploads the photo directly to the PC receiver.

The app uses CameraX for in-app capture, ML Kit for QR scanning, and OkHttp for upload.

## Build

```powershell
gradle assembleDebug
```

APK output:

```text
app\build\outputs\apk\debug\app-debug.apk
```
