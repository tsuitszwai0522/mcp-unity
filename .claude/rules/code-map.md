# Code Map 導航規則

## 目的
`doc/code-map/` 目錄下的 Code Map 是自動生成的 codebase 索引，用於快速定位關鍵 class 與入口檔案，減少搜索成本。

## 讀取流程（必須遵守）

1. **先讀 INDEX.md**（輕量，<50 行）→ 定位目標所在模組
2. **只讀對應的模組檔案**（如 `unity-client.md`）→ 找到具體 class 與檔案路徑
3. **禁止一次讀取所有模組檔案** — 每個模組檔案可能有 100+ 行，全部讀取會浪費 context

## 使用時機

- 定位功能/Bug 時，先查 Code Map 再用 Grep/Glob 搜索
- 如果 Code Map 中找到相關 class，直接跳到該檔案，不再搜索
- 如果 Code Map 中找不到，再用傳統搜索方式

## 增量補充（工作中順手更新）

### Tier 2 — 模組檔案的 Description 欄
- 發現 Description 欄為空，且你在本次工作中已理解該 class 的職責 → 補上簡短描述（10 字以內）
- 不主動全量掃描，只在工作中順手更新

### Tier 1 — INDEX.md 的 System Entry Points
- 發現新的系統級入口（跨多個 class 的協調者、重要的 Manager）→ 追加到 `<!-- MANUAL_SECTION_START -->` 和 `<!-- MANUAL_SECTION_END -->` 之間
- Script 重新生成時會保留此區段，不會被覆蓋

## 禁止事項
- ❌ 在未查閱 Code Map 的情況下，直接用 Grep 大範圍搜索整個專案
- ❌ 一次讀取所有模組檔案
- ❌ 修改 Code Map 中 auto-generated 的結構欄位（Class, Base, File）— 這些由 script 維護
