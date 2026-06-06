package com.codex.phonereturn;

import android.Manifest;
import android.app.Activity;
import android.content.Intent;
import android.content.SharedPreferences;
import android.content.pm.PackageManager;
import android.net.Uri;
import android.os.Bundle;
import android.view.Gravity;
import android.view.OrientationEventListener;
import android.view.Surface;
import android.widget.Button;
import android.widget.EditText;
import android.widget.LinearLayout;
import android.widget.TextView;
import android.widget.Toast;

import androidx.camera.core.CameraSelector;
import androidx.camera.core.ImageAnalysis;
import androidx.camera.core.ImageCapture;
import androidx.camera.core.ImageCaptureException;
import androidx.camera.core.ImageProxy;
import androidx.camera.core.Preview;
import androidx.camera.lifecycle.ProcessCameraProvider;
import androidx.camera.view.PreviewView;
import androidx.core.content.ContextCompat;
import androidx.lifecycle.Lifecycle;
import androidx.lifecycle.LifecycleOwner;
import androidx.lifecycle.LifecycleRegistry;

import com.google.zxing.BinaryBitmap;
import com.google.zxing.DecodeHintType;
import com.google.zxing.MultiFormatReader;
import com.google.zxing.NotFoundException;
import com.google.zxing.PlanarYUVLuminanceSource;
import com.google.zxing.Result;
import com.google.zxing.common.HybridBinarizer;

import org.json.JSONObject;

import java.io.File;
import java.io.FileInputStream;
import java.io.IOException;
import java.io.OutputStream;
import java.nio.ByteBuffer;
import java.net.HttpURLConnection;
import java.net.URL;
import java.util.HashMap;
import java.util.Locale;
import java.util.Map;
import java.util.concurrent.ExecutorService;
import java.util.concurrent.Executors;

import com.google.common.util.concurrent.ListenableFuture;

public class MainActivity extends Activity implements LifecycleOwner {
    private static final int REQUEST_CAMERA = 1001;
    private static final String PREFS = "phone_photo_return";
    private static final String KEY_BASE_URL = "base_url";
    private static final String KEY_TOKEN = "token";
    private static final String KEY_HOST = "host";
    private static final String KEY_PORT = "port";

    private final ExecutorService worker = Executors.newSingleThreadExecutor();
    private final ExecutorService cameraExecutor = Executors.newSingleThreadExecutor();
    private LifecycleRegistry lifecycleRegistry;
    private ProcessCameraProvider cameraProvider;
    private PreviewView previewView;
    private ImageCapture imageCapture;
    private ImageAnalysis imageAnalysis;
    private OrientationEventListener orientationListener;
    private TextView statusText;
    private String pendingCameraAction;
    private boolean scanning;
    private long lastDecodeAttempt;
    private int cameraSessionId;
    private int targetRotation = Surface.ROTATION_0;

    private String baseUrl;
    private String token;
    private String host;
    private int port;

    @Override
    protected void onCreate(Bundle savedInstanceState) {
        super.onCreate(savedInstanceState);
        lifecycleRegistry = new LifecycleRegistry(this);
        lifecycleRegistry.setCurrentState(Lifecycle.State.CREATED);
        setupOrientationListener();
        loadPairing();
        handleIntent(getIntent());
        showHome();
    }

    @Override
    protected void onStart() {
        super.onStart();
        lifecycleRegistry.setCurrentState(Lifecycle.State.STARTED);
    }

    @Override
    protected void onResume() {
        super.onResume();
        lifecycleRegistry.setCurrentState(Lifecycle.State.RESUMED);
    }

    @Override
    protected void onPause() {
        lifecycleRegistry.setCurrentState(Lifecycle.State.STARTED);
        super.onPause();
    }

    @Override
    protected void onStop() {
        lifecycleRegistry.setCurrentState(Lifecycle.State.CREATED);
        super.onStop();
    }

    @Override
    protected void onNewIntent(Intent intent) {
        super.onNewIntent(intent);
        handleIntent(intent);
        showHome();
    }

    @Override
    protected void onDestroy() {
        stopCamera();
        worker.shutdown();
        cameraExecutor.shutdown();
        if (orientationListener != null) {
            orientationListener.disable();
        }
        lifecycleRegistry.setCurrentState(Lifecycle.State.DESTROYED);
        super.onDestroy();
    }

    @Override
    public Lifecycle getLifecycle() {
        return lifecycleRegistry;
    }

