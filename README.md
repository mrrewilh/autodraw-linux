**I NEED HELP WITH THE LISTENER FOR RUST. ANY PR WOULD BE APPRECIATED.**

---------------------------------------

Be sure your user has permissions to access /dev/uinput:
sudo usermod -aG input $USER and some modprobe shit

To build run have the prerequisites (dotnet-sdk 8.0 rust appimagetool) installed, then run .sh; 
chmod +x run_linux.sh
./run_linux.sh

Roadmap

👍Absolute coordinate mapping for different displays.

👍Dynamic resolution settings.

-Global Keybinds: Implementation of a direct evdev listener in Rust to bypass Wayland's global input restrictions.

-Flatpak packaging.


--------------------

Special thanks to the AutoDraw community and AlexDalas & Siydge for the original software. I only built the native Linux bridge for rust that makes it work on x11 and DE's that has a good x11 support. 
