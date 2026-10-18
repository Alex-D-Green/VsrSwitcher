# VSR Switcher

Turn NVIDIA **RTX Video Super Resolution** on and off — and choose its quality — from the system
tray, a hotkey or the command line, instead of digging through NVIDIA App.

RTX Video Super Resolution (VSR) is the NVIDIA feature that upscales and sharpens video played in a
browser or video player. NVIDIA App keeps its switch a few screens deep; VSR Switcher puts it one
click — or one key combination — away.

I find VSR useful in many cases, but I don't want to waste GPU power when I don't need it. When I
watch a 720p tutorial that shows an app's interface, I definitely want to trade GPU power for a
sharper picture. When I'm watching The Benny Hill Show on YouTube, I don't need VSR at all. NVIDIA
App offers only a global on/off switch, and to reach it you have to click through several pages.
VSR Switcher fixes that: turn VSR on or off with a hotkey or a single click on the tray icon. Your
AI agent can even do it for you now.

## Features

- **Tray icon that shows the state at a glance** — green when Super Resolution is on, grey when it
  is off. Windows can be set to show the icon only while VSR is on.
- **One click or one hotkey** toggles it, restoring the quality you last used.
- **Everything NVIDIA App offers for VSR**: on/off, quality (Auto or 1–4), GPU priority, and the
  on-video status indicator.
- **Stays in step with NVIDIA App** — change something there and the icon follows immediately.
- **Command line** (`vsrswitch`) for scripts, with JSON output.
- **Start with Windows**, optional.
- **Tiny**: a 2 MB installer. No administrator rights needed, no background service, nothing sent
  over the network.

## Requirements

- **Windows 10 or 11**, 64-bit — any edition, Home or Pro.
- An **NVIDIA GeForce RTX** graphics card that supports RTX Video Super Resolution.
- **NVIDIA App** installed, in any folder. VSR Switcher changes settings through NVIDIA App's own
  settings component, the same way NVIDIA App does; the graphics driver alone is not enough.
  Nothing of NVIDIA's is included in the download.
