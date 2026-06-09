import ctypes
import io
import ipaddress
import os
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

import qrcode
from flask import Flask, abort, jsonify, render_template, request
from werkzeug.serving import make_server


APP_NAME = "手机拍照自动回传电脑"
PORT = 36666
TOKEN = secrets.token_urlsafe(18)
DEFAULT_UPLOAD_DIR = Path.home() / "Pictures" / "PhonePhotoReturn"
SETTINGS_ROOT = Path(os.environ.get("APPDATA", Path.home() / "AppData" / "Roaming"))
SETTINGS_FILE = SETTINGS_ROOT / "PhonePhotoReturn" / "settings.txt"
ALLOWED_EXTENSIONS = {".jpg", ".jpeg", ".png", ".gif", ".webp", ".bmp", ".heic"}


def is_usable_upload_dir(path):
    return bool(path and str(path).strip())


def load_upload_dir():
    try:
        if SETTINGS_FILE.exists():
            for line in SETTINGS_FILE.read_text(encoding="utf-8").splitlines():
                if line.lower().startswith("upload_directory="):
                    directory = line.split("=", 1)[1].strip()
                    if is_usable_upload_dir(directory):
                        return Path(directory)
    except OSError:
        pass
    return DEFAULT_UPLOAD_DIR


def save_upload_dir(path):
    try:
        SETTINGS_FILE.parent.mkdir(parents=True, exist_ok=True)
        SETTINGS_FILE.write_text(f"upload_directory={path}", encoding="utf-8")
    except OSError:
        pass


upload_dir = load_upload_dir()
events = queue.Queue()
server_ready = threading.Event()

ERROR_BUFFER_OVERFLOW = 111
ERROR_SUCCESS = 0
GAA_FLAG_SKIP_ANYCAST = 0x0002
GAA_FLAG_SKIP_MULTICAST = 0x0004
GAA_FLAG_SKIP_DNS_SERVER = 0x0008
IF_OPER_STATUS_UP = 1
IF_TYPE_SOFTWARE_LOOPBACK = 24
MAX_ADAPTER_ADDRESS_LENGTH = 8


def resource_path(relative_path):
    base_path = Path(getattr(sys, "_MEIPASS", Path(__file__).resolve().parent))
    return base_path / relative_path


app = Flask(__name__, template_folder=str(resource_path("templates")))


def ensure_upload_dir():
    upload_dir.mkdir(parents=True, exist_ok=True)


def is_usable_ipv4(address):
    try:
        ip = ipaddress.ip_address(address)
    except ValueError:
        return False
    return ip.version == 4 and not ip.is_loopback and not ip.is_unspecified


def append_ipv4_address(addresses, address):
    if is_usable_ipv4(address) and address not in addresses:
        addresses.append(address)


def prioritize_ipv4_addresses(addresses):
    private_addresses = []
    other_addresses = []
    link_local_addresses = []
    for address in addresses:
        ip = ipaddress.ip_address(address)
        if ip.is_link_local:
            link_local_addresses.append(address)
        elif ip.is_private:
            private_addresses.append(address)
        else:
            other_addresses.append(address)

    return private_addresses or other_addresses or link_local_addresses or addresses


def get_hostname_ipv4_addresses():
    addresses = []
    hostnames = {socket.gethostname(), socket.getfqdn()}
    for hostname in hostnames:
        if not hostname:
            continue
        try:
            for item in socket.getaddrinfo(hostname, None, socket.AF_INET):
                append_ipv4_address(addresses, item[4][0])
        except OSError:
            pass

        try:
            for address in socket.gethostbyname_ex(hostname)[2]:
                append_ipv4_address(addresses, address)
        except OSError:
            pass

    return addresses


