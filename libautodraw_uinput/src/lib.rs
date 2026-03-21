use std::fs::File;
use std::io::{Read, Write};
use std::os::fd::AsRawFd;
use std::ptr;
use std::sync::atomic::{AtomicBool, Ordering};
use std::sync::Mutex;
use std::thread;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use nix::{ioctl_none, ioctl_write_int, ioctl_write_ptr, libc::c_ulong};

// ── Raw input event structures ─────────────────────────────────────────────

#[derive(Clone, Copy)]
#[repr(C)]
struct Timeval {
    seconds: u64,
    microseconds: u64,
}

#[repr(C)]
struct InputEvent {
    time: Timeval,
    event_type: u16,
    code: u16,
    value: i32,
}

impl InputEvent {
    fn to_bytes(&self) -> [u8; 24] {
        let mut buf = [0u8; 24];
        buf[0..8].copy_from_slice(&self.time.seconds.to_le_bytes());
        buf[8..16].copy_from_slice(&self.time.microseconds.to_le_bytes());
        buf[16..18].copy_from_slice(&self.event_type.to_le_bytes());
        buf[18..20].copy_from_slice(&self.code.to_le_bytes());
        buf[20..24].copy_from_slice(&self.value.to_le_bytes());
        buf
    }
}

// ── uinput device setup structures ─────────────────────────────────────────

#[repr(C)]
struct DeviceSetup {
    id: InputId,
    name: [u8; 80],
    ff_effects_max: u32,
}

#[repr(C)]
struct InputId {
    bustype: u16,
    vendor: u16,
    product: u16,
    version: u16,
}

#[repr(C)]
struct UinputAbsSetup {
    code: u32,
    absinfo: AbsInfo,
}

#[repr(C)]
struct AbsInfo {
    value: i32,
    minimum: i32,
    maximum: i32,
    fuzz: i32,
    flat: i32,
    resolution: i32,
}

const DEVICE_SETUP: DeviceSetup = DeviceSetup {
    id: InputId {
        bustype: 0x03,
        vendor: 0x1234,
        product: 0x5678,
        version: 1,
    },
    name: {
        let mut n = [0u8; 80];
        let label = b"AutoDraw Virtual Mouse+Keyboard";
        let len = if label.len() > 79 { 79 } else { label.len() };
        let mut i = 0;
        while i < len {
            n[i] = label[i];
            i += 1;
        }
        n
    },
    ff_effects_max: 0,
};

// ── ioctl definitions ─────────────────────────────────────────────────────

const UINPUT_IOCTL_BASE: c_ulong = b'U' as c_ulong;
ioctl_none!(ui_dev_create, UINPUT_IOCTL_BASE, 1);
ioctl_none!(ui_dev_destroy, UINPUT_IOCTL_BASE, 2);
ioctl_write_int!(ui_set_evbit, UINPUT_IOCTL_BASE, 100);
ioctl_write_int!(ui_set_keybit, UINPUT_IOCTL_BASE, 101);
ioctl_write_int!(ui_set_absbit, UINPUT_IOCTL_BASE, 103);
ioctl_write_ptr!(ui_dev_setup, UINPUT_IOCTL_BASE, 3, DeviceSetup);
ioctl_write_ptr!(ui_abs_setup, UINPUT_IOCTL_BASE, 4, UinputAbsSetup);

// ── Linux input constants ─────────────────────────────────────────────────

const EV_SYN: u16 = 0x00;
const EV_ABS: u16 = 0x03;
const EV_KEY: u16 = 0x01;
const SYN_REPORT: u16 = 0x00;
const ABS_X: u16 = 0x00;
const ABS_Y: u16 = 0x01;
const BTN_LEFT: u16 = 0x110;
const BTN_RIGHT: u16 = 0x111;
const KEY_ESC: u16 = 1;
const KEY_LEFTCTRL: u16 = 29;
const KEY_LEFTSHIFT: u16 = 42;
const KEY_V: u16 = 47;

pub const AUTODRAW_BUTTON_LEFT: u8 = 0;
pub const AUTODRAW_BUTTON_RIGHT: u8 = 1;

