# PhonePhotoReturn

## 介绍

PhonePhotoReturn 是一个局域网内使用的手机和电脑文件传输工具，主要用于把手机拍照、相册照片和普通文件快速传回 Windows 电脑，也支持从电脑发送文件到 Android App。

项目包含两部分：

- `desktop`：Windows 电脑端接收器，默认构建目标是 .NET Framework 4.5.2；原 Python 版接收器仍保留在项目中。
- `android`：Android 客户端，可扫码或手动输入配对信息，支持拍照上传、相册上传、文件上传，并自动接收电脑发送的文件。

当前功能：

- 手机拍照上传到电脑。
- 手机从相册选择照片上传到电脑。
- 手机选择普通文件上传到电脑。
- 网页端上传照片和文件到电脑。
- 电脑端多选文件发送到 Android App。
- 电脑端照片保存目录和文件保存目录可分别设置。
- 上传和发送文件时尽量保持原文件名不变。
- 电脑端可选择是否固定 token；默认关闭。

本工具只面向本地局域网使用，不包含云上传、后台常驻服务、计划任务或开机自启动逻辑。

## 使用

### 1. 启动电脑端

运行构建后的 `PhonePhotoReturn.exe`。启动后窗口会显示：

- 微信 / App 扫码二维码。
- 扫码地址。
- App 配对内容。
- 固定 token 开关。
- 照片保存目录。
- 文件保存目录。
- 发送到手机区域。
- 日志区域。

电脑和手机需要连接在同一个局域网内。

### 2. 手机上传照片到电脑

Android App 中保留原有照片功能：

- `拍照上传`：打开相机，拍照后上传到电脑。
- `从相册选择`：打开相册，选择照片后上传到电脑。

网页端也保留原有照片功能。用手机浏览器或微信扫码打开电脑端页面后，可以：

- `拍照上传`：调用手机相机拍照并上传。
- `从相册选择`：从相册选择照片并上传。

照片会保存到电脑端的“照片保存目录”。

默认照片保存目录：

```text
用户图片目录\PhonePhotoReturn
```

### 3. 手机上传文件到电脑

Android App 中点击 `上传文件`，会打开系统文件选择器，默认尽量进入手机下载目录。选择一个或多个文件后，文件会上传到电脑端的“文件保存目录”。

网页端也提供 `上传文件` 入口，可从手机浏览器或微信扫码打开页面后上传文件。

默认文件保存目录：

```text
用户下载目录\PhonePhotoReturn
```

### 4. 电脑发送文件到手机

电脑端在 `发送到手机` 区域点击 `选择文件`，可以多选文件。点击 `确认发送` 后，这些文件会加入待发送队列。

Android App 配对后会自动轮询电脑端，发现待发送文件后自动下载到手机：

```text
Download/PhonePhotoReturn
```

电脑发送手机只支持 Android App 接收，网页版不做电脑发手机接收。

### 5. 固定 token

电脑端有 `固定 token` 开关，默认关闭。

- 关闭时：每次启动电脑端都会生成新的 token，手机需要重新扫码或重新配对。
- 开启时：保存当前 token，下次启动电脑端会继续使用同一个 token。

开关状态会保存到配置文件。切换开关后立即生效到配置；运行中的扫码地址不会被强制换 token，避免已配对手机突然失效。

配置文件位置：

```text
C:\Users\<用户名>\AppData\Roaming\PhonePhotoReturn\settings.txt
```

该配置文件同时保存：

- 照片保存目录。
- 文件保存目录。
- 固定 token 开关状态。
- 固定 token 值。

## 构建

### Windows 电脑端

```powershell
cd desktop
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建产物会复制到：

```text
outputs\PhonePhotoReturn.exe
```

如需构建保留的 Python 版本：

```powershell
cd desktop
powershell -NoProfile -ExecutionPolicy Bypass -File .\build-python.ps1
```

### Android App

```powershell
cd android
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

构建产物会复制到：

```text
outputs\PhonePhotoReturn-Android-debug.apk
```

Android 项目包含 `app/libs/zxing-core-3.5.3.jar`，因此不需要通过 Gradle 下载 ZXing 依赖。
