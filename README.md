# TypeBlocks

TypeBlocks records your typing and reconstructs readable text history, with encrypted local storage, password protection, and configurable recording controls.

## Core features

- **Intelligent Text:** applies recorded corrections, cursor movement, selections, and undo/redo to reconstruct the text you wrote.
- **Three ways to view your typing:** All keys shows the recorded key sequence; Pure Text hides key markers; Intelligent Text also applies edits. For example, typing `3`, Backspace, then `4pm` appears as `34pm` in Pure Text and `4pm` in Intelligent Text.
- **Find past text:** browse daily history with the newest blocks first, search with **Ctrl+F**, or filter and sort the event table.
- **Control recording:** use a customizable global hotkey or the tray menu, with clear On/Off popups. Exclude apps and choose whether to start at Windows sign-in.
- **Protect access:** set your own password, choose when history locks automatically, and optionally prepare a recovery code.
- **Choose your appearance:** switch between light and dark themes, with Settings available directly from the history window.

## Why “Blocks”?

Your typing is grouped into blocks by application and pauses. A new block starts when recorded typing switches to a different app or follows a gap of **at least five minutes**. The newest blocks appear first, keeping separate writing sessions easy to find.

[Watch the 58-second demo](https://github.com/RagDel/TypeBlocks/releases/download/v0.3.2/TypeBlocks-demo-v0.3.2.mp4) · Edited walkthrough using synthetic data.

## Download and setup

1. Download **TypeBlocks-v0.3.2-win-x64.zip** from the [latest preview](https://github.com/RagDel/TypeBlocks/releases/tag/v0.3.2) and extract it to a permanent folder.
2. Install the latest [.NET 8 Desktop Runtime for Windows x64](https://dotnet.microsoft.com/en-us/download/dotnet/8.0) if needed, then open **TypeBlocks.exe**. This is a portable app with no installer.
3. Create and confirm a password of at least 12 characters. **Recording cannot begin before setup finishes.** Sign-in startup is selected by default; you can turn it off.
4. Use **Ctrl+Alt+Shift+F11** to pause or resume, or change the shortcut in Settings. Wait for **Off** before entering sensitive information. If the shortcut is unavailable at startup, recording stays paused and a warning appears.
5. Double-click the tray icon to open history. Set up a recovery code in **Settings → Recovery code** before you need it, and save it somewhere private.

**Upgrading:** exit TypeBlocks before replacing the executable, keep its location unchanged if startup is enabled, and back up your encrypted History folder first. Enabling recovery upgrades the vault; v0.1.0 cannot open it afterward.

This preview is unsigned. .NET 8 support ends **November 10, 2026** ([Microsoft's policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core)).

## Privacy and limitations

- History is encrypted under `%LOCALAPPDATA%\TypeBlocks`. TypeBlocks has no built-in upload or cloud-sync feature. Typing history does not include screenshots, clipboard contents, or window titles.
- **Sensitive text may still be recorded**, especially in browser password fields. Pause first or exclude the entire sensitive app.
- **Text modes only change the display.** Original encrypted events remain saved. Intelligent Text is best effort: mouse edits, pasted content, existing document text, and some app-specific behavior cannot be reconstructed reliably. It may start additional blocks for actions it cannot safely replay.
- History locks after **60 minutes by default**, adjustable in Settings. Closing its window does not immediately lock it; use **Lock & close**. Windows session locking also locks history. Storage encryption cannot protect an unlocked viewer or a compromised computer.
- Recovery must be activated in advance. Anyone with the recovery code and vault files can regain access. Losing both your password and recovery code makes history unrecoverable.
- History has no automatic expiry. To remove a day, exit the app and delete its daily `.tmh` file from the History folder. To uninstall, disable sign-in startup, exit, and remove the executable and local data folder.

Public source, downloads, and the demo contain no personal recordings, vault files, passwords, settings, or private development history. Never attach your real history or recovery code to public issues.

## Build from source

Requires Windows and the .NET 8 SDK:

```powershell
.\test.ps1
.\build.ps1
```

The executable is written to `artifacts/app/TypeBlocks.exe`. Tests use synthetic records and temporary vaults with live recording disabled. They do not establish compatibility with every keyboard or application. The embedded Geologica font uses the [SIL Open Font License](src/TypeBlocks/Assets/Fonts/OFL.txt); font and .NET launcher notices accompany the Windows download.

## License and responsible use

Created by [Tilemachos Tragakis](https://github.com/RagDel). Available under the [MIT License](LICENSE).

**You are responsible for when recording is active, protecting your device and access credentials, managing retained data, obtaining required consent, and complying with applicable law.** Use TypeBlocks for your own typing or with the informed permission of everyone whose input may be recorded. Do not use it for covert monitoring or unauthorized credential collection.

The software is provided **as is**, without warranties, including any guarantee of security, confidentiality, accuracy, or fitness for a particular purpose. To the maximum extent permitted by applicable law, the author and contributors disclaim liability for claims, damages, or losses arising from use, misuse, sensitive-data exposure, or security vulnerabilities. The MIT License contains the governing warranty and liability terms; nothing here excludes liability that cannot legally be excluded.
