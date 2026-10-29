# TypeBlocks

TypeBlocks records keystrokes and reconstructs readable text history, with password-protected encryption, local storage, and configurable recording controls.

## Core features

- **Recover and clean up typed text.** Readable text modes hide key markers and can apply recorded edits such as backspace, cursor movement, selection, and undo/redo. Switch back to the original events at any time.
- **Find past typing.** Browse by day, search text with **Ctrl+F**, or filter the event table by application, key, or text and sort any column.
- **Protect stored history.** Each user creates their own password and encrypted vault. Recording can continue while the history viewer is locked.
- **Toggle recording quickly.** Use the configurable global hotkey (**Ctrl+Alt+Shift+F11** by default) while the app runs in the background, or use tray Pause/Resume. A brief **Typeblocks: On/Off** popup confirms the change without taking focus.
- **Choose when and where to record.** Exclude entire applications, enable or disable Windows sign-in startup, and keep a paused state across restarts.
- **Keep storage local.** TypeBlocks has no built-in upload or cloud-sync feature and does not capture screenshots, clipboard contents, or window titles.

## Download and first run

1. Download **TypeBlocks-v0.1.0-win-x64.zip** from the [v0.1.0 preview release](https://github.com/RagDel/TypeBlocks/releases/tag/v0.1.0) and extract it to a permanent folder.
2. Install the latest **.NET 8 Desktop Runtime for Windows x64** from [Microsoft](https://dotnet.microsoft.com/en-us/download/dotnet/8.0), if needed.
3. Run **TypeBlocks.exe**. First-run setup requires a new password of at least 12 characters and confirmation **before recording can begin**. There is no password recovery.
4. Choose whether to start at Windows sign-in; this option is checked by default. Recording normally starts after setup; if the shortcut is unavailable, it starts paused and shows a warning. The app stays visible in the system tray, possibly inside its overflow menu.
5. Pause recording and wait for **Off** before entering sensitive information. Configure exclusions and your preferred shortcut in **Settings**; double-click the tray icon to open history.

This is a portable, unsigned Windows x64 preview, with no installer. Keep the executable in the same location after enabling startup. .NET 8 support ends **November 10, 2026**; see [Microsoft's support policy](https://dotnet.microsoft.com/en-us/platform/support/policy/dotnet-core).

## Privacy and limitations

History is encrypted locally under `%LOCALAPPDATA%\TypeBlocks`. Each saved batch uses AES-256-GCM, with its key wrapped using RSA-3072 OAEP-SHA256. The private key is encrypted with AES-256-GCM using a password-derived key from PBKDF2-SHA256, a random salt, and 600,000 iterations. No user's password or vault key is built into the app. Public source and release packages exclude personal recordings, vault files, settings, and private development history.

- **Cleaning the view does not erase recorded events.** Text modes only change the display. Reconstruction is best effort; pasted text, mouse edits, existing document content, autocomplete, and some input methods cannot be reproduced reliably.
- **Sensitive fields may still be recorded.** Password-field detection is incomplete, especially in browsers. Pause first or exclude the entire sensitive application.
- **Unlocking lasts up to one hour**, including after closing the viewer. Use **Lock & close** to lock immediately. Windows session lock/disconnect also locks history. Encryption cannot protect an unlocked viewer, malware running as you, or a compromised Windows session.
- **History has no automatic expiry.** To remove a day, exit TypeBlocks and delete its daily `.tmh` file from the `History` folder. To uninstall, disable sign-in startup, exit, and remove the executable and local data folder. Deleting data is irreversible without a backup.

Never attach real history, vault files, settings, or screenshots of private typing to public issues.

## Build from source

Requires Windows and the .NET 8 SDK. Built with C#, WinForms, Win32, and built-in .NET cryptography; no third-party NuGet packages.

```powershell
.\test.ps1
.\build.ps1
```

The executable is written to `artifacts/app/TypeBlocks.exe`. Tests use synthetic records and temporary vaults with live recording disabled. They do not establish compatibility with every physical keyboard, layout, or application.

## License and responsible use

Created by [Tilemachos Tragakis](https://github.com/RagDel). Released under the [MIT License](LICENSE), allowing use, modification, and redistribution subject to its terms.

**You are responsible for deciding when recording is active, protecting your password and device, managing retained data, obtaining any required consent, and complying with applicable law.** Use TypeBlocks only for your own typing or with the informed permission of everyone whose input may be recorded. Do not use it for covert monitoring or unauthorized credential collection.

The software is provided **as is**, without warranties, including any guarantee of security, confidentiality, accuracy, or fitness for a particular purpose. To the maximum extent permitted by applicable law, the author and contributors disclaim liability for claims, damages, or losses arising from use, misuse, sensitive-data exposure, or security vulnerabilities. The MIT License contains the governing warranty and liability terms; nothing here excludes liability that cannot legally be excluded.
