# QuickLook.Plugin.PrprojViewer

Previews Adobe Premiere Pro projects (`.prproj`) in [QuickLook](https://github.com/QL-Win/QuickLook), so you can check what's inside a project without opening Premiere.

- **Sequences**: name, duration (timecode), frame size, frame rate and track count
- **Media**: every imported file with its path, offline files first and in red
- Files that aren't at the saved path but exist relative to the project (the whole folder was moved) show up as "Moved", since Premiere usually relinks those by itself
- Double-click a media file to show it in Explorer

The project is only read, never written, and it works while Premiere has it open.

## Install

Download `QuickLook.Plugin.PrprojViewer.qlplugin` from [Releases](../../releases/latest), select it in Explorer, press Space and click "Click here to install this plugin", then "Restart now".

Tested on QuickLook 4.5.0 (Microsoft Store version) with projects from Premiere Pro 2025/2026.

To uninstall, delete the `QuickLook.Plugin.PrprojViewer` folder and restart QuickLook. It's in:

- `%APPDATA%\pooi.moe\QuickLook\QuickLook.Plugin\` (installer version)
- `%LOCALAPPDATA%\Packages\21090PaddyXu.QuickLook_egxr34yet59cg\LocalCache\Roaming\pooi.moe\QuickLook\QuickLook.Plugin\` (Store version)

## Build

Needs the .NET SDK. Run `.\pack.ps1`, the plugin ends up in `dist\`.

`lib\QuickLook.Common.dll` comes from a QuickLook 4.5.0 install and is only used to compile.

## License

GPL-3.0, same as QuickLook.
