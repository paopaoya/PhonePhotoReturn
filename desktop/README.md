# PhonePhotoReturn Desktop

The default desktop build is now a Windows Forms receiver targeting .NET Framework 4.5.2.

The original Python implementation is still kept in this folder:

- Python app: `app.py`
- Python dependencies: `requirements.txt`
- Python mobile page template: `templates/mobile.html`
- Python build script: `build-python.ps1`

## Receiver Behavior

- Listens on `0.0.0.0:36666`.
- Shows a QR code for WeChat/browser uploads.
- Exposes `/pair.json` for the Android client.
- Receives `POST /upload` multipart uploads with field name `photo`.
- Accepts the token from `?token=...`, form field `token`, or `Authorization: Bearer <token>`.
- Saves photos to the last directory selected in the UI, falling back to `Pictures\PhonePhotoReturn` on first launch.

## Build .NET 4.5.2

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

The built EXE is copied to `..\outputs\PhonePhotoReturn.exe`.

## Build Python Version

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-python.ps1
```

The Python build output is copied to `..\outputs\PhonePhotoReturn-Python.exe`.