def get_windows_adapter_ipv4_addresses():
    if not sys.platform.startswith("win"):
        return []

    class SocketAddress(ctypes.Structure):
        _fields_ = [
            ("lpSockaddr", ctypes.c_void_p),
            ("iSockaddrLength", ctypes.c_int),
        ]

    class SockaddrIn(ctypes.Structure):
        _fields_ = [
            ("sin_family", ctypes.c_ushort),
            ("sin_port", ctypes.c_ushort),
            ("sin_addr", ctypes.c_ubyte * 4),
            ("sin_zero", ctypes.c_ubyte * 8),
        ]

    class IpAdapterUnicastAddress(ctypes.Structure):
        pass

    IpAdapterUnicastAddress._fields_ = [
        ("Length", ctypes.c_ulong),
        ("Flags", ctypes.c_ulong),
        ("Next", ctypes.POINTER(IpAdapterUnicastAddress)),
        ("Address", SocketAddress),
    ]

    class IpAdapterAddresses(ctypes.Structure):
        pass

    IpAdapterAddresses._fields_ = [
        ("Length", ctypes.c_ulong),
        ("IfIndex", ctypes.c_ulong),
        ("Next", ctypes.POINTER(IpAdapterAddresses)),
        ("AdapterName", ctypes.c_char_p),
        ("FirstUnicastAddress", ctypes.POINTER(IpAdapterUnicastAddress)),
        ("FirstAnycastAddress", ctypes.c_void_p),
        ("FirstMulticastAddress", ctypes.c_void_p),
        ("FirstDnsServerAddress", ctypes.c_void_p),
        ("DnsSuffix", ctypes.c_wchar_p),
        ("Description", ctypes.c_wchar_p),
        ("FriendlyName", ctypes.c_wchar_p),
        ("PhysicalAddress", ctypes.c_ubyte * MAX_ADAPTER_ADDRESS_LENGTH),
        ("PhysicalAddressLength", ctypes.c_ulong),
        ("Flags", ctypes.c_ulong),
        ("Mtu", ctypes.c_ulong),
        ("IfType", ctypes.c_ulong),
        ("OperStatus", ctypes.c_int),
        ("Ipv6IfIndex", ctypes.c_ulong),
        ("ZoneIndices", ctypes.c_ulong * 16),
        ("FirstPrefix", ctypes.c_void_p),
    ]

    get_adapters_addresses = ctypes.windll.iphlpapi.GetAdaptersAddresses
    get_adapters_addresses.argtypes = [
        ctypes.c_ulong,
        ctypes.c_ulong,
        ctypes.c_void_p,
        ctypes.POINTER(IpAdapterAddresses),
        ctypes.POINTER(ctypes.c_ulong),
    ]
    get_adapters_addresses.restype = ctypes.c_ulong

    flags = GAA_FLAG_SKIP_ANYCAST | GAA_FLAG_SKIP_MULTICAST | GAA_FLAG_SKIP_DNS_SERVER
    buffer_size = ctypes.c_ulong(15 * 1024)
    buffer = ctypes.create_string_buffer(buffer_size.value)
    adapter_addresses = ctypes.cast(buffer, ctypes.POINTER(IpAdapterAddresses))
    result = get_adapters_addresses(socket.AF_INET, flags, None, adapter_addresses, ctypes.byref(buffer_size))
    if result == ERROR_BUFFER_OVERFLOW:
        buffer = ctypes.create_string_buffer(buffer_size.value)
        adapter_addresses = ctypes.cast(buffer, ctypes.POINTER(IpAdapterAddresses))
        result = get_adapters_addresses(socket.AF_INET, flags, None, adapter_addresses, ctypes.byref(buffer_size))
    if result != ERROR_SUCCESS:
        return []

    addresses = []
    adapter = adapter_addresses
    while adapter:
        adapter_item = adapter.contents
        if adapter_item.IfType != IF_TYPE_SOFTWARE_LOOPBACK and adapter_item.OperStatus == IF_OPER_STATUS_UP:
            unicast = adapter_item.FirstUnicastAddress
            while unicast:
                socket_address = unicast.contents.Address
                if socket_address.lpSockaddr and socket_address.iSockaddrLength >= ctypes.sizeof(SockaddrIn):
                    sockaddr = ctypes.cast(socket_address.lpSockaddr, ctypes.POINTER(SockaddrIn)).contents
                    if sockaddr.sin_family == socket.AF_INET:
                        append_ipv4_address(addresses, socket.inet_ntoa(bytes(sockaddr.sin_addr)))
                unicast = unicast.contents.Next
        adapter = adapter_item.Next

    return addresses


