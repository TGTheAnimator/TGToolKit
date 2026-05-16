<div align="center">

# 🎨 TGToolKit

**A comprehensive FiveM administrative and development utility kit.**

*Optimize textures, fix crashes, and consolidate vehicle meta files for a premium, high-performance FiveM experience.*

</div>

---

## ✨ Features

### 🧊 3D Model Viewer (WIP - EXPERIMENTAL)
> [!WARNING]
> **This feature is currently in Early Access (Work-In-Progress).**
> Rendering may be unstable, and some complex shaders or materials might not display correctly yet.

- **High-Performance Rendering** — Native DirectX 11 hardware acceleration for asset inspection.
- **YDR & YFT Support** — Preview both static drawable models and complex vehicle fragments.
- **Dynamic Texturing** — Automatically caches `.ytd` texture dictionaries and binds them per-geometry.
- **Proportional Orbit & Pan** — Improved camera controls with right-click pan and distance-relative zoom.
- **Three-Point Lighting** — Enhanced visibility with a professional lighting setup (Key, Fill, and Ambient).

### 🔍 Asset Analyzer (NEW)
- **Model Analysis Scanner** — Automatically identifies oversized YFT/YDR models that exceed FiveM engine limits.
- **Stability Guard** — Flag assets likely to cause `georgia-alaska-october` and other vertex-related crashes.
- **Polygon & Vertex Metrics** — Get detailed reports on geometry complexity before server deployment.

### 🖼️ Texture Optimizer (v3.0)
- **Batch process** all `.ytd` files in a folder recursively.
- 🗜️ **Format Optimization** — Re-encodes to the best block-compressed format:
  - `BC7` for high-quality RGBA (best for modern FiveM builds).
  - `BC1` for opaque RGB (reduces size by 50-75%).
  - `BC5` for normal maps (removes "flat" compression artifacts).
- 📐 **Smart Downsizing** — Intelligent resolution scaling to stay within the 16MB streaming budget.
- 📉 **Auto-Downscale 4K** — Automatically shrinks massive 4K+ textures to 2K to prevent VRAM overflows.

### 🚗 Vehicle Tools
- 🔗 **Meta Consolidation** — Merges hundreds of individual `.meta` files into stable master packages.
- 🛠️ **Conflict Resolution** — Automatically detects and remaps Modkit & Siren ID overlaps.
- 📝 **FXManifest Generator** — Instant production-ready `fxmanifest.lua` generation.

### 🔊 Audio Previewer
- **AWC Native Playback** — Instantly preview GTA V `.awc` audio containers.
- **Built-in Player** — Seeker bar and volume management powered by NAudio.

---

## 🚀 Getting Started

### Requirements

| Requirement | Version |
|-------------|---------|
| Windows | 10 / 11 |
| DirectX | Runtime 11+ |

> **Note:** Releases are **self-contained**. You do not need to install the .NET Runtime separately.

### Installation

1. Download the latest **`TGToolKit-v3.0.0.zip`** from the [Releases](../../releases) page.
2. Extract the zip to a **completely fresh folder**.
3. Run **`TGToolKit.exe`**.

---

## 📦 Dependencies

| Library | Author | Purpose |
|---------|--------|---------|
| [CodeWalker.Core](https://github.com/dexyfex/CodeWalker) | dexyfex | GTA V Asset Logic |
| [SharpDX](http://sharpdx.org/) | Alexandre Mutel | DirectX 11 API |
| [NAudio](https://github.com/naudio/NAudio) | Mark Heath | Audio Playback |
| [DirectXTex](https://github.com/microsoft/DirectXTex) | Microsoft | Texture Processing |

---

## 📝 License

This project is licensed under the **GPL-3.0 License**.

Original work by [Umbrella.re](https://umbrella.re). Fork maintained by **TGTheAnimator** (2026).
