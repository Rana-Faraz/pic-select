# PicSelect

PicSelect is a Windows desktop app for rapidly narrowing down large photo sets through repeated review rounds.

You point it at a folder of images, review photos one by one, and mark each image as `Choose` or `Ignore`. Chosen photos can move into later iterations so you can keep refining a set without touching the original files.

## What It Does

- Non-destructive photo review. Original files are never renamed, moved, or deleted.
- Folder-based projects with local persistence.
- Append-only history for decisions and iteration changes.
- Iteration 1 starts from the imported snapshot. Later iterations start from the previous round's chosen photos.
- Recursive import with low-memory background processing.
- Import status tracking, cancel/force stop, restart, and delete project actions.
- Gallery revisit flow for completed iterations.
- Double-click zoom and drag-to-pan in the review screen.
- Local thumbnail caching after import.
- Local-only storage with SQLite.

## Supported Formats

- `.jpg`
- `.jpeg`
- `.png`
- `.bmp`
- `.gif`
- `.webp`
- `.tif`
- `.tiff`

## Download

Latest first release:

- Installer: [PicSelect-Setup-x64.exe](https://github.com/Rana-Faraz/pic-select/releases/download/v1.0.0/PicSelect-Setup-x64.exe)
- Portable build: [PicSelect-win-x64-portable.zip](https://github.com/Rana-Faraz/pic-select/releases/download/v1.0.0/PicSelect-win-x64-portable.zip)
- Release page: [v1.0.0](https://github.com/Rana-Faraz/pic-select/releases/tag/v1.0.0)

Notes:

- The installer is currently unsigned, so Windows SmartScreen may warn before launch.
- Current release packaging is `win-x64`.

## Review Controls

- `Enter`: choose current photo
- `Esc` or `Space`: ignore current photo
- `Left Arrow`: previous photo
- `Right Arrow`: next photo
- Double-click: toggle zoom
- Drag while zoomed: pan the image

## How Import Works

- Imports run in the background.
- Review stays locked until the project snapshot is complete.
- Files are streamed and written in batches to avoid loading huge folders into memory.
- Bad files are skipped and logged instead of aborting the whole import.
- Thumbnails are generated in a separate background phase after import.

## Project Structure

```text
PicSelect/             WinUI 3 desktop app
PicSelect.Core/        Core domain and persistence logic
PicSelect.Core.Tests/  xUnit tests for non-UI behavior
installer/             Inno Setup installer script
.github/workflows/     CI and release automation
```

## Tech Stack

- WinUI 3
- .NET 10
- Windows App SDK
- SQLite
- xUnit
- GitHub Actions
- Inno Setup

## Local Development

Prerequisites:

- Windows 10/11
- .NET 10 SDK
- WinUI 3 / Windows App SDK development environment

Build the app:

```powershell
dotnet build .\PicSelect\PicSelect.csproj
```

Run core tests:

```powershell
dotnet test .\PicSelect.Core.Tests\PicSelect.Core.Tests.csproj
```

Create a local Release publish:

```powershell
dotnet publish .\PicSelect\PicSelect.csproj -c Release -p:PublishProfile=win-x64.pubxml
```

## Packaging

The repo includes an Inno Setup script at [installer/PicSelect.iss](/Users/rfara/Documents/Projects/pic-select/installer/PicSelect.iss).

To build a local installer after producing the self-contained publish output:

```powershell
& "$env:LOCALAPPDATA\Programs\Inno Setup 6\ISCC.exe" .\installer\PicSelect.iss
```

Generated packaging output is ignored through [`.gitignore`](/Users/rfara/Documents/Projects/pic-select/.gitignore) under `artifacts/`.

## CI And Releases

This repo has two GitHub Actions workflows:

- [ci.yml](/Users/rfara/Documents/Projects/pic-select/.github/workflows/ci.yml): builds the app and runs core tests on pushes to `master` and on pull requests
- [release.yml](/Users/rfara/Documents/Projects/pic-select/.github/workflows/release.yml): builds a self-contained Windows release, produces the installer and portable zip, and publishes them to GitHub Releases

To publish a new release:

```powershell
git tag -a v1.0.1 -m "Release v1.0.1"
git push origin v1.0.1
```

That tag triggers the release workflow automatically.

## Current State

PicSelect is usable now, but still early. Some likely next steps:

- code signing for installer and binaries
- better app branding and icon assets
- richer gallery performance tuning for extremely large projects
- stronger installer polish and upgrade handling

## License

No license file is included yet.
