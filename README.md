<div align="center">

# 🎨 TGToolKit — v3.4.1

**The Ultimate FiveM Administrative and Automated DevOps Utility Suite.**

*Built on enterprise-grade .NET 8 architecture to optimize textures, fix fatal engine crashes, consolidate resources, and operate as an automated DevOps engineer for your live FiveM server.*

</div>

---

## 🏗️ Enterprise Performance
TGToolKit isn't just a collection of scripts; it is engineered for massive server environments:
- **Asynchronous I/O & Bounded Concurrency:** Safely processes gigabytes of server dumps without Large Object Heap (LOH) memory spikes or freezing your UI.
- **Thread-Safe Pipeline:** Utilizes `Channel<T>` for background logging and `IProgress<T>` for UI synchronization, ensuring perfect stability during heavy batch operations.
- **Hardware Acceleration:** Native DirectX 11 pipeline for flawless, zero-lag asset rendering.
- **Global Safety Net:** Structured `crash.log` captures every unhandled exception with full stack trace — no more cryptic Windows crash dialogs.

---

## ✨ Features

### 🔌 Fuzzy Config Auto-Wirer *(New in v3.4)*
The crown jewel. TGToolKit acts as an automated DevOps engineer for your live server.
- **Ecosystem Detection:** Automatically maps your server's dependency graph — detects Qbox (`qbx_core`), QBCore, ESX, and prioritizes custom/premium ecosystems.
- **Universal Config Re-routing:** Uses Regex injection to rewrite `Config.Framework`, `Config.Inventory`, `Config.Phone`, `Config.UI`, and more across *every* script simultaneously.
- **Qbox Native Code Paths:** Detects `qbx_core` and activates the `'qbx'` identifier in modern scripts (JG, Wasabi, XDope) to unlock their native Qbox optimizations.
- **Works Local & Remote:** Provider-abstracted — runs identically over your local `[resources]` folder or directly inside your **RocketNode/Pterodactyl** container over SFTP.
- **Safety Net:** Every modified file receives a `.tg_backup` before changes are applied.

### ⚔️ Surgical Conflict Resolver *(New in v3.4)*
Stops you from booting a broken server.
- **14 Conflict Categories:** Automatically detects mutually exclusive scripts across MDT, Phone, Inventory, Ambulance, Police, Targeting, Mechanic, Garage, Clothing, HUD, Voice, Fuel, and Notification systems.
- **Framework Default Detection:** Identifies 20+ QBCore/Qbox built-in scripts (`qb-garages`, `qbx_medical`, `qb-clothing`, etc.) and flags them as framework bloat when a premium replacement is installed.
- **Smart Pre-Selection:** The conflict modal opens with the recommended premium winner already checked — ecosystem-aware (JPR > XDope > framework default).
- **Quarantine, Not Delete:** Losing scripts are renamed to `.disabled_*` (FiveM ignores dot-prefixed directories) and commented out in `server.cfg`. Nothing is permanently deleted.
- **Dual Badges:** `✓ RECOMMENDED` (green) and `FRAMEWORK DEFAULT` (amber) badges on each script choice.

### 🔧 server.cfg Validator & Fixer *(New in v3.4)*
- **7-Tier Load Order:** Enforces the canonical dependency chain: `ox_lib/oxmysql` → `qbx_core` → inventories/targets → voice → utility libs → phone → gameplay scripts.
- **Auto-Append:** Any installed resource folder without a corresponding `ensure` line gets one added automatically.
- **Order Repair:** Detects and corrects out-of-order entries with a clean, TGToolKit-managed block.

### ↩️ Emergency Rollback *(New in v3.4)*
- **One-Click Revert:** The "Restore Backups" button finds every `.tg_backup` file under the server root and reverts each original file to its pre-modification state.
- **Works Over SFTP:** Streams backup content directly from the remote container — no local copies needed.

### 🛡️ Server Linter & Dependency Resolver
- **Remote SFTP Scanning:** Connect directly to your Pterodactyl/RocketNode server to scan manifests instantly over SFTP without downloading gigabytes of `.ytd` files.
- **95+ Known Integrations:** Expert-level diagnostic advice for JPR, XDope, Lation, JG, Rahe, KQ, Wasabi, and standard FiveM environment resources.
- **Conflict & Deprecation Traps:** Flags deprecated legacy scripts, missing `@ox_lib` dependencies, and overlapping framework systems.

### ✂️ YTD Splitter (The VRAM Savior)
- **Mathematical Bin-Packing:** Intelligently reads massive 40MB+ `.ytd` files and perfectly splits them into safe ≤14.5MB chunks using exact GPU block compression (BC1–BC7) calculations.
- **Auto-TXD Relationships:** Automatically generates `split_txd_relationships.meta` and `fxmanifest_snippet.lua`.

### 🚨 Visual Siren Builder
- **32-Bit Sequence Grid:** Click and draw emergency light flash patterns on a visual timeline.
- **Instant XML Generation:** Calculates 32-bit bitmask integers and generates `carcols.meta` XML.

### 🧊 3D CAD Viewport
- **DirectX 11 Rendering:** Dynamic layout factories and uber-shader permutations handle missing normals, tangents, and untextured props.
- **CAD-Style Inspection:** ArcBall camera (orbit, pan, zoom), wireframe toggle, geometric ground grid.

### 🖼️ Texture Optimizer
- **Rule of 4 Accuracy:** Strictly enforces multiple-of-4 pixel dimensions to eliminate shimmering artifacts.
- **Smart Compression:** Auto-encodes to `BC7` (RGBA), `BC1` (opaque), `BC5` (normals).

### 🔍 Asset Analyzer
- **Model Analysis Scanner:** Identifies oversized YFT/YDR models exceeding the 64,000 vertex hard limit.
- **Crash Prevention:** Targets assets causing the fatal `georgia-alaska-october` memory crash.

### 🚗 Vehicle Tools
- **Meta Consolidation:** Merges hundreds of individual `.meta` files into stable master packages.
- **Conflict Resolution:** Detects and remaps Modkit & Siren ID overlaps.

### 🔊 Audio Previewer
- **AWC Native Playback:** Preview GTA V `.awc` audio containers with a built-in player.

---

## 🚀 Getting Started

### Requirements

| Requirement | Version |
|-------------|---------|
| Windows | 10 / 11 (x64) |
| DirectX | Runtime 11+ |

> **Note:** Releases are **self-contained single-file executables**. No .NET Runtime installation required.

### Installation

1. Download the latest **`TGToolKit-v3.4.1.zip`** from the [Releases](../../releases) page.
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
| [SSH.NET](https://github.com/sshnet/SSH.NET) | renci | Remote SFTP DevOps Engine |

---

## 📝 License

This project is licensed under the **GPL-3.0 License**.

Based on ToolKitV by [Umbrella.re](https://umbrella.re). Completely restructured and maintained by **TGTheAnimator** (2026).