# ColorFinder

ColorFinder is an early-stage, cross-platform file manager built around persistent color metadata. It uses a C#/.NET backend and a vanilla HTML/CSS/JavaScript interface hosted in the operating system WebView through Photino.

The first implementation includes:

- home-directory listing and folder navigation;
- back, forward, and up history owned by the C# backend;
- grid and list views;
- single-item selection;
- seven built-in color tags and color removal;
- persistent JSON metadata stored in the user's configuration directory;
- Core unit tests for persistence, listing, and navigation.

## Requirements

- .NET 8 SDK to build, and the .NET 8 runtime to run
- Linux: GTK 3 and WebKitGTK
- macOS: the system WKWebView runtime

## Run

On NixOS or any system with Nix installed, enter the development environment first:

```sh
nix-shell
```

The shell provides the .NET 8 SDK along with GTK and WebKitGTK runtime libraries.

```sh
dotnet restore
dotnet run --project src/ColorFinder.Desktop
```

Run the tests with:

```sh
dotnet test
```

On Linux, assignments are stored at `$XDG_CONFIG_HOME/colorfinder/colors.json`, or `~/.config/colorfinder/colors.json` when `XDG_CONFIG_HOME` is unset. On macOS they are stored under `~/Library/Application Support/colorfinder/colors.json`.

## Architecture

- `ColorFinder.Core` owns filesystem access, navigation, and color metadata.
- `ColorFinder.Desktop` hosts the native WebView and exposes a narrow JSON message bridge.
- `ColorFinder.UI` renders state and owns transient selection/view state.

See [ColorFinder C# Rewrite Plan.md](ColorFinder%20C%23%20Rewrite%20Plan.md) for the product roadmap.