// ── Global listener state ──────────────────────────────────────────────────

static ESC_PRESSED: AtomicBool = AtomicBool::new(false);
static LISTENER_RUNNING: AtomicBool = AtomicBool::new(false);

// ── Mouse handle ──────────────────────────────────────────────────────────

#[repr(C)]
pub struct Mouse {
    file: File,
    width: i32,
    height: i32,
    listener_handle: Mutex<Option<thread::JoinHandle<()>>>,
}

impl Mouse {
    fn new(width: i32, height: i32) -> Result<Self, String> {
        if width <= 0 || height <= 0 {
            return Err(format!("invalid resolution: {width}x{height}"));
        }

        let file = File::options()
            .write(true)
            .open("/dev/uinput")
            .map_err(|e| format!("failed to open /dev/uinput: {e}"))?;

        let fd = file.as_raw_fd();

        unsafe {
            ui_set_evbit(fd, EV_SYN as u64).map_err(|e| format!("set_evbit SYN: {e}"))?;
            ui_set_evbit(fd, EV_ABS as u64).map_err(|e| format!("set_evbit ABS: {e}"))?;
            ui_set_evbit(fd, EV_KEY as u64).map_err(|e| format!("set_evbit KEY: {e}"))?;

            ui_set_absbit(fd, ABS_X as u64).map_err(|e| format!("set_absbit X: {e}"))?;
            ui_set_absbit(fd, ABS_Y as u64).map_err(|e| format!("set_absbit Y: {e}"))?;

            ui_set_keybit(fd, BTN_LEFT as u64).map_err(|e| format!("set_keybit LEFT: {e}"))?;
            ui_set_keybit(fd, BTN_RIGHT as u64).map_err(|e| format!("set_keybit RIGHT: {e}"))?;
            ui_set_keybit(fd, KEY_ESC as u64).map_err(|e| format!("set_keybit ESC: {e}"))?;
            ui_set_keybit(fd, KEY_LEFTCTRL as u64).map_err(|e| format!("set_keybit LCTRL: {e}"))?;
            ui_set_keybit(fd, KEY_LEFTSHIFT as u64)
                .map_err(|e| format!("set_keybit LSHIFT: {e}"))?;
            ui_set_keybit(fd, KEY_V as u64).map_err(|e| format!("set_keybit V: {e}"))?;

            ui_abs_setup(
                fd,
                &UinputAbsSetup {
                    code: ABS_X as u32,
                    absinfo: AbsInfo {
                        value: 0,
                        minimum: 0,
                        maximum: width - 1,
                        fuzz: 0,
                        flat: 0,
                        resolution: 0,
                    },
                },
            )
            .map_err(|e| format!("abs_setup X: {e}"))?;

            ui_abs_setup(
                fd,
                &UinputAbsSetup {
                    code: ABS_Y as u32,
                    absinfo: AbsInfo {
                        value: 0,
                        minimum: 0,
                        maximum: height - 1,
                        fuzz: 0,
                        flat: 0,
                        resolution: 0,
                    },
                },
            )
            .map_err(|e| format!("abs_setup Y: {e}"))?;

            ui_dev_setup(fd, &DEVICE_SETUP).map_err(|e| format!("dev_setup: {e}"))?;
            ui_dev_create(fd).map_err(|e| format!("dev_create: {e}"))?;
        }

        Ok(Self {
            file,
            width,
            height,
            listener_handle: Mutex::new(None),
        })
    }

    fn write_event(&mut self, event: &InputEvent) -> Result<(), String> {
        self.file
            .write_all(&event.to_bytes())
            .map_err(|e| format!("write event: {e}"))
    }

    fn now_timeval() -> Timeval {
        let now = SystemTime::now()
            .duration_since(UNIX_EPOCH)
            .expect("system clock before epoch");
        Timeval {
            seconds: now.as_secs(),
            microseconds: now.subsec_micros() as u64,
        }
    }

