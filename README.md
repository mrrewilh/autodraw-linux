Be sure your user has permissions to access /dev/uinput:
sudo usermod -aG input $USER

To build run the .sh
chmod +x run_linux.sh
./run_linux.sh

Roadmap
👍Absolute coordinate mapping for different displays.
👍Dynamic resolution settings.
-Global Keybinds: Implementation of a direct evdev listener in Rust to bypass Wayland's global input restrictions.
-Flatpak packaging.
