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

## Requirements

- Windows 10/11
- .NET 8.0 Runtime

## Build

```bash
dotnet build -c Release
```

## Publish (self-contained)

```bash
dotnet publish -c Release -r win-x64 --self-contained
```

### Trimmed (smaller output)

```bash
dotnet publish -c Release -r win-x64 --self-contained -p:PublishTrimmed=true
```

Reduces the output from ~71 MB to ~18 MB.

## License

MIT — see [LICENSE](LICENSE) for details.