    fn send_syn(&mut self, time: Timeval) -> Result<(), String> {
        self.write_event(&InputEvent {
            time,
            event_type: EV_SYN,
            code: SYN_REPORT,
            value: 0,
        })
    }

    fn move_abs(&mut self, x: i32, y: i32, interval_100ns: u64) -> Result<(), String> {
        let x = x.clamp(0, self.width - 1);
        let y = y.clamp(0, self.height - 1);
        let time = Self::now_timeval();
        self.write_event(&InputEvent {
            time,
            event_type: EV_ABS,
            code: ABS_X,
            value: x,
        })?;
        self.write_event(&InputEvent {
            time,
            event_type: EV_ABS,
            code: ABS_Y,
            value: y,
        })?;
        self.send_syn(time)?;
        if interval_100ns > 0 {
            thread::sleep(ticks_to_duration(interval_100ns));
        }
        Ok(())
    }

    fn click(&mut self, button: u8, pressed: bool) -> Result<(), String> {
        let code = match button {
            AUTODRAW_BUTTON_LEFT => BTN_LEFT,
            AUTODRAW_BUTTON_RIGHT => BTN_RIGHT,
            _ => return Err(format!("unknown button id: {button}")),
        };
        let time = Self::now_timeval();
        self.write_event(&InputEvent {
            time,
            event_type: EV_KEY,
            code,
            value: pressed as i32,
        })?;
        self.send_syn(time)
    }

    fn key_event(&mut self, key_code: u16, pressed: bool) -> Result<(), String> {
        let time = Self::now_timeval();
        self.write_event(&InputEvent {
            time,
            event_type: EV_KEY,
            code: key_code,
            value: pressed as i32,
        })?;
        self.send_syn(time)
    }

    fn paste_hex(&mut self, hex: &str) -> Result<(), String> {
        let mut child = std::process::Command::new("xclip")
            .arg("-selection")
            .arg("clipboard")
            .arg("-i")
            .stdin(std::process::Stdio::piped())
            .stdout(std::process::Stdio::null())
            .stderr(std::process::Stdio::null())
            .spawn()
            .map_err(|e| format!("failed to spawn xclip: {e}"))?;

        if let Some(mut stdin) = child.stdin.take() {
            stdin
                .write_all(hex.as_bytes())
                .map_err(|e| format!("xclip write: {e}"))?;
        }
        child.wait().map_err(|e| format!("xclip wait: {e}"))?;

        thread::sleep(Duration::from_millis(10));
        self.key_event(KEY_LEFTCTRL, true)?;
        thread::sleep(Duration::from_millis(5));
        self.key_event(KEY_V, true)?;
        thread::sleep(Duration::from_millis(5));
        self.key_event(KEY_V, false)?;
        thread::sleep(Duration::from_millis(5));
        self.key_event(KEY_LEFTCTRL, false)?;
        Ok(())
    }
}

impl Drop for Mouse {
    fn drop(&mut self) {
        let _ = unsafe { ui_dev_destroy(self.file.as_raw_fd()) };
    }
}

// ── Helpers ────────────────────────────────────────────────────────────────

fn ticks_to_duration(ticks: u64) -> Duration {
    if ticks == 0 {
        Duration::ZERO
    } else {
        Duration::from_nanos(ticks.saturating_mul(100))
    }
}

fn start_listener(path: String) {
    LISTENER_RUNNING.store(true, Ordering::SeqCst);
    thread::spawn(move || {
        let mut evdev = match File::open(&path) {
            Ok(f) => f,
            Err(e) => {
                eprintln!("[Listener] Cannot open {path}: {e}");
                LISTENER_RUNNING.store(false, Ordering::SeqCst);
                return;
            }
        };
        eprintln!("[Listener] Listening on {path}");
        let mut buf = [0u8; 24];
        loop {
            if !LISTENER_RUNNING.load(Ordering::SeqCst) {
                break;
            }
            match evdev.read_exact(&mut buf) {
                Ok(()) => {
                    let event_type = u16::from_le_bytes([buf[16], buf[17]]);
                    let code = u16::from_le_bytes([buf[18], buf[19]]);
                    let value = i32::from_le_bytes([buf[20], buf[21], buf[22], buf[23]]);
                    if event_type == EV_KEY && code == KEY_ESC && value == 1 {
                        ESC_PRESSED.store(true, Ordering::SeqCst);
                        eprintln!("[Listener] ESC pressed");
                    }
                }
                Err(_) => {
                    thread::sleep(Duration::from_millis(100));
                }
            }
        }
        eprintln!("[Listener] Stopped");
        LISTENER_RUNNING.store(false, Ordering::SeqCst);
    });
}

