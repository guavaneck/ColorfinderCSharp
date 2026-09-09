{ pkgs ? import <nixpkgs> { } }:

let
  gstreamerPlugins = with pkgs.gst_all_1; [
    gst-plugins-base
    gst-plugins-good
  ];
  runtimeLibraries = with pkgs; [
    atk
    cairo
    gdk-pixbuf
    glib
    gtk3
    libnotify
    pango
    stdenv.cc.cc.lib
    webkitgtk_4_1
  ] ++ gstreamerPlugins;
in
pkgs.mkShell {
  packages = with pkgs; [
    dotnet-sdk_8
    pkg-config
  ] ++ runtimeLibraries;

  DOTNET_ROOT = "${pkgs.dotnet-sdk_8}/share/dotnet";
  DOTNET_CLI_TELEMETRY_OPTOUT = "1";
  DOTNET_NOLOGO = "1";
  # Photino 4's GTK host returns corrupt viewport geometry under native Sway/Wayland.
  # XWayland provides correct dimensions while still using the system WebKitGTK.
  GDK_BACKEND = "x11";
  # WebKitGTK initializes GStreamer at startup. appsink is provided by
  # gst-plugins-base; expose Nix-installed plugins to its scanner explicitly.
  GST_PLUGIN_SYSTEM_PATH_1_0 = pkgs.lib.makeSearchPath "lib/gstreamer-1.0" gstreamerPlugins;
  # Prevent background MSBuild nodes from outliving nix-shell's temporary directory.
  MSBUILDDISABLENODEREUSE = "1";

  shellHook = ''
    mkdir -p "$PWD/.tmp"
    export TMPDIR="$PWD/.tmp"
    export LD_LIBRARY_PATH="${pkgs.lib.makeLibraryPath runtimeLibraries}''${LD_LIBRARY_PATH:+:$LD_LIBRARY_PATH}"
  '';
}
