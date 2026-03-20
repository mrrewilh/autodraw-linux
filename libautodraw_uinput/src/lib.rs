use std::fs::File;
use std::io::Write;
use std::os::fd::AsRawFd;
use std::ptr;
use std::thread;
use std::time::{Duration, SystemTime, UNIX_EPOCH};

use nix::{ioctl_none, ioctl_write_int, ioctl_write_ptr, libc::c_ulong};

// ── Raw input event structures (Linux input_event compatible) ──────────────

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
        bustype: 0x03, // BUS_USB
        vendor: 0x1234,
        product: 0x5678,
        version: 1,
    },
    name: {
        let mut n = [0u8; 80];
        let label = b"AutoDraw Virtual Mouse";
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

// ── Button constants exposed to C# ────────────────────────────────────────

pub const AUTODRAW_BUTTON_LEFT: u8 = 0;
pub const AUTODRAW_BUTTON_RIGHT: u8 = 1;

// ── Mouse handle ──────────────────────────────────────────────────────────

#[repr(C)]
pub struct Mouse {
    file: File,
    width: i32,
    height: i32,
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
            // Enable event types
            ui_set_evbit(fd, EV_SYN as u64).map_err(|e| format!("set_evbit SYN: {e}"))?;
            ui_set_evbit(fd, EV_ABS as u64).map_err(|e| format!("set_evbit ABS: {e}"))?;
            ui_set_evbit(fd, EV_KEY as u64).map_err(|e| format!("set_evbit KEY: {e}"))?;

            // Enable absolute axes
            ui_set_absbit(fd, ABS_X as u64).map_err(|e| format!("set_absbit X: {e}"))?;
            ui_set_absbit(fd, ABS_Y as u64).map_err(|e| format!("set_absbit Y: {e}"))?;

            // Enable buttons
            ui_set_keybit(fd, BTN_LEFT as u64).map_err(|e| format!("set_keybit LEFT: {e}"))?;
            ui_set_keybit(fd, BTN_RIGHT as u64).map_err(|e| format!("set_keybit RIGHT: {e}"))?;

            // Configure ABS_X range: 0 .. width-1
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

            // Configure ABS_Y range: 0 .. height-1
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

    /// Send absolute position via EV_ABS + SYN_REPORT, then sleep for interval.
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
}

impl Drop for Mouse {
    fn drop(&mut self) {
        let _ = unsafe { ui_dev_destroy(self.file.as_raw_fd()) };
    }
}

// ── Helpers ────────────────────────────────────────────────────────────────

/// Convert .NET 100-nanosecond units to Duration.
fn ticks_to_duration(ticks: u64) -> Duration {
    if ticks == 0 {
        Duration::ZERO
    } else {
        Duration::from_nanos(ticks.saturating_mul(100))
    }
}

// ── FFI functions ─────────────────────────────────────────────────────────

/// Initialise a virtual mouse device via /dev/uinput.
/// `width` and `height` set the absolute coordinate range.
/// Returns an opaque handle on success, null on failure.
#[no_mangle]
pub extern "C" fn mouse_init(width: i32, height: i32) -> *mut Mouse {
    match Mouse::new(width, height) {
        Ok(m) => Box::into_raw(Box::new(m)),
        Err(_) => ptr::null_mut(),
    }
}

/// Move the virtual mouse to absolute coordinates (x, y), then sleep.
///
/// `interval_100ns` is in .NET 100-nanosecond ticks.
/// Coordinates are clamped to the screen range set during init.
///
/// Returns 0 on success, -1 on failure.
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

/// Press or release a mouse button, then sleep for `click_delay_100ns`.
///
/// `button`: AUTODRAW_BUTTON_LEFT (0) or AUTODRAW_BUTTON_RIGHT (1).
/// `is_pressed`: 1 = press, 0 = release.
///
/// Returns 0 on success, -1 on failure.
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

/// Destroy the virtual mouse device and free the handle.
#[no_mangle]
pub extern "C" fn mouse_destroy(handle: *mut Mouse) {
    if handle.is_null() {
        return;
    }
    unsafe {
        drop(Box::from_raw(handle));
    }
}
