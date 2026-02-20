# AudioSync

A lightweight Windows system tray utility that automatically syncs your default communication audio device to match your default output device.

## Why?

Windows maintains separate "Default" and "Default Communications" audio devices. When you switch your output device (e.g. from speakers to headphones), the communications device often stays on the old one — causing Discord, Teams, etc. to use the wrong output. AudioSync fixes this by automatically keeping them in sync.

## Features

- **Automatic sync** — changes the communication device whenever the default output device changes
- **System tray icon** — green speaker when syncing, gray when paused
- **Left-click** to toggle sync on/off
- **Right-click menu** — manually pick a communication device, toggle startup with Windows, view logs
- **Single instance** — prevents duplicate processes
- **Log rotation** — keeps logs small automatically

## Download

Grab the latest single-file exe from the [Releases](https://github.com/GoldenD/AudioSync/releases) page — no installation or .NET runtime required.

> **Windows SmartScreen warning:** Since the exe is not code-signed, Windows may show a "Windows protected your PC" popup. Click **More info** → **Run anyway**. Alternatively, right-click the downloaded file → **Properties** → check **Unblock** → **OK** before running.

## Requirements

- Windows 10/11
- .NET 8.0 SDK (for building from source only)

## Build

```bash
dotnet build -c Release
```

## Publish (self-contained single file)

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true -p:PublishSingleFile=true
```

Produces a single ~12 MB exe with no dependencies.

## License

MIT — see [LICENSE](LICENSE) for details.
