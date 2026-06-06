import io
import json
import queue
import secrets
import socket
import sys
import threading
import time
import webbrowser
from datetime import datetime
from pathlib import Path
from tkinter import END, DISABLED, NORMAL, StringVar, Text, Tk, filedialog, ttk
from urllib.parse import urlencode

import qrcode
from flask import Flask, abort, jsonify, render_template, request
from werkzeug.utils import secure_filename


APP_NAME = "手机拍照自动回传电脑"
PORT = 5000
TOKEN = secrets.token_urlsafe(18)
DEFAULT_UPLOAD_DIR = Path.home() / "Pictures" / "PhonePhotoReturn"
ALLOWED_EXTENSIONS = {".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic"}

upload_dir = DEFAULT_UPLOAD_DIR
events = queue.Queue()


def resource_path(relative_path):
    base_path = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))
    return base_path / relative_path


app = Flask(__name__, template_folder=str(resource_path("templates")))


def ensure_upload_dir():
    upload_dir.mkdir(parents=True, exist_ok=True)


def get_local_ip():
    s = socket.socket(socket.AF_INET, socket.SOCK_DGRAM)
    try:
        s.connect(("8.8.8.8", 80))
        return s.getsockname()[0]
    except OSError:
        return "127.0.0.1"
    finally:
        s.close()


def base_url():
    return f"http://{get_local_ip()}:{PORT}"


def web_url():
    return f"{base_url()}/?token={TOKEN}"


def app_pair_payload():
    host = get_local_ip()
    return {
        "type": "phone_photo_return_pair",
        "version": 1,
        "name": APP_NAME,
        "host": host,
        "port": PORT,
        "base_url": f"http://{host}:{PORT}",
        "token": TOKEN,
        "upload_path": "/upload",
        "health_path": "/health",
    }


def app_pair_url():
    payload = app_pair_payload()
    query = urlencode(
        {
            "host": payload["host"],
            "port": payload["port"],
            "token": payload["token"],
            "base_url": payload["base_url"],
        }
    )
    return f"photoreturn://pair?{query}"


def allowed_file(filename):
    suffix = Path(filename).suffix.lower()
    return suffix in ALLOWED_EXTENSIONS or not suffix


def unique_photo_path(original_name):
    ensure_upload_dir()
    original_name = secure_filename(original_name or "")
    suffix = Path(original_name).suffix.lower()
    if suffix not in ALLOWED_EXTENSIONS:
        suffix = ".jpg"

    timestamp = datetime.now().strftime("%Y-%m-%d_%H-%M-%S")
    candidate = upload_dir / f"{timestamp}{suffix}"
    if not candidate.exists():
        return candidate

    high_res = datetime.now().strftime("%Y-%m-%d_%H-%M-%S_%f")
    return upload_dir / f"{high_res}{suffix}"


def request_token():
    auth_header = request.headers.get("Authorization", "")
    if auth_header.startswith("Bearer "):
        return auth_header.removeprefix("Bearer ").strip()
    return request.args.get("token") or request.form.get("token")


def require_token():
    if request_token() != TOKEN:
        abort(403)


@app.get("/")
def index():
    require_token()
    return render_template("mobile.html", token=TOKEN)


@app.get("/health")
def health():
    require_token()
    return jsonify(
        {
            "ok": True,
            "name": APP_NAME,
            "version": 1,
            "upload_path": "/upload",
            "server_time": datetime.now().isoformat(timespec="seconds"),
        }
    )


@app.get("/pair.json")
def pair_json():
    require_token()
    return jsonify(app_pair_payload())


@app.post("/upload")
def upload_file():
    require_token()

    if "photo" not in request.files:
        return jsonify({"error": "No file part"}), 400

    file = request.files["photo"]
    if file.filename == "":
        return jsonify({"error": "No selected file"}), 400

    if not allowed_file(file.filename):
        return jsonify({"error": "Unsupported file type"}), 400

    photo_path = unique_photo_path(file.filename)
    file.save(photo_path)

    msg = f"已保存照片: {photo_path}"
    print(msg, flush=True)
    events.put(msg)
    return jsonify({"message": "Success", "filename": photo_path.name})


def run_server():
    app.run(host="0.0.0.0", port=PORT, debug=False, use_reloader=False)


