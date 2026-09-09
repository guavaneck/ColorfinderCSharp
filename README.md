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
