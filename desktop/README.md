# 手机拍照自动回传电脑（干净版）

这是一个本地局域网照片回传工具，电脑端同时兼容网页和 Android App：

- 电脑端启动 Flask 服务，监听 `0.0.0.0:5000`
- 手机可扫“网页扫码”二维码，直接用浏览器拍照上传
- Android App 可扫“App 配对”二维码，拿到电脑 IP、端口和 token
- 手机拍照后只上传到当前电脑
- 照片保存到 `图片\PhonePhotoReturn` 或界面选择的目录
- 二维码包含一次性 token，没有 token 会被拒绝
- 不写注册表、不安装服务、不创建计划任务、不包含外部上传逻辑

## App 接口

- `GET /health?token=...`：检查电脑端在线状态
- `GET /pair.json?token=...`：返回 App 配对信息
- `POST /upload?token=...`：上传照片，multipart 字段名为 `photo`

App 也可以把 token 放到请求头：

```text
Authorization: Bearer <token>
```

## 打包

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File .\build.ps1
```

生成文件会复制到 `outputs` 目录。
