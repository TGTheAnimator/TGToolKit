<div align="center">

# 🎨 TGToolKit

**The Ultimate FiveM Administrative and Development Utility Suite.**

*Built on enterprise-grade .NET 8 architecture to optimize textures, fix fatal engine crashes, consolidate resources, and streamline server development.*

</div>

---

## 🏗️ Enterprise Performance
TGToolKit isn't just a collection of scripts; it is engineered for massive server environments:
- **Asynchronous I/O & Bounded Concurrency:** Safely processes gigabytes of server dumps without Large Object Heap (LOH) memory spikes or freezing your UI.
- **Thread-Safe Pipeline:** Utilizes `Channel<T>` for background logging and `IProgress<T>` for UI synchronization, ensuring perfect stability during heavy batch operations.
- **Hardware Acceleration:** Native DirectX 11 pipeline for flawless, zero-lag asset rendering.

---

## ✨ Features

### 🧊 3D CAD Viewport
A professional, standalone 3D model viewer—no need to boot up heavy map editors just to inspect a model.
- **Flawless DirectX 11 Rendering:** Dynamic layout factories and uber-shader permutations automatically handle missing normals, tangents, and untextured props without crashing.
- **CAD-Style Inspection:** Features an ArcBall camera (smooth orbit, pan, zoom), toggleable wireframe rasterizer states, and a geometric ground grid.
- **Dynamic Texturing:** Automatically caches `.ytd` texture dictionaries and binds them per-geometry accurately using ShaderMappings.

### ✂️ YTD Splitter (The VRAM Savior)
- **Mathematical Bin-Packing:** Intelligently reads massive 40MB+ `.ytd` files (common in custom MLOs) and perfectly splits them into safe <=14.5MB chunks.
- **Auto-TXD Relationships:** Automatically generates the `split_txd_relationships.meta` and `fxmanifest_snippet.lua`. You can split a massive map dictionary and stream it instantly without losing a single texture in-game.

### 🛡️ Server Linter & Dependency Resolver
- **Remote SFTP Scanning:** Connect directly to your Pterodactyl/RocketNode server to scan manifests instantly over SFTP without downloading gigabytes of `.ytd` files.
- **Conflict & Deprecation Traps:** Scans your entire `[resources]` folder to flag deprecated legacy scripts (like `__resource.lua` or `mysql-async`), missing `@ox_lib` dependencies, and overlapping framework systems.

### 🚨 Visual Siren Builder
- **32-Bit Sequence Grid:** Stop typing raw integer sequences. Use a sleek, modern visual timeline grid to click and draw your emergency light flash patterns.
- **Instant XML Generation:** Automatically calculates the 32-bit bitmask integers and generates perfect `carcols.meta` XML code ready to copy and paste.

### 🖼️ Texture Optimizer (v3.0)
- **The "Rule of 4" Accuracy:** Strictly enforces multiple-of-4 pixel dimensions to completely eliminate the shimmering and diagonal skewing artifacts caused by standard resizers.
- **Parallel Processing:** Blasts through recursive folders using asynchronous CPU swarming to run `texconv` batches in a fraction of the time.
- **Smart Compression:** Auto-encodes to the best format (`BC7` for RGBA, `BC1` for opaque, `BC5` for normals) while shrinking massive 4K textures down to safe engine limits.

### 🔍 Asset Analyzer
- **Model Analysis Scanner:** Automatically identifies oversized YFT/YDR models that exceed the hardcoded 64,000 vertex limit.
- **Crash Prevention:** Specifically targets assets guaranteed to cause the fatal `georgia-alaska-october` memory crash.

### 🚗 Vehicle Tools
- **Meta Consolidation:** Merges hundreds of individual `.meta` files into stable master packages.
- **Conflict Resolution:** Automatically detects and remaps Modkit & Siren ID overlaps so your police lights and tuning parts never break when merging packs.
- **FXManifest Generator:** Instant production-ready `fxmanifest.lua` generation.

### 🔊 Audio Previewer
- **AWC Native Playback:** Instantly preview GTA V `.awc` audio containers.
- **Built-in Player:** Seeker bar and volume management powered by NAudio.

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
| [SSH.NET](https://github.com/sshnet/SSH.NET) | renci | Remote Server Linter |

---

## 📝 License

This project is licensed under the **GPL-3.0 License**.

Based on ToolKitV by [Umbrella.re](https://umbrella.re). Completely restructured and maintained by **TGTheAnimator** (2026).