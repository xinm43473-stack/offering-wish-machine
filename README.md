<p align="center">
  <img src="./images/ico.png" alt="Icon" width="50%">
</p>

# Offering Wish Machine

**English** | [简体中文](./README.zh-CN.md)

A small Windows desktop widget: pick a deity, hit "Offer", and merit text floats up the screen.

## Features

- **Deities**: each one owns a folder with its image, a title, and several texts popped at random when offering
- **Offerings**: three offering slots fed by images in `Assets/Offerings`; right-click to rename
- **Offering / Auto offering**: click manually, or turn on "Auto offering" to loop at the current speed; 1–50×
<p align="center">
  <img src="./images/2d8ccdfcf908ea3186b1ca28c0f024ad.png" alt="Screenshot" width="50%">
</p>
- **Floating text**: at high speeds one offering may pop several texts at once, composited into a single layered window
- **Title & wish**: loaded per deity and written back to that deity's folder as you type
- **Bilingual UI**: follows the system language (English for English systems, Chinese otherwise), switchable from the gear menu
- **Starter assets**: the current assets are embedded in the exe and laid out into `Assets/` on first run; deleted assets do not come back by themselves
- **Log**: `Assets/logs/incense.log`, rotated to `incense.1.log` past 512 KB
- **Background**: right-click the background around the deity to pick a file in `Assets/background` as the background, or add another one
<p align="center">
  <img src="./images/bf61e7b6-3dca-4baa-ae22-c1c39a620a15.png" alt="Screenshot" width="50%">
</p>
- **Drag & drop**: drop a file, folder or shortcut onto the avatar in the middle to create a deity, see "Drag to create a deity" below

## Drag to create a deity

Drop a file, a folder or a shortcut onto the avatar in the middle and it becomes a deity that looks exactly like it does in Explorer; the name defaults to the file name:

- **Image files**: copied as-is, no preset icon
- **Programs / shortcuts**: use the icon Explorer shows; a shortcut uses its own icon first (an Edge web-app shortcut shows the site icon instead of the Edge icon)
- **Folders**: use Explorer's thumbnail; when the system has not cached one yet, the first 4 images inside are tiled into one (1 fills / 2 side by side / 3 left big + two right / 4 in a 2×2 grid)
- **Default floating text**: always "This time for sure" (only for drag & drop; the "New deity" button keeps the regular defaults)
- **Right-click the avatar**: "Edit floating texts (open folder)" opens `Assets/Deities/<name>/` directly; changes show up within 300 ms
- **No duplicates**: an existing deity with the same name is only selected, never overwritten and never renamed to "name (2)"

<p align="center">
  <img src="./images/drag-drop-folder.png" alt="Dropping the deepsleep folder onto the avatar to create a deity" width="49%">
  <img src="./images/drag-drop-shortcut.png" alt="Dropping a Visual Studio Code shortcut onto the avatar to create a deity" width="49%">
</p>
<p align="center">
  <img src="./images/drag-drop-popping-text.png" alt="The default floating text showing after an offering" width="70%">
</p>

## Run

Double-click `上贡许愿机.exe`. On first launch it creates `Assets/` next to the exe and lays out the starter assets.

- Gear button: collapse the side panels, transparent list background, UI language
- The current speed is shown next to "Auto offering"; `F10` toggles auto offering by default and can be changed in the gear menu

<p align="center">
  <img src="./images/preview-offering.jpg" alt="Three offering slots, the offering speed slider, and the Auto offering and Offer buttons" width="90%">
</p>

## Build

Requires .NET Framework 4.x (included with Windows).

```powershell
.\build.ps1                 # builds 上贡许愿机.exe with the starter assets embedded
.\build.ps1 -NoAssets       # no embedded assets (debug)
.\build.ps1 -Output _test.exe
```

## Asset layout

```
Assets/
  Deities/<name>/          # deity: <name>.png, <name>.meta (title), <name>.wish, 1.txt/2.txt… (popping texts)
  Offerings/               # offering images, file name = offering name
  background/              # background images, first file wins
  logs/incense.log         # offering log
  ui.ini                   # UI settings (speed, hotkey, language)
```

An empty txt in `Deities/<name>/` means "no floating text for this deity"; no txt at all falls back to the default "Merit +1".

## Verify

```powershell
.\verify.ps1
```

Runs the UI regression, the feature regression (speed, floating text, hotkeys, settings persistence, layout) and the language regression in turn; every stage prints `RESULT_OK` on success.

## Tech

C# / .NET Framework 4.x / WinForms, compiled directly with `csc`; the whole UI lives in a single `Form1.cs`.

## Disclaimer

Images, texts and other assets in this project come from the internet or the author's own use, and are provided for personal learning and exchange only. All rights belong to their original owners.

If any material infringes your rights, please reach me via [GitHub Issues](https://github.com/xinm43473-stack/offering-wish-machine/issues). It will be removed promptly after verification.

This project is provided "as is", without warranty of any kind, and the user bears any consequences of using this software.