class PhotoReturnApp:
    def __init__(self):
        self.root = Tk()
        self.root.title(APP_NAME)
        self.root.geometry("720x720")
        self.root.minsize(660, 620)

        self.web_url_var = StringVar(value=web_url())
        self.app_pair_var = StringVar(value=app_pair_url())
        self.path_var = StringVar(value=str(upload_dir))
        self.status_var = StringVar(value="服务已启动，等待手机上传")

        self.build_ui()
        self.refresh_qr_codes()
        self.root.after(300, self.poll_events)

    def build_ui(self):
        self.root.columnconfigure(0, weight=1)
        self.root.rowconfigure(2, weight=1)

        pad = {"padx": 16, "pady": 8}
        title = ttk.Label(self.root, text=APP_NAME, font=("Microsoft YaHei UI", 16, "bold"))
        title.grid(row=0, column=0, sticky="ew", **pad)

        qr_frame = ttk.Frame(self.root)
        qr_frame.grid(row=1, column=0, sticky="ew", padx=16, pady=4)
        qr_frame.columnconfigure(0, weight=1)
        qr_frame.columnconfigure(1, weight=1)

        web_frame = ttk.LabelFrame(qr_frame, text="网页扫码")
        web_frame.grid(row=0, column=0, sticky="nsew", padx=(0, 8))
        web_frame.columnconfigure(0, weight=1)
        self.web_qr_label = ttk.Label(web_frame, anchor="center")
        self.web_qr_label.grid(row=0, column=0, sticky="nsew", padx=10, pady=10)

        app_frame = ttk.LabelFrame(qr_frame, text="App 配对")
        app_frame.grid(row=0, column=1, sticky="nsew", padx=(8, 0))
        app_frame.columnconfigure(0, weight=1)
        self.app_qr_label = ttk.Label(app_frame, anchor="center")
        self.app_qr_label.grid(row=0, column=0, sticky="nsew", padx=10, pady=10)

        info_frame = ttk.Frame(self.root)
        info_frame.grid(row=2, column=0, sticky="nsew", **pad)
        info_frame.columnconfigure(1, weight=1)
        info_frame.rowconfigure(3, weight=1)

        ttk.Label(info_frame, text="网页地址:").grid(row=0, column=0, sticky="w", pady=4)
        ttk.Entry(info_frame, textvariable=self.web_url_var, state="readonly").grid(row=0, column=1, sticky="ew", padx=(8, 0))

        ttk.Label(info_frame, text="App 配对:").grid(row=1, column=0, sticky="w", pady=4)
        ttk.Entry(info_frame, textvariable=self.app_pair_var, state="readonly").grid(row=1, column=1, sticky="ew", padx=(8, 0))

        path_frame = ttk.Frame(info_frame)
        path_frame.grid(row=2, column=0, columnspan=2, sticky="ew", pady=(8, 4))
        path_frame.columnconfigure(1, weight=1)
        ttk.Label(path_frame, text="保存目录:").grid(row=0, column=0, sticky="w")
        ttk.Entry(path_frame, textvariable=self.path_var, state="readonly").grid(row=0, column=1, sticky="ew", padx=8)
        ttk.Button(path_frame, text="更改", command=self.choose_dir).grid(row=0, column=2, sticky="e")

        log_frame = ttk.Frame(info_frame)
        log_frame.grid(row=3, column=0, columnspan=2, sticky="nsew", pady=(8, 0))
        log_frame.rowconfigure(0, weight=1)
        log_frame.columnconfigure(0, weight=1)
        self.log = Text(log_frame, height=8, wrap="word", state=DISABLED)
        self.log.grid(row=0, column=0, sticky="nsew")
        scrollbar = ttk.Scrollbar(log_frame, command=self.log.yview)
        scrollbar.grid(row=0, column=1, sticky="ns")
        self.log.configure(yscrollcommand=scrollbar.set)

        button_frame = ttk.Frame(self.root)
        button_frame.grid(row=3, column=0, sticky="ew", **pad)
        ttk.Label(button_frame, textvariable=self.status_var).pack(side="left")
        ttk.Button(button_frame, text="打开保存目录", command=self.open_upload_dir).pack(side="right")

    def make_qr_image(self, data):
        qr = qrcode.QRCode(version=None, box_size=5, border=2)
        qr.add_data(data)
        qr.make(fit=True)
        image = qr.make_image(fill_color="black", back_color="white")

        buffer = io.BytesIO()
        image.save(buffer, format="PNG")
        buffer.seek(0)

        from tkinter import PhotoImage

        return PhotoImage(data=buffer.read())

    def refresh_qr_codes(self):
        current_web_url = web_url()
        current_app_pair_url = app_pair_url()
        self.web_url_var.set(current_web_url)
        self.app_pair_var.set(current_app_pair_url)

        self.web_qr_image = self.make_qr_image(current_web_url)
        self.app_qr_image = self.make_qr_image(current_app_pair_url)
        self.web_qr_label.configure(image=self.web_qr_image)
        self.app_qr_label.configure(image=self.app_qr_image)

    def choose_dir(self):
        global upload_dir
        directory = filedialog.askdirectory(title="选择照片保存目录", initialdir=str(upload_dir))
        if not directory:
            return
        upload_dir = Path(directory)
        ensure_upload_dir()
        self.path_var.set(str(upload_dir))
        self.add_log(f"保存目录已切换: {upload_dir}")

    def open_upload_dir(self):
        ensure_upload_dir()
        webbrowser.open(str(upload_dir))

    def add_log(self, message):
        now = time.strftime("%H:%M:%S")
        self.log.configure(state=NORMAL)
        self.log.insert(END, f"[{now}] {message}\n")
        self.log.see(END)
        self.log.configure(state=DISABLED)

    def poll_events(self):
        while True:
            try:
                msg = events.get_nowait()
            except queue.Empty:
                break
            self.status_var.set("收到新照片")
            self.add_log(msg)
        self.root.after(300, self.poll_events)

    def run(self):
        self.root.mainloop()


def main():
    ensure_upload_dir()
    server_thread = threading.Thread(target=run_server, daemon=True)
    server_thread.start()
    PhotoReturnApp().run()


if __name__ == "__main__":
    main()
