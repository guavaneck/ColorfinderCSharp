# ColorFinder

ColorFinder 1.0 is a lightweight Linux and macOS file manager with persistent color tags, automatic color rules, drag-and-drop file management, and customizable UI skins.

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

After `shell.nix` changes, exit and re-enter the development shell before using the shorter command so GTK receives the updated runtime environment.

ColorFinder uses WKWebView on macOS, so the GStreamer dependency is Linux-specific.

## Linux desktop integration

Version 1.0 desktop-integration assets are in `packaging/linux`. When packaging or installing ColorFinder:

- Install `colorfinder.desktop` in `$XDG_DATA_HOME/applications` or `/usr/share/applications`.
- Install `colorfinder.svg` in the `hicolor/scalable/apps` icon directory.
- Ensure the published `ColorFinder.Desktop` executable is available on `PATH`.

The desktop entry can launch ColorFinder at a directory supplied by the desktop environment.

## Custom UI skins

ColorFinder loads an optional stylesheet from `~/.config/colorfinder/ui/theme.css` on Linux, or the equivalent `colorfinder/ui/theme.css` directory below the macOS Application Support folder. Custom styles load after the built-in UI and can use ordinary classes, IDs, CSS variables, and stable `data-ui` attributes. A minimal terminal example is available in [`packaging/themes/terminal`](packaging/themes/terminal), and the complete authoring guide is in [`docs/custom-ui-skins.md`](docs/custom-ui-skins.md).

The active skin's background, panel, control, text, muted-text, accent, and border colors can be changed with color pickers in Color Settings. Overrides are saved as `colors.json` beside `theme.css`; **Use Skin Defaults** removes them. Tag names and their visible colors can also be changed there. Tag values remain separate from the UI skin so a skin can be replaced without changing file metadata.