- The **[.NET 10 Desktop Runtime](https://dotnet.microsoft.com/download/dotnet/10.0)** (x64). The
  installer offers to install it if it is missing. Installing .NET needs administrator rights,
  once, because it is shared by every app that uses it.

Not supported: 32-bit Windows (NVIDIA App is 64-bit only), Windows on ARM, and Windows in S mode,
which runs only Microsoft Store apps.

VSR also has to be supported by whatever plays the video — for example Chrome, Edge, Firefox, VLC
or mpv. VSR Switcher controls the driver-wide switch; it cannot make an unsupported player use it.

**Not sure it works on your PC?** Run `vsrswitch doctor` from the VSR Switcher folder. It changes
nothing, and shows which NVIDIA component it found and whether every compatibility check passes.
If something does not fit, VSR Switcher refuses to change settings rather than risk changing the
wrong one — and says why.

## Install

Each [release](../../releases/latest) offers two downloads: an installer and a portable ZIP.
Neither needs administrator rights.

The release is not code-signed yet, so Windows SmartScreen may say *"Windows protected your PC"*
when you run either one. Choose **More info → Run anyway**.

### Installer (recommended)

Run `VsrSwitcher-<version>-setup.exe`. It installs VSR Switcher for your user account only, into
`%LOCALAPPDATA%\Programs\VSR Switcher`, with a Start menu entry. It offers to start VSR Switcher
with Windows and to add the `vsrswitch` command line to your `PATH`. Running a newer installer
upgrades in place, keeping your settings.

If the .NET 10 Desktop Runtime is missing, the installer offers to download and install it from
Microsoft (about 60 MB); Windows then asks for administrator permission.

### Portable ZIP

1. Download `VsrSwitcher-<version>-win-x64.zip`.
2. Extract it to a folder where it can stay, for example `%LOCALAPPDATA%\Programs\VSR Switcher`.
   Pick the final place now: Windows ties the tray icon's visibility setting and the "Start with
   Windows" entry to the program's location, so moving the folder later means setting those up
   again.
3. Run **`VsrSwitcher.exe`**, and right-click its icon to tick **Start with Windows** if you want.

   If the .NET 10 Desktop Runtime is missing, Windows says so at this point and offers a download
   link.

### After installing

The icon appears in the tray — at first inside the hidden-icons overflow (**^**), where Windows
puts every new icon. See [Show the icon only while VSR is on](#show-the-icon-only-while-vsr-is-on).

## Using the tray app

- **Left-click** the icon to turn Super Resolution on or off. Turning it on restores the quality
  you used last (NVIDIA App's own On button always picks Auto).
- **Ctrl+Alt+Shift+V** does the same from anywhere — see [Hotkey](#hotkey).
- **Right-click** for the menu:

| Menu item | What it does |
| --- | --- |
| **Super Resolution** | On/off. The current hotkey is shown beside it. |
| **Quality** | Auto, or a fixed level from 1 (very low) to 4 (high). Choosing a level while VSR is off turns it on at that level. |
| **GPU priority (Auto only)** | Low, Medium or High — how much GPU the Auto quality may use. Same as NVIDIA App's *GPU priority* slider. |
| **Show status indicator on video** | NVIDIA's small "RTX VSR" badge drawn over video while VSR is working. |
| **Start with Windows** | Start VSR Switcher when you sign in. |
| **Hotkey…** | Change or switch off the hotkey. |
| **About VSR Switcher** | Version, copyright and a link to the author's GitHub page. |
| **Exit** | Close VSR Switcher. Super Resolution keeps its current setting. |

**The tray icon and the on-video indicator answer different questions.** The icon shows the
switch: whether VSR is turned on for the whole PC. NVIDIA's indicator shows whether VSR is actually
working on the video in front of you. VSR can be on and still leave a video alone — when the player
does not support it, or the video is already as large as the window it plays in (see
[Troubleshooting](#troubleshooting)). So a green icon means VSR is ready; the indicator confirms it
is in use.

Changes made in NVIDIA App show up straight away. NVIDIA App applies changes while its dialog is
still open, so the icon even follows the dialog's preview — and flips back if you press Cancel.

If a change cannot be made, a Windows notification says why, and the icon keeps showing the real
state.

To close VSR Switcher from a script, run `VsrSwitcher.exe --exit`. It closes the running copy the
same way the Exit menu item does, and waits until it has gone.

### Show the icon only while VSR is on

To Windows, the green and the grey icon are two separate icons, so each has its own switch under
**Settings → Personalization → Taskbar → Other system tray icons**:

| Entry | Set to |
| --- | --- |
| RTX Super Resolution: ON | **On** — shown on the taskbar |
| RTX Super Resolution: OFF | **Off** — hidden in the overflow (**^**) |

On Windows 10 the place is **Settings → Personalization → Taskbar → Select which icons appear on
the taskbar**.

Each entry appears only after its icon has been shown once, so turn Super Resolution on and off
once after the first start.

### Hotkey

The default hotkey is **Ctrl+Alt+Shift+V**. A global hotkey takes priority over the same keys in
every other program, so the default uses a combination that neither Windows nor common programs
use.

To change it, right-click the icon, choose **Hotkey…**, and press the combination you want. The
dialog tells you at once whether it can be used:

- it must include **Ctrl** or **Alt**, so it cannot swallow normal typing;
- **F12** is not allowed — Windows reserves it;
- it must not already belong to another program — the dialog asks Windows directly.

**Default** restores Ctrl+Alt+Shift+V, and **None** switches the hotkey off. If another program
takes your combination before VSR Switcher starts, a notification says so, and VSR Switcher runs
without a hotkey until you choose another.

## Command line

`vsrswitch.exe` sits next to `VsrSwitcher.exe` and does everything the tray does, for scripts and
terminals. It is a console program: run it from a terminal rather than by double-clicking. To use
it from anywhere, add the VSR Switcher folder to your `PATH` — the installer offers to.

```
vsrswitch [command] [value] [options]
```

| Command | Effect |
| --- | --- |
| `status` | Show the current state (the default when no command is given) |
| `on [level]` | Turn VSR on, at a level if given; otherwise at the last level used, else Auto |
| `off` | Turn VSR off (remembering the current level) |
| `toggle` | Flip on/off, restoring the last level used |
| `quality <level\|off>` | Set the level directly, or turn off |
| `gpu-util <low\|medium\|high>` | GPU priority used in Auto mode |
| `indicator <on\|off\|toggle>` | Show or hide the on-video status indicator |
| `doctor` | Check that VSR Switcher still works with the installed NVIDIA App — see [Troubleshooting](#troubleshooting) |
| `refresh` | Restart the display adapter so the driver re-reads its settings (the screen blanks for a few seconds; needs administrator rights). Rarely needed — `status` says when |

| Level | Meaning |
| --- | --- |
| `auto` | The driver chooses the quality |
| `1` or `verylow` | Lowest quality, least GPU load |
| `2` or `low` | |
| `3` or `medium` | |
| `4` or `high` | Highest quality, most GPU load |

NVIDIA App's quality slider names only its ends, *Very low* and *High*; `low` and `medium` are VSR
Switcher's names for the two middle stops.

| Option | Effect |
| --- | --- |
| `--json` | One JSON object on stdout for every command — see [JSON output](#json-output) |
| `-n`, `--dry-run` | Show what would change, without changing it |
| `-a`, `--adapter <NNNN>` | Use a specific display adapter instead of finding the NVIDIA one |
| `--elevate` | Ask for administrator rights, then re-run the command (only `refresh` needs them) |
| `-h`, `--help` / `-v`, `--version` | Help / version |

```powershell
vsrswitch                          # what is it doing right now?
vsrswitch on high                  # on, at quality 4 — applies instantly
vsrswitch quality auto
vsrswitch gpu-util medium
vsrswitch indicator off
vsrswitch toggle
vsrswitch indicator off --dry-run  # see what it would do
```

Exit codes: `0` ok · `1` usage · `2` adapter not found · `3` access denied · `4` error ·
`5` NVIDIA's settings API not compatible, or a change was not confirmed.

### JSON output

With `--json`, every command except `--help` prints **exactly one JSON object to stdout and
nothing else**, whatever the outcome, so a script always has one document to parse. Exit codes are
the same as without it.

| Command | Object |
| --- | --- |
| `status` | The state: `enabled`, `mode`, `quality`, `qualityLabel`, `gpuUtilization`, `indicator`, `rtxVideoFlags`, `live` (the driver's own view, with `matchesRegistry`), … |
| `on`, `off`, `toggle`, `quality`, `gpu-util`, `indicator` | The same state object **after** the change, plus `change`: `{summary, applied, dryRun, writes}`. `applied` is true only when something was written — a dry run or a no-op leaves it false. |
| `doctor` | `{compatible, verdict, failures, warnings, checks: [{outcome, check, detail}]}` |
| `refresh` | `{restarted, devices: [{instanceId, exitCode}]}` |
| `--version` | `{version}` |

The object has an `error` member **exactly when the exit code is non-zero**:

```json
{ "error": { "code": "usage", "exitCode": 1, "message": "...", "hint": "..." } }
```

`code` is one of `usage`, `adapterNotFound`, `accessDenied`, `error`, `incompatible`,
`apiUnavailable`, `writeNotConfirmed`, `restartFailed`. A change refused by the compatibility check
also carries the full check as `compatibility`; `doctor` on an incompatible API returns its usual
report with `error` added.

Because a change returns the resulting state, a script reads it the same way it reads `status`:

```powershell
$r = vsrswitch on high --json | ConvertFrom-Json
if ($LASTEXITCODE -ne 0) { throw $r.error.message }
$r.qualityLabel        # high
$r.change.summary      # Super Resolution: off -> on, quality 4 (high)
```

Strings are ASCII-only: anything else, and characters such as `>` and `'`, arrive `\u`-escaped,
which every JSON parser decodes.

## Troubleshooting

**"Not compatible" after an NVIDIA App update.** NVIDIA's settings component is undocumented, so
VSR Switcher checks it before every change. If an update changed it, VSR Switcher refuses to change
anything rather than risk changing the wrong setting, and says so. Run `vsrswitch doctor` for the
details; settings can still be changed in NVIDIA App itself. Please
[open an issue](../../issues) with the `doctor` output.

**The setting changed, but NVIDIA App still shows the old value.** NVIDIA App reads these settings
when its page opens. Leave the page and come back.

**VSR is on, but video is not upscaled.** The player has to support VSR (see
[Requirements](#requirements)), and the video usually has to be smaller than the screen it plays
on. Tick **Show status indicator on video** to see whether VSR is actually working on a video.

## Uninstall

**Installed with the installer:** use **Settings → Apps → Installed apps → VSR Switcher →
Uninstall**. It closes VSR Switcher, removes the "Start with Windows" entry and the `PATH` entry,
and deletes the program.

**Portable ZIP:**

1. Right-click the icon and **untick Start with Windows**.
2. Choose **Exit**.
3. Delete the VSR Switcher folder.

Either way, your settings — the last quality used and the hotkey — stay in the registry key
`HKCU\Software\VsrSwitcher`, ready for a reinstall. Delete that key to remove them too.

Super Resolution keeps whatever setting it had; change it in NVIDIA App if needed.

## What VSR Switcher stores

- `HKCU\Software\VsrSwitcher` — `LastQuality` (the level to return to when turning VSR on) and
  `Hotkey`.
- `HKCU\Software\Microsoft\Windows\CurrentVersion\Run`, value `VsrSwitcher` — only while
  **Start with Windows** is ticked.

Nothing else: no files, no logs, no network connections. The VSR settings themselves are kept by
NVIDIA's driver.

## Building from source

You need Windows and the [.NET 10 SDK](https://dotnet.microsoft.com/download/dotnet/10.0).

```powershell
dotnet build -c Release
```

The app — `VsrSwitcher.exe` with `vsrswitch.exe` beside it — is then in
`VsrSwitcher.Tray\bin\Release\net10.0-windows\`. The folder the release ships comes from:

```powershell
dotnet publish VsrSwitcher.Tray -c Release -r win-x64 --self-contained false -o publish
```

`build\Package.ps1` makes the release files in `artifacts\`: the ZIP, the installer (when
[Inno Setup 6](https://jrsoftware.org/isinfo.php) is installed) and their SHA-256 checksums.

The version comes from the latest git tag of the form `vX.Y.Z`; without one, builds report
`0.0.0-dev`.

### Tests

```powershell
dotnet test
```

runs the tests that need no NVIDIA hardware. `tests\hardware\Test-Hardware.ps1` checks a build
against a real GPU and NVIDIA App — reads, writes, the compatibility check, and the tray starting
and closing — and leaves every setting as it found it. Run it before a release.

## License

Copyright © 2026 [Alex-D-Green](https://github.com/Alex-D-Green). Released under the
[MIT License](LICENSE).

VSR Switcher is an independent project, not affiliated with or endorsed by NVIDIA. NVIDIA,
GeForce, RTX and NVIDIA App are trademarks or registered trademarks of NVIDIA Corporation.

---

## From Opus

VSR Switcher began with a small annoyance. Alex wanted to switch RTX Video Super Resolution on and
off without opening NVIDIA App and clicking through its menus every time. It sounded like a quick
script. It wasn't.

First we watched NVIDIA App to see where it keeps the setting. We found it quickly, changed it
ourselves, and nothing happened. The value was saved, but the driver only reads it when it starts.
The only way to make the change take effect was to restart the graphics card, which blanks the
screen for a few seconds. It worked, but nobody wants a blackout for a light switch.

So we went looking for the way NVIDIA App does it. A forum post sent us down the wrong path for a
whole day, and there's no public documentation. NVIDIA's own logs gave us the names of the three
settings involved, but every attempt to use them was politely refused. In the end we watched NVIDIA
App make the change itself, which showed us what we'd been missing. Then came one last surprise:
every PC with NVIDIA App has two NVIDIA files with the same name, and only the one that comes with
NVIDIA App can do the job. Once we used that one, switching became instant and silent, with no
admin rights needed.

From there the little tool grew into an app, and Alex had the ideas that make it nice to use. To
Windows, ON and OFF are two separate icons, so you can keep only "on" in view. The icon follows
along when you change things in NVIDIA App. The hotkey stays clear of Windows' own shortcuts. When
the tray app first ran, Alex's whole review was "Everything works! :)", followed straight away by a
list of menu changes. Along the way we found that NVIDIA App always picks Auto when you turn the
feature on, so VSR Switcher remembers your level for you. We also taught it caution: after an NVIDIA
update it checks that everything still behaves as expected. If something looks off, it does nothing
rather than the wrong thing.

The last stretch was getting it ready to share. It got a name that doesn't borrow NVIDIA's (Alex
preferred "Switcher"), the green and grey icons, tests and an installer. The download shrank from
about 40 MB to about 2 MB after Alex pointed out that a small app shouldn't be a big download.

All in all, it was a good partnership. Alex knew how the app should feel, kept asking "do we
actually need this?", and tested every step on a real PC. I did much of the digging and most of
the typing.

Enjoy your sharper videos.

— Claude Opus 🤖