def get_local_ipv4_addresses():
    addresses = []
    for address in get_windows_adapter_ipv4_addresses():
        append_ipv4_address(addresses, address)
    for address in get_hostname_ipv4_addresses():
        append_ipv4_address(addresses, address)

    return prioritize_ipv4_addresses(addresses)


def get_local_ip():
    addresses = get_local_ipv4_addresses()
    return addresses[0] if addresses else "127.0.0.1"


def check_port_available(host, port):
    test_socket = socket.socket(socket.AF_INET, socket.SOCK_STREAM)
    try:
        if hasattr(socket, "SO_EXCLUSIVEADDRUSE"):
            test_socket.setsockopt(socket.SOL_SOCKET, socket.SO_EXCLUSIVEADDRUSE, 1)
        test_socket.bind((host, port))
    except OSError as exc:
        return False, exc
    finally:
        test_socket.close()
    return True, None


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


def allowed_file(filename):
    suffix = Path(filename).suffix.lower()
    return suffix in ALLOWED_EXTENSIONS or not suffix


def safe_original_file_name(original_name):
    name = str(original_name or "").strip()
    if not name:
        return "photo.jpg"
    for invalid in '<>:"/\\|?*':
        name = name.replace(invalid, "_")
    return name or "photo.jpg"


def photo_suffix(original_name):
    original_name = safe_original_file_name(original_name)
    suffix = Path(original_name).suffix.lower()
    if suffix not in ALLOWED_EXTENSIONS:
        suffix = ".jpg"
    return suffix


def reserve_photo_file(original_name):
    ensure_upload_dir()
    safe_name = safe_original_file_name(original_name)
    suffix = photo_suffix(safe_name)
    stem = Path(safe_name).stem or "photo"
    for index in range(100):
        file_name = f"{stem}{suffix}" if index == 0 else f"{stem}({index}){suffix}"
        photo_path = upload_dir / file_name
        try:
            return photo_path, photo_path.open("xb")
        except FileExistsError:
            continue
    raise RuntimeError("无法创建唯一照片文件名")


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

    try:
        photo_path, output = reserve_photo_file(file.filename)
        with output:
            file.save(output)
    except Exception as exc:
        if "photo_path" in locals() and photo_path.exists():
            photo_path.unlink(missing_ok=True)
        return jsonify({"error": f"Save failed: {exc}"}), 500

    msg = f"已保存照片: {photo_path}"
    print(msg, flush=True)
    events.put(msg)
    return jsonify({"message": "Success", "filename": photo_path.name})


def run_server():
    available, error = check_port_available("0.0.0.0", PORT)
    if not available:
        events.put(("error", f"服务启动失败，端口 {PORT} 不可用: {error}"))
        return

    try:
        server = make_server("0.0.0.0", PORT, app)
    except OSError as exc:
        events.put(("error", f"服务启动失败，端口 {PORT} 不可用: {exc}"))
        return

    server_ready.set()
    events.put(("ready", "服务已启动，等待手机上传"))
    server.serve_forever()


