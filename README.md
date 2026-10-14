# ColorFinder

ColorFinder is a lightweight Linux and macOS file manager with persistent color tags and automatic color rules.

## Run from source

ColorFinder requires the .NET 8 SDK. On Linux, its native system WebView also requires GTK 3, WebKitGTK 4.1, and GStreamer 1.x base plugins (`appsink`). Package names vary by distribution; the GStreamer package is commonly named `gst-plugins-base`, `gstreamer1.0-plugins-base`, or similar.

The included Nix development shell provides all required Linux dependencies:

```bash
nix-shell --run 'dotnet run --project src/ColorFinder.Desktop'
```

If the shell is already active:

```bash
dotnet run --project src/ColorFinder.Desktop
```

ColorFinder uses WKWebView on macOS, so the GStreamer dependency is Linux-specific.

## Linux desktop integration

Version 0.9 desktop-integration assets are in `packaging/linux`. When packaging or installing ColorFinder:

- Install `colorfinder.desktop` in `$XDG_DATA_HOME/applications` or `/usr/share/applications`.
- Install `colorfinder.svg` in the `hicolor/scalable/apps` icon directory.
- Ensure the published `ColorFinder.Desktop` executable is available on `PATH`.

The desktop entry can launch ColorFinder at a directory supplied by the desktop environment.
