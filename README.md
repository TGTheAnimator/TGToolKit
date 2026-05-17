<div align="center">
 
 # 🎨 TGToolKit — v3.5.0
 
 **The Ultimate FiveM Administrative and Automated DevOps Utility Suite.**
 
 *Built on .NET 8 architecture to optimize textures, auto-fix fatal engine crashes, consolidate vehicle assets, and operate as a high-speed DevOps engineer for your live local or remote (SFTP) FiveM server.*
 
 </div>
 
 ---
 
 ## 🏗️ Enterprise Performance
 TGToolKit is engineered for massive server environments with highly optimized backend processing:
 - **Asynchronous I/O & Bounded Concurrency:** Safely processes gigabytes of server dumps without Large Object Heap (LOH) memory spikes or freezing your UI.
 - **Thread-Safe Pipeline:** Utilizes `Channel<T>` for background logging and `IProgress<T>` for UI synchronization, ensuring perfect stability during heavy batch operations.
 - **Provider-Abstracted File Transfer:** Powered by a highly specialized file-system wrapper, running identical blazing-fast operations whether you are targeting a local workspace or an SFTP-connected (Pterodactyl/RocketNode) container.
 - **Global Safety Net:** Structured `crash.log` captures every unhandled exception with full stack trace — no more cryptic Windows crash dialogs.
 
 ---
 
 ## ✨ Features
 
 ### 🌐 Global Qbox Transpiler Engine *(New in v3.5.0)*
 A unified Mass-Scan Core Engine that automates server-wide modernization in a single pass.
 - **Recipes:** Instantly parses and converts outdated client/server Lua files, replacing legacy notification functions (`QBCore.Functions.Notify`) and outdated DrawText exports with modern, optimized `ox_lib` equivalents (`lib.notify`, `lib.showTextUI`).
 - **Centralized Overrides:** Apply server-wide sweeps to immediately override Webhooks (`Config.Webhook`), Locale languages (`Config.Locale`), and Currencies (Currency codes and symbols like `USD`/`$`) across all resources.
 - **Filter System:** Skips binary stream directories (`/stream/`), web assets (`/ui/`, `/html/`), and model data to transpile hundreds of resources in seconds.
 - **Backup Protection:** Automatically creates a `.tg_backup` for every single file modified, ensuring one-click rollbacks if needed.
 
 ### 🗃️ Asset & Item Importer *(New in v3.5.0)*
 Eliminates the manual drag-and-drop fatigue of adding custom items.
 - **Automatic Harvesting:** Scans newly downloaded resources to harvest item code blocks and associated image files automatically.
 - **Surgical Injection:** Perfectly injects items into `ox_inventory/data/items.lua` and copies matching `.png` images into `ox_inventory/web/images/`.
 - **Advanced Metadata Setup:** Smart-detects items requiring custom metadata configurations (like premium drug or cooking scripts) so they function perfectly when spawned.
 - **Dynamic Hot-Swapping:** Allows you to change spawn codes, item names, or create easy variations (e.g. weed strains, license types) during import.
 
 ### 🛡️ Server Linter & Stateful Ignore Engine *(New in v3.5.0)*
 Comprehensive DevOps diagnostics with intelligent persistence to wipe out alert fatigue.
 - **Stateful Ignore Persistence:** Acknowledged warnings are saved directly to a root `.tgtoolkit_ignore.json` file. If you work in a development team, the ignored issues stay hidden for everyone.
 - **One-Click Ignore All:** Easily hide hundreds of harmless, repetitive warnings (like legacy files) with a single click.
 - **Remote SFTP Scanning:** Inspect manifests and resource trees over SFTP without downloading gigabytes of `.ytd` assets.
 - **Dependency Engine:** Automatically scans resource files to see if `ox_lib` is utilized, injecting the appropriate `shared_script '@ox_lib/init.lua'` inside your `fxmanifest.lua` on the fly.
 
 ### ⚔️ Surgical Conflict Resolver
 Stops you from booting a broken server and prevents texture-based crashes.
 - **14 Conflict Categories:** Detects mutually exclusive scripts across MDT, Phone, Inventory, Ambulance, Police, Targeting, Mechanic, Garage, Clothing, HUD, Voice, Fuel, and Notification systems.
 - **Surgical Stream conflict resolution:** Detects asset name clashes inside `/stream/` folders across different resources and allows single-click surgical deletion of conflicting assets to keep your server directory neat and error-free.
 - **Quarantine, Not Delete:** Deactivates framework defaults and duplicate resources into `.disabled_*` files, keeping your files safe in case you need to revert.
 
 ### 🔧 server.cfg Validator & Fixer
 - **7-Tier Load Order:** Enforces the canonical dependency chain: `ox_lib/oxmysql` → `qbx_core` → inventories/targets → voice → utility libs → phone → gameplay scripts.
 - **Auto-Append:** Any installed resource folder without a corresponding `ensure` line gets one added automatically.
 - **Order Repair:** Detects and corrects out-of-order entries with a clean, TGToolKit-managed block.
 
 ### ↩️ Emergency Rollback
 - **One-Click Revert:** The "Restore Backups" button finds every `.tg_backup` file under the server root and reverts each original file to its pre-modification state.
 - **Works Over SFTP:** Streams backup content directly from the remote container — no local copies needed.
 
 ### 💰 Centralized Economy Balancer
 - **Local Delta-Sync Workspace:** Uses WinSCP bulk stream to clone remote folders into SSD-speed temp directories, and ONLY uploads the modified scripts upon saving.
 - **Lua Brace Surgeon:** Safely parses through thousands of lines of `ox_inventory/items.lua` or `lunar_shops.lua`, replacing targeted prices without syntax corruption.
 - **Unified Master List:** A massive DataGrid sorts buy prices, sell prices, and weights side-by-side. 
 
 ### 🗄️ SQL Migration Matrix
 - **Asynchronous Schema Scanning:** Logs into your live MySQL/MariaDB server via MySqlConnector and compares your active schema against every single `.sql` file sitting inside your `[resources]` directory.
 - **One-Click Execute:** Maps out exactly which tables and columns are missing, allowing you to create them automatically to prevent silent script failures.
 
 ### ✂️ YTD Splitter (The VRAM Savior)
 - **Mathematical Bin-Packing:** Intelligently reads massive 40MB+ `.ytd` files and perfectly splits them into safe ≤14.5MB chunks using exact GPU block compression (BC1–BC7) calculations.
 - **Auto-TXD Relationships:** Automatically generates `split_txd_relationships.meta` and `fxmanifest_snippet.lua`.
 
 ### 🚨 Visual Siren Builder
 - **32-Bit Sequence Grid:** Click and draw emergency light flash patterns on a visual timeline.
 - **Instant XML Generation:** Calculates 32-bit bitmask integers and generates `carcols.meta` XML.
 
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
 
 > **Note:** Releases are **self-contained single-file executables**. No .NET Runtime installation required.
 
 ### Installation
 
 1. Download the latest **`TGToolKit-v3.5.0.zip`** from the [Releases](../../releases) page.
 2. Extract the zip to a **completely fresh folder**.
 3. Run **`TGToolKit.exe`**.
 
 ---
 
 ## 📦 Dependencies
 
 | Library | Author | Purpose |
 |---------|--------|---------|
 | [CodeWalker.Core](https://github.com/dexyfex/CodeWalker) | dexyfex | GTA V Asset Logic |
 | [NAudio](https://github.com/naudio/NAudio) | Mark Heath | Audio Playback |
 | [DirectXTex](https://github.com/microsoft/DirectXTex) | Microsoft | Texture Processing |
 | [SSH.NET](https://github.com/sshnet/SSH.NET) | renci | Remote SFTP DevOps Engine |
 
 ---
 
 ## 📝 License
 
 This project is licensed under the **GPL-3.0 License**.
 
Created and maintained by **TGTheAnimator** (2026).