    @Override
    public void onRequestPermissionsResult(int requestCode, String[] permissions, int[] grantResults) {
        super.onRequestPermissionsResult(requestCode, permissions, grantResults);
        if (requestCode == REQUEST_CAMERA && grantResults.length > 0 && grantResults[0] == PackageManager.PERMISSION_GRANTED) {
            if ("scan".equals(pendingCameraAction)) {
                showScanner();
            } else if ("camera".equals(pendingCameraAction)) {
                showCamera();
            }
        } else {
            toast("需要相机权限");
        }
        pendingCameraAction = null;
    }

    private void handleIntent(Intent intent) {
        if (intent == null || intent.getData() == null) {
            return;
        }
        Pairing pairing = parsePairing(intent.getData().toString());
        if (pairing != null) {
            savePairing(pairing);
            toast("已完成配对");
        }
    }

    private void setupOrientationListener() {
        orientationListener = new OrientationEventListener(this) {
            @Override
            public void onOrientationChanged(int orientation) {
                int rotation = orientationToSurfaceRotation(orientation);
                if (rotation != targetRotation) {
                    targetRotation = rotation;
                    applyTargetRotation();
                }
            }
        };
        if (orientationListener.canDetectOrientation()) {
            orientationListener.enable();
        }
    }

    private int orientationToSurfaceRotation(int orientation) {
        if (orientation == OrientationEventListener.ORIENTATION_UNKNOWN) {
            return targetRotation;
        }
        if (orientation >= 315 || orientation < 45) {
            return Surface.ROTATION_0;
        }
        if (orientation < 135) {
            return Surface.ROTATION_270;
        }
        if (orientation < 225) {
            return Surface.ROTATION_180;
        }
        return Surface.ROTATION_90;
    }

    private void applyTargetRotation() {
        if (imageCapture != null) {
            imageCapture.setTargetRotation(targetRotation);
        }
        if (imageAnalysis != null) {
            imageAnalysis.setTargetRotation(targetRotation);
        }
    }

    private void showHome() {
        stopCamera();

        LinearLayout root = verticalRoot();
        root.setPadding(dp(22), dp(28), dp(22), dp(20));

        TextView title = text("PhonePhotoReturn", 24, true);
        root.addView(title, fullWrap());

        TextView subtitle = text("App 内拍照，自动上传到电脑", 15, false);
        subtitle.setTextColor(0xFF475569);
        subtitle.setPadding(0, dp(8), 0, dp(18));
        root.addView(subtitle, fullWrap());

        TextView pairText = text(pairingText(), 14, false);
        pairText.setTextColor(0xFF334155);
        pairText.setPadding(0, 0, 0, dp(14));
        root.addView(pairText, fullWrap());

        Button scanButton = button("扫码配对电脑端");
        scanButton.setOnClickListener(v -> ensureCameraPermission("scan"));
        root.addView(scanButton, buttonParams());

        Button pasteButton = button("粘贴/输入配对内容");
        pasteButton.setOnClickListener(v -> showManualPair());
        root.addView(pasteButton, buttonParams());

        Button healthButton = button("检测连接");
        healthButton.setEnabled(hasPairing());
        healthButton.setOnClickListener(v -> checkHealth());
        root.addView(healthButton, buttonParams());

        Button cameraButton = button("打开相机");
        cameraButton.setEnabled(hasPairing());
        cameraButton.setOnClickListener(v -> ensureCameraPermission("camera"));
        root.addView(cameraButton, buttonParams());

        Button clearButton = button("清除配对");
        clearButton.setOnClickListener(v -> {
            getSharedPreferences(PREFS, MODE_PRIVATE).edit().clear().apply();
            baseUrl = null;
            token = null;
            host = null;
            port = 0;
            showHome();
        });
        root.addView(clearButton, buttonParams());

        statusText = text("等待操作", 14, false);
        statusText.setTextColor(0xFF2563EB);
        statusText.setPadding(0, dp(18), 0, 0);
        root.addView(statusText, fullWrap());

        setContentView(root);
    }

    private void showManualPair() {
        stopCamera();
        LinearLayout root = verticalRoot();
        root.setPadding(dp(22), dp(28), dp(22), dp(20));

        TextView title = text("输入配对内容", 22, true);
        root.addView(title, fullWrap());

        TextView help = text("可粘贴 App 配对二维码内容，或网页二维码里的完整地址。", 14, false);
        help.setTextColor(0xFF475569);
        help.setPadding(0, dp(8), 0, dp(12));
        root.addView(help, fullWrap());

        EditText input = new EditText(this);
        input.setMinLines(4);
        input.setGravity(Gravity.TOP | Gravity.START);
        input.setHint("photoreturn://pair?... 或 http://电脑IP:5000/?token=...");
        root.addView(input, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, dp(140)));

        Button save = button("保存配对");
        save.setOnClickListener(v -> {
            Pairing pairing = parsePairing(input.getText().toString());
            if (pairing == null) {
                toast("配对内容无效");
                return;
            }
            savePairing(pairing);
            toast("配对成功");
            showHome();
        });
        root.addView(save, buttonParams());

        Button back = button("返回");
        back.setOnClickListener(v -> showHome());
        root.addView(back, buttonParams());

        setContentView(root);
    }

    private void showScanner() {
        stopCamera();
        scanning = true;

        LinearLayout root = verticalRoot();
        root.setPadding(dp(12), dp(16), dp(12), dp(12));
        TextView title = text("扫描电脑端 App 配对二维码", 18, true);
        root.addView(title, fullWrap());

        previewView = new PreviewView(this);
        previewView.setScaleType(PreviewView.ScaleType.FILL_CENTER);
        root.addView(previewView, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1));

        statusText = text("将二维码放入画面中央", 14, false);
        statusText.setTextColor(0xFF2563EB);
        root.addView(statusText, fullWrap());

        Button back = button("返回");
        back.setOnClickListener(v -> showHome());
        root.addView(back, buttonParams());

        setContentView(root);
        startCamera(true);
    }

    private void showCamera() {
        if (!hasPairing()) {
            toast("请先扫码配对");
            showHome();
            return;
        }
        stopCamera();

        LinearLayout root = verticalRoot();
        root.setPadding(dp(12), dp(16), dp(12), dp(12));
        TextView title = text("拍照后自动上传", 18, true);
        root.addView(title, fullWrap());

        previewView = new PreviewView(this);
        previewView.setScaleType(PreviewView.ScaleType.FILL_CENTER);
        root.addView(previewView, new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, 0, 1));

        Button capture = button("拍照并上传");
        capture.setOnClickListener(v -> takePhoto());
        root.addView(capture, buttonParams());

        Button back = button("返回");
        back.setOnClickListener(v -> showHome());
        root.addView(back, buttonParams());

        statusText = text("准备拍照", 14, false);
        statusText.setTextColor(0xFF2563EB);
        root.addView(statusText, fullWrap());

        setContentView(root);
        startCamera(false);
    }

    private void startCamera(boolean scanMode) {
        int sessionId = ++cameraSessionId;
        ListenableFuture<ProcessCameraProvider> providerFuture = ProcessCameraProvider.getInstance(this);
        providerFuture.addListener(() -> {
            try {
                ProcessCameraProvider provider = providerFuture.get();
                if (sessionId != cameraSessionId || previewView == null) {
                    return;
                }
                cameraProvider = provider;
                provider.unbindAll();

                Preview preview = new Preview.Builder().build();
                preview.setSurfaceProvider(previewView.getSurfaceProvider());

                CameraSelector cameraSelector = CameraSelector.DEFAULT_BACK_CAMERA;
                if (scanMode) {
                    imageAnalysis = new ImageAnalysis.Builder()
                            .setBackpressureStrategy(ImageAnalysis.STRATEGY_KEEP_ONLY_LATEST)
                            .setTargetRotation(targetRotation)
                            .build();
                    imageAnalysis.setAnalyzer(cameraExecutor, this::decodePreviewFrame);
                    provider.bindToLifecycle(this, cameraSelector, preview, imageAnalysis);
                    imageCapture = null;
                } else {
                    imageCapture = new ImageCapture.Builder()
                            .setCaptureMode(ImageCapture.CAPTURE_MODE_MINIMIZE_LATENCY)
                            .setTargetRotation(targetRotation)
                            .build();
                    provider.bindToLifecycle(this, cameraSelector, preview, imageCapture);
                    imageAnalysis = null;
                }
            } catch (Exception e) {
                setStatus("启动相机失败: " + e.getMessage());
            }
        }, ContextCompat.getMainExecutor(this));
    }

    private void decodePreviewFrame(ImageProxy image) {
        try {
            long now = System.currentTimeMillis();
            if (!scanning || now - lastDecodeAttempt < 450) {
                return;
            }
            lastDecodeAttempt = now;

            byte[] luminance = imageToLuminance(image);
            int width = image.getWidth();
            int height = image.getHeight();
            int rotationDegrees = image.getImageInfo().getRotationDegrees();
            if (rotationDegrees == 90 || rotationDegrees == 270) {
                luminance = rotateYPlane(luminance, width, height, rotationDegrees);
                int rotatedWidth = height;
                height = width;
                width = rotatedWidth;
            } else if (rotationDegrees == 180) {
                luminance = rotateYPlane(luminance, width, height, rotationDegrees);
            }

            try {
                PlanarYUVLuminanceSource source = new PlanarYUVLuminanceSource(
                        luminance, width, height, 0, 0, width, height, false);
                BinaryBitmap bitmap = new BinaryBitmap(new HybridBinarizer(source));
                MultiFormatReader reader = new MultiFormatReader();
                Map<DecodeHintType, Object> hints = new HashMap<>();
                hints.put(DecodeHintType.TRY_HARDER, Boolean.TRUE);
                reader.setHints(hints);
                Result result = reader.decodeWithState(bitmap);
                Pairing pairing = parsePairing(result.getText());
                if (pairing != null) {
                    scanning = false;
                    runOnUiThread(() -> {
                        savePairing(pairing);
                        toast("配对成功");
                        showHome();
                    });
                }
            } catch (NotFoundException ignored) {
                // No QR in this frame.
            } catch (Exception e) {
                runOnUiThread(() -> setStatus("扫码失败: " + e.getMessage()));
            }
        } finally {
            image.close();
        }
    }

    private byte[] imageToLuminance(ImageProxy image) {
        ImageProxy.PlaneProxy plane = image.getPlanes()[0];
        ByteBuffer buffer = plane.getBuffer();
        int width = image.getWidth();
        int height = image.getHeight();
        int rowStride = plane.getRowStride();
        int pixelStride = plane.getPixelStride();
        byte[] luminance = new byte[width * height];
        int outputOffset = 0;

        for (int y = 0; y < height; y++) {
            int rowStart = y * rowStride;
            for (int x = 0; x < width; x++) {
                luminance[outputOffset++] = buffer.get(rowStart + x * pixelStride);
            }
        }
        return luminance;
    }

    private byte[] rotateYPlane(byte[] data, int width, int height, int rotationDegrees) {
        byte[] rotated = new byte[data.length];
        if (rotationDegrees == 90) {
            int index = 0;
            for (int x = 0; x < width; x++) {
                for (int y = height - 1; y >= 0; y--) {
                    rotated[index++] = data[y * width + x];
                }
            }
        } else if (rotationDegrees == 180) {
            for (int i = 0; i < data.length; i++) {
                rotated[i] = data[data.length - 1 - i];
            }
        } else if (rotationDegrees == 270) {
            int index = 0;
            for (int x = width - 1; x >= 0; x--) {
                for (int y = 0; y < height; y++) {
                    rotated[index++] = data[y * width + x];
                }
            }
        } else {
            return data;
        }
        return rotated;
    }

    private void takePhoto() {
        if (imageCapture == null) {
            setStatus("相机还没有准备好");
            return;
        }
        setStatus("正在拍照...");
        applyTargetRotation();
        File file = new File(getCacheDir(), "photo_" + System.currentTimeMillis() + ".jpg");
        ImageCapture.OutputFileOptions options = new ImageCapture.OutputFileOptions.Builder(file).build();
        imageCapture.takePicture(
                options,
                ContextCompat.getMainExecutor(this),
                new ImageCapture.OnImageSavedCallback() {
                    @Override
                    public void onImageSaved(ImageCapture.OutputFileResults outputFileResults) {
                        uploadPhoto(file);
                    }

                    @Override
                    public void onError(ImageCaptureException exception) {
                        setStatus("拍照失败: " + exception.getMessage());
                    }
                }
        );
    }

    private void uploadPhoto(File file) {
        setStatus("正在上传...");
        worker.execute(() -> {
            String boundary = "----PhonePhotoReturn" + System.currentTimeMillis();
            HttpURLConnection conn = null;
            try {
                URL url = new URL(baseUrl + "/upload");
                conn = (HttpURLConnection) url.openConnection();
                conn.setRequestMethod("POST");
                conn.setDoOutput(true);
                conn.setConnectTimeout(8000);
                conn.setReadTimeout(15000);
                conn.setRequestProperty("Authorization", "Bearer " + token);
                conn.setRequestProperty("Content-Type", "multipart/form-data; boundary=" + boundary);

                OutputStream out = conn.getOutputStream();
                writeAscii(out, "--" + boundary + "\r\n");
                writeAscii(out, "Content-Disposition: form-data; name=\"photo\"; filename=\"" + file.getName() + "\"\r\n");
                writeAscii(out, "Content-Type: image/jpeg\r\n\r\n");
                FileInputStream in = new FileInputStream(file);
                byte[] buffer = new byte[8192];
                int read;
                while ((read = in.read(buffer)) != -1) {
                    out.write(buffer, 0, read);
                }
                in.close();
                writeAscii(out, "\r\n--" + boundary + "--\r\n");
                out.flush();
                out.close();

                int code = conn.getResponseCode();
                boolean ok = code >= 200 && code < 300;
                if (ok) {
                    //noinspection ResultOfMethodCallIgnored
                    file.delete();
                }
                runOnUiThread(() -> setStatus(ok ? "上传成功" : "上传失败: HTTP " + code));
            } catch (Exception e) {
                runOnUiThread(() -> setStatus("上传失败: " + e.getMessage()));
            } finally {
                if (conn != null) {
                    conn.disconnect();
                }
            }
        });
    }

    private void checkHealth() {
        if (!hasPairing()) {
            setStatus("请先扫码配对");
            return;
        }
        setStatus("正在检测连接...");
        worker.execute(() -> {
            HttpURLConnection conn = null;
            try {
                URL url = new URL(baseUrl + "/health");
                conn = (HttpURLConnection) url.openConnection();
                conn.setRequestMethod("GET");
                conn.setConnectTimeout(5000);
                conn.setReadTimeout(5000);
                conn.setRequestProperty("Authorization", "Bearer " + token);
                int code = conn.getResponseCode();
                runOnUiThread(() -> setStatus(code >= 200 && code < 300 ? "电脑端在线" : "连接失败: HTTP " + code));
            } catch (Exception e) {
                runOnUiThread(() -> setStatus("连接失败: " + e.getMessage()));
            } finally {
                if (conn != null) {
                    conn.disconnect();
                }
            }
        });
    }

    private void ensureCameraPermission(String action) {
        if (checkSelfPermission(Manifest.permission.CAMERA) == PackageManager.PERMISSION_GRANTED) {
            if ("scan".equals(action)) {
                showScanner();
            } else {
                showCamera();
            }
            return;
        }
        pendingCameraAction = action;
        requestPermissions(new String[]{Manifest.permission.CAMERA}, REQUEST_CAMERA);
    }

    private Pairing parsePairing(String raw) {
        if (raw == null) {
            return null;
        }
        String text = raw.trim();
        if (text.isEmpty()) {
            return null;
        }

        try {
            if (text.startsWith("{")) {
                JSONObject json = new JSONObject(text);
                if (!"phone_photo_return_pair".equals(json.optString("type"))) {
                    return null;
                }
                String parsedBaseUrl = stripTrailingSlash(json.optString("base_url"));
                String parsedToken = json.optString("token");
                if (parsedBaseUrl.isEmpty() || parsedToken.isEmpty()) {
                    return null;
                }
                return new Pairing(parsedBaseUrl, parsedToken, json.optString("host"), json.optInt("port", 5000));
            }

            Uri uri = Uri.parse(text);
            if ("photoreturn".equals(uri.getScheme()) && "pair".equals(uri.getHost())) {
                String parsedBaseUrl = uri.getQueryParameter("base_url");
                String parsedHost = uri.getQueryParameter("host");
                String parsedToken = uri.getQueryParameter("token");
                int parsedPort = parsePort(uri.getQueryParameter("port"), 5000);
                if ((parsedBaseUrl == null || parsedBaseUrl.isEmpty()) && parsedHost != null && !parsedHost.isEmpty()) {
                    parsedBaseUrl = "http://" + parsedHost + ":" + parsedPort;
                }
                if (parsedBaseUrl == null || parsedBaseUrl.isEmpty() || parsedToken == null || parsedToken.isEmpty()) {
                    return null;
                }
                return new Pairing(stripTrailingSlash(parsedBaseUrl), parsedToken, parsedHost, parsedPort);
            }

            if ("http".equals(uri.getScheme()) || "https".equals(uri.getScheme())) {
                String parsedToken = uri.getQueryParameter("token");
                if (parsedToken == null || parsedToken.isEmpty() || uri.getHost() == null) {
                    return null;
                }
                int parsedPort = uri.getPort() > 0 ? uri.getPort() : ("https".equals(uri.getScheme()) ? 443 : 80);
                String parsedBaseUrl = uri.getScheme() + "://" + uri.getHost() + ":" + parsedPort;
                return new Pairing(parsedBaseUrl, parsedToken, uri.getHost(), parsedPort);
            }
        } catch (Exception ignored) {
            return null;
        }
        return null;
    }

    private void stopCamera() {
        scanning = false;
        cameraSessionId++;
        imageCapture = null;
        imageAnalysis = null;
        previewView = null;
        if (cameraProvider != null) {
            cameraProvider.unbindAll();
        }
    }

    private void loadPairing() {
        SharedPreferences prefs = getSharedPreferences(PREFS, MODE_PRIVATE);
        baseUrl = prefs.getString(KEY_BASE_URL, null);
        token = prefs.getString(KEY_TOKEN, null);
        host = prefs.getString(KEY_HOST, null);
        port = prefs.getInt(KEY_PORT, 0);
    }

    private void savePairing(Pairing pairing) {
        baseUrl = pairing.baseUrl;
        token = pairing.token;
        host = pairing.host;
        port = pairing.port;
        getSharedPreferences(PREFS, MODE_PRIVATE)
                .edit()
                .putString(KEY_BASE_URL, baseUrl)
                .putString(KEY_TOKEN, token)
                .putString(KEY_HOST, host)
                .putInt(KEY_PORT, port)
                .apply();
    }

    private boolean hasPairing() {
        return baseUrl != null && token != null && !baseUrl.isEmpty() && !token.isEmpty();
    }

    private String pairingText() {
        if (!hasPairing()) {
            return "未配对。请先运行电脑端，然后扫描“App 配对”二维码。";
        }
        return String.format(Locale.US, "已配对电脑: %s\n端口: %d", host == null ? baseUrl : host, port);
    }

    private int parsePort(String value, int fallback) {
        try {
            return value == null ? fallback : Integer.parseInt(value);
        } catch (NumberFormatException e) {
            return fallback;
        }
    }

    private String stripTrailingSlash(String value) {
        if (value == null) {
            return "";
        }
        while (value.endsWith("/")) {
            value = value.substring(0, value.length() - 1);
        }
        return value;
    }

    private void writeAscii(OutputStream out, String value) throws IOException {
        out.write(value.getBytes(java.nio.charset.StandardCharsets.US_ASCII));
    }

    private LinearLayout verticalRoot() {
        LinearLayout root = new LinearLayout(this);
        root.setOrientation(LinearLayout.VERTICAL);
        root.setGravity(Gravity.CENTER_HORIZONTAL);
        root.setBackgroundColor(0xFFF8FAFC);
        return root;
    }

    private TextView text(String value, int sp, boolean bold) {
        TextView view = new TextView(this);
        view.setText(value);
        view.setTextSize(sp);
        view.setTextColor(0xFF0F172A);
        if (bold) {
            view.setTypeface(view.getTypeface(), android.graphics.Typeface.BOLD);
        }
        return view;
    }

    private Button button(String label) {
        Button button = new Button(this);
        button.setText(label);
        button.setAllCaps(false);
        return button;
    }

    private LinearLayout.LayoutParams fullWrap() {
        return new LinearLayout.LayoutParams(LinearLayout.LayoutParams.MATCH_PARENT, LinearLayout.LayoutParams.WRAP_CONTENT);
    }

    private LinearLayout.LayoutParams buttonParams() {
        LinearLayout.LayoutParams params = fullWrap();
        params.setMargins(0, dp(6), 0, dp(6));
        return params;
    }

    private int dp(int value) {
        return Math.round(value * getResources().getDisplayMetrics().density);
    }

    private void setStatus(String message) {
        if (statusText != null) {
            statusText.setText(message);
        }
    }

    private void toast(String message) {
        Toast.makeText(this, message, Toast.LENGTH_SHORT).show();
    }

    private static class Pairing {
        final String baseUrl;
        final String token;
        final String host;
        final int port;

        Pairing(String baseUrl, String token, String host, int port) {
            this.baseUrl = baseUrl;
            this.token = token;
            this.host = host;
            this.port = port;
        }
    }
}