// ── FFI functions ─────────────────────────────────────────────────────────

#[no_mangle]
pub extern "C" fn mouse_init(width: i32, height: i32) -> *mut Mouse {
    match Mouse::new(width, height) {
        Ok(m) => Box::into_raw(Box::new(m)),
        Err(_) => ptr::null_mut(),
    }
}

#[no_mangle]
pub extern "C" fn mouse_move_abs(handle: *mut Mouse, x: i32, y: i32, interval_100ns: u64) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let mouse = unsafe { &mut *handle };
    match mouse.move_abs(x, y, interval_100ns) {
        Ok(()) => 0,
        Err(_) => -1,
    }
}

#[no_mangle]
pub extern "C" fn mouse_click(
    handle: *mut Mouse,
    button: u8,
    is_pressed: i32,
    click_delay_100ns: u64,
) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let mouse = unsafe { &mut *handle };
    match mouse.click(button, is_pressed != 0) {
        Ok(()) => {
            let dur = ticks_to_duration(click_delay_100ns);
            if !dur.is_zero() {
                thread::sleep(dur);
            }
            0
        }
        Err(_) => -1,
    }
}

#[no_mangle]
pub extern "C" fn key_event(handle: *mut Mouse, key_code: u16, pressed: i32) -> i32 {
    if handle.is_null() {
        return -1;
    }
    let mouse = unsafe { &mut *handle };
    match mouse.key_event(key_code, pressed != 0) {
        Ok(()) => 0,
        Err(_) => -1,
    }
}

#[no_mangle]
pub extern "C" fn paste_hex(handle: *mut Mouse, hex: *const i8) -> i32 {
    if handle.is_null() || hex.is_null() {
        return -1;
    }
    let mouse = unsafe { &mut *handle };
    let cstr = unsafe { std::ffi::CStr::from_ptr(hex) };
    let hex_str = match cstr.to_str() {
        Ok(s) => s,
        Err(_) => return -1,
    };
    match mouse.paste_hex(hex_str) {
        Ok(()) => 0,
        Err(e) => {
            eprintln!("[paste_hex] {e}");
            -1
        }
    }
}

#[no_mangle]
pub extern "C" fn start_keyboard_listener(event_path: *const i8) -> i32 {
    if LISTENER_RUNNING.load(Ordering::SeqCst) {
        return 0;
    }
    let path = if event_path.is_null() {
        "/dev/input/event0".to_string()
    } else {
        let cstr = unsafe { std::ffi::CStr::from_ptr(event_path) };
        match cstr.to_str() {
            Ok(s) => s.to_string(),
            Err(_) => return -1,
        }
    };
    ESC_PRESSED.store(false, Ordering::SeqCst);
    start_listener(path);
    0
}

#[no_mangle]
pub extern "C" fn stop_keyboard_listener() {
    LISTENER_RUNNING.store(false, Ordering::SeqCst);
}

#[no_mangle]
pub extern "C" fn get_esc_flag() -> i32 {
    if ESC_PRESSED.swap(false, Ordering::SeqCst) {
        1
    } else {
        0
    }
}

#[no_mangle]
pub extern "C" fn listener_is_running() -> i32 {
    if LISTENER_RUNNING.load(Ordering::SeqCst) {
        1
    } else {
        0
    }
}

#[no_mangle]
pub extern "C" fn mouse_destroy(handle: *mut Mouse) {
    if handle.is_null() {
        return;
    }
    unsafe {
        drop(Box::from_raw(handle));
    }
}