class PhotoReturnApp:
    def __init__(self):
        self.root = Tk()
        self.root.withdraw()
        self.root.title(APP_NAME)
        self.root.iconbitmap(default=str(resource_path("assets/icon.ico")))
        self.root.geometry("620x560")
        self.root.minsize(560, 520)

        self.scan_url_var = StringVar(value="")
        self.path_var = StringVar(value=str(upload_dir))
        self.status_var = StringVar(value="正在启动服务...")

        self.build_ui()
        self.center_window()
        self.root.after(300, self.poll_events)

    def build_ui(self):
        self.root.columnconfigure(0, weight=1)

        pad = {"padx": 10, "pady": 5}
        title = ttk.Label(self.root, text=APP_NAME, font=("Microsoft YaHei UI", 16, "bold"))
        title.grid(row=0, column=0, sticky="ew", **pad)

        qr_frame = ttk.LabelFrame(self.root, text="微信/App 扫码")
        qr_frame.grid(row=1, column=0, sticky="ew", padx=10, pady=(0, 6))
        qr_frame.columnconfigure(0, weight=1)
        self.qr_label = ttk.Label(qr_frame, anchor="center")
        self.qr_label.grid(row=0, column=0, sticky="ew", padx=8, pady=8)

        info_frame = ttk.Frame(self.root)
        info_frame.grid(row=2, column=0, sticky="nsew", **pad)
        info_frame.columnconfigure(1, weight=1)

        ttk.Label(info_frame, text="扫码地址:").grid(row=0, column=0, sticky="w", pady=3)
        self.scan_entry = ttk.Entry(info_frame, textvariable=self.scan_url_var, state="readonly")
        self.scan_entry.grid(row=0, column=1, sticky="ew", padx=(8, 0))
        self.copy_button = ttk.Button(info_frame, text="复制", command=self.copy_scan_url, state=DISABLED)
        self.copy_button.grid(row=0, column=2, sticky="e", padx=(8, 0))

        path_frame = ttk.Frame(info_frame)
        path_frame.grid(row=1, column=0, columnspan=3, sticky="ew", pady=(6, 4))
        path_frame.columnconfigure(1, weight=1)
        ttk.Label(path_frame, text="保存目录:").grid(row=0, column=0, sticky="w")
        ttk.Entry(path_frame, textvariable=self.path_var, state="readonly").grid(row=0, column=1, sticky="ew", padx=8)
        ttk.Button(path_frame, text="更改", command=self.choose_dir).grid(row=0, column=2, sticky="e")

        log_frame = ttk.Frame(info_frame)
        log_frame.grid(row=2, column=0, columnspan=3, sticky="nsew", pady=(8, 0))
        log_frame.columnconfigure(0, weight=1)
        self.log = Text(log_frame, height=6, wrap="word", state=DISABLED)
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
        current_scan_url = web_url()
        self.scan_url_var.set(current_scan_url)

        self.qr_image = self.make_qr_image(current_scan_url)
        self.qr_label.configure(image=self.qr_image)
        self.copy_button.configure(state=NORMAL)

    def disable_scan_code(self):
        self.scan_url_var.set("")
        self.qr_label.configure(image="")
        self.copy_button.configure(state=DISABLED)

    def center_window(self):
        self.root.update_idletasks()
        width = self.root.winfo_width()
        height = self.root.winfo_height()
        x = (self.root.winfo_screenwidth() - width) // 2
        y = (self.root.winfo_screenheight() - height) // 2
        self.root.geometry(f"{width}x{height}+{x}+{y}")
        self.root.deiconify()

    def copy_scan_url(self):
        self.root.clipboard_clear()
        self.root.clipboard_append(self.scan_url_var.get())
        self.status_var.set("扫码地址已复制")

    def choose_dir(self):
        global upload_dir
        directory = filedialog.askdirectory(title="选择照片保存目录", initialdir=str(upload_dir))
        if not directory:
            return
        upload_dir = Path(directory)
        ensure_upload_dir()
        save_upload_dir(upload_dir)
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
                event = events.get_nowait()
            except queue.Empty:
                break
            if isinstance(event, tuple):
                event_type, msg = event
            else:
                event_type, msg = "message", event

            if event_type == "ready":
                self.status_var.set(msg)
                self.refresh_qr_codes()
                self.add_log(msg)
            elif event_type == "error":
                self.status_var.set(msg)
                self.disable_scan_code()
                self.add_log(msg)
            else:
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
