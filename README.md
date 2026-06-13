# MacBook Touchpad Visualizer

[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](LICENSE)

A **real‑time Precision Touchpad raw input visualizer** for **MacBooks running Windows via BootCamp**.

Reads the touchpad's HID raw data directly through the Windows Raw Input API (WM_INPUT) — bypassing the mouse/gesture system — and visualizes every touch point with position, contact size, and pressure.

> 🍎 **Works on:** MacBook Air/Pro (2015–2019, Intel) with BootCamp Windows 10/11

---

## ✨ Features

- 🖐 **Real‑time touch tracking** — position, contact area, pressure
- 👆 **Multi‑finger support** — up to 2 simultaneous touches
- 🎨 **Live visualization** — colored circles for each finger, size scales with contact area
- 📊 **Info panel** — raw HID coordinates, mapped window coordinates, contact size, pressure
- ❄️ **Freeze frame** (F12) — pause the display for detailed analysis
- 📋 **Copy report** — one‑click export of all diagnostic data
- 🚫 **Disable touchpad** — temporarily disable the touchpad (requires admin)

---

## 📸 Screenshot

*(screenshot pending)*

---

## 🚀 Quick Start

### Prerequisites
- Windows 10/11 on a MacBook (BootCamp)
- [.NET 8.0 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) or later

### Build & Run
```bash
git clone https://github.com/Pro-Qin/MacBook-Touchpad-Visualizer.git
cd MacBook-Touchpad-Visualizer
dotnet build
dotnet run
```

Or open `TouchpadVisualizer.csproj` in Visual Studio / Rider and press F5.

---

## ⌨️ Controls

| Key / Button | Action |
|-------------|--------|
| **Esc** | Exit the application |
| **F12** | Freeze / unfreeze the display |
| **◀ / ▶** | Collapse / expand sidebar |
| **📋 Copy** | Copy full diagnostic report to clipboard |

---

## 🔧 How It Works

1. The app registers for **Raw Input** from HID digitizer devices (`UsagePage=0x0D, Usage=0x05`)
2. The touchpad sends raw HID reports as `WM_INPUT` messages
3. Each **18-byte** HID report is parsed:
   - Bytes 6‑9  → Finger 1 X, Y (16‑bit LE)
   - Bytes 10‑13 → Finger 1 width, height, pressure, flags
   - Bytes 14‑17 → Finger 2 X, Y (16‑bit LE)
4. Raw touchpad coordinates are mapped to window coordinates
5. The WPF canvas renders colored circles at each touch point

### Apple MacBook HID Report Format
```
Offset  Size  Field
──────────────────────────
0       1     Report ID (0x05)
1       1     Status flags (bit0 = tip switch)
2‑5     4     Reserved / padding
6‑7     2     Finger 1 X (LE)
8‑9     2     Finger 1 Y (LE)
10‑13   4     Finger 1 extended (width, height, pressure, flags)
14‑15   2     Finger 2 X (LE)
16‑17   2     Finger 2 Y (LE)
```

---

## 📄 License

This project is licensed under the **MIT License** — see the [LICENSE](LICENSE) file for details.

---

## 🤝 Contributing

Issues, feature requests, and pull requests are welcome!
