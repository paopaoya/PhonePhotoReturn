# PhonePhotoReturn

PhonePhotoReturn is a local-network photo return tool with two parts:

- `desktop`: Windows receiver. It shows QR codes, receives photos over LAN, and saves them locally.
- `android`: Android client. It pairs with the desktop receiver, captures photos in-app, and uploads them directly to the PC.

The project is designed for local network use. It does not include cloud upload logic, background persistence, services, scheduled tasks, or registry startup.

## Desktop

```powershell
cd desktop
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

## Android

```powershell
cd android
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The Android project includes `app/libs/zxing-core-3.5.3.jar` so it can build without downloading ZXing through Gradle.
