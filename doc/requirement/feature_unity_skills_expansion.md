# Feature Design: Unity Skills 擴充

> **狀態**：Phase 1-2 已完成，Phase 3 待實作
> **建立日期**：2026-02-15
> **相關模組**：`shared-ai-sop~/skills/`

## 1. 需求描述

### 背景

`shared-ai-sop~` submodule 目前有 2 個 Unity 專用 skill：

| Skill | 覆蓋範圍 |
|-------|---------|
| `unity-mcp-workflow` | MCP 工具通用規範（GameObject、UGUI、Material、Prefab、工具陷阱） |
| `unity-ui-builder` | Figma → Unity UGUI 轉換（座標映射、Sprite 匯入、建構計劃） |

經分析，以下領域的 MCP 工具能力完整但缺乏 skill 引導：

- **測試與除錯**：`run_tests`、`get_console_logs`、`recompile_scripts` 無任何 skill 覆蓋
- **3D 場景建構**：Transform、Material、Scene 管理工具齊全，但現有 skill 聚焦 2D UI
- **專案配置**：`add_package`、`execute_menu_item`、`create_scriptable_object` 無 skill 覆蓋

### 目標

新增 3 個 Unity skill，分 3 個 Phase 實作，完善 MCP Unity 工具鏈的 AI 引導覆蓋。

## 2. Phase 總覽

| Phase | 內容 | 優先級 | 狀態 |
|-------|------|--------|------|
| Phase 1 | [`unity-test-debug`](#phase-1-unity-test-debug)（新 skill） | P0 | ✅ 已完成 |
| Phase 2 | [擴充 `unity-mcp-workflow`](#phase-2-擴充-unity-mcp-workflow)（Prefab Variant、Material 流程、Shader 引導、Scene 管理、Transform） | P1 | ✅ 已完成 |
| Phase 3 | [擴充 `unity-mcp-workflow`](#phase-3-擴充-unity-mcp-workflow-續)（Package 管理、Menu Item、ScriptableObject） | P2 | 討論完成 |

### Skill 引用關係（方案 B：分層整合）

```
unity-mcp-workflow (共用基底，Phase 1 擴充)
├── [新增] 修改後驗證規則：改 C# → recompile_scripts → 確認編譯通過
├── [新增] EditMode 優先原則（簡述 + 指向 unity-test-debug 取得完整 checklist）
├── unity-ui-builder       (引用共用規則 + Figma/UI 專用，自動受益於驗證規則)
├── (Phase 2 擴充：Prefab Variant、Material 流程、Scene 管理)
└── unity-test-debug       (引用共用規則 + 完整測試/除錯迭代循環)
```

### 每個 Skill 的產出物

- `skills/{name}/SKILL.md` — 完整版（Antigravity / Codex 用）
- `skills/{name}/claude-rule.md` — 精簡版（Claude Code 用）

---

## Phase 1: `unity-test-debug`

### 定位

Unity 測試執行與除錯的迭代循環引導。覆蓋「寫測試 → 編譯 → 執行 → 讀 log → 定位問題 → 修正」的完整流程。

### 觸發條件

- 「跑測試」「執行 EditMode 測試」
- 「為什麼編譯失敗」「編譯錯誤」
- 「看 Console 錯誤」「有什麼 warning」
- 「除錯這個問題」「為什麼 runtime 報錯」
- 「幫這個 class 寫測試」（Unity 專案內）

### 涵蓋的 MCP 工具與資源

| 工具/資源 | 用途 |
|-----------|------|
| `recompile_scripts` | 觸發編譯 + 取得編譯結果 |
| `run_tests` | 執行 EditMode / PlayMode 測試 |
| `get_console_logs` | 讀取 error / warning / info log |
| `send_console_log` | 發送標記訊息（debug 輔助） |
| `unity://tests/EditMode` | 列出 EditMode 測試清單 |
| `unity://tests/PlayMode` | 列出 PlayMode 測試清單 |
| `unity://logs/*` | 快速瀏覽 Console log |

### 核心原則：EditMode 優先

> **背景**：實務觀察發現 AI agent 能正確撰寫並編譯 EditMode 測試，但 PlayMode 測試經常連編譯都無法通過。根本原因是 PlayMode 的 assembly 編譯環境與 EditMode 截然不同，AI 容易混淆。

**EditMode vs PlayMode 編譯環境差異：**

| | EditMode | PlayMode |
|--|----------|----------|
| `.asmdef` 的 `includePlatforms` | `["Editor"]` | `[]`（空陣列） |
| `UnityEditor.*` API | 可用 | **不可用（compile error）** |
| 可引用的 assembly | Editor + Runtime | **僅 Runtime** |
| 測試 attribute | `[Test]` | `[Test]` + `[UnityTest]` (return `IEnumerator`) |

**AI agent 常見 PlayMode 編譯失敗原因：**

1. 在 PlayMode 測試中 `using UnityEditor` — player 環境無此命名空間
2. `.asmdef` 引用了 `*.Editor` assembly — PlayMode 無法解析 Editor-only 引用
3. 混用 `async/await` 與 `[UnityTest]` 的 `IEnumerator` coroutine 模式
4. 目標 runtime code 在預設 `Assembly-CSharp`（無 `.asmdef`），PlayMode test `.asmdef` 無法明確引用

**決策：Skill 採用 EditMode 優先策略。**

```
需要測試
    │
    ▼
能否用 EditMode 測試？──→ 是 ──→ EditMode（預設路線）
    │
    ▼ 否
真的需要 MonoBehaviour 生命週期 / Coroutine / Physics / 場景載入？
    │
    ▼ 是
PlayMode（例外路線，需通過 Pre-flight Checklist）
```

### 架構建議：讓更多邏輯可 EditMode 測試

Skill 應引導 AI 建議將可測試邏輯從 MonoBehaviour 分離：

```csharp
// ✅ 純邏輯類 — EditMode 可測試
public class DamageCalculator {
    public int Calculate(int attack, int defense) => Mathf.Max(1, attack - defense);
}

// ✅ MonoBehaviour 做薄 delegate — 不需要對它寫測試
public class Enemy : MonoBehaviour {
    private DamageCalculator _calc = new();
    public void TakeDamage(int attack) => hp -= _calc.Calculate(attack, defense);
}
```

### PlayMode 適用場景（僅限以下情況）

| 場景 | 為什麼必須 PlayMode |
|------|-------------------|
| MonoBehaviour 生命週期（`Start`, `Update`, `OnDestroy`） | 需要 Play Mode 才會觸發回呼 |
| Coroutine（`yield return`） | 需要 MonoBehaviour 驅動 coroutine |
| Physics（碰撞偵測、Raycast） | 需要 Physics engine update loop |
| 實際場景載入 + UI 互動測試 | 需要完整 runtime 環境 |

### PlayMode Pre-flight Checklist

若判斷確實需要 PlayMode 測試，AI **必須依序驗證**以下項目才能撰寫測試代碼：

- [ ] **確認 PlayMode test `.asmdef` 存在**：`includePlatforms` 必須為 `[]`（空陣列），若不存在則先建立
- [ ] **確認 `.asmdef` 僅引用 runtime assembly**：不可引用任何 `*.Editor` assembly
- [ ] **確認 `precompiledReferences`**：需含 `nunit.framework.dll`
- [ ] **確認 `defineConstraints`**：需含 `UNITY_INCLUDE_TESTS`
- [ ] **確認目標 runtime code 的 `.asmdef` 名稱**：查詢專案中的 `.asmdef` 確認可引用
- [ ] **代碼中無 `using UnityEditor`**：PlayMode 在 player context 編譯，Editor API 不可用
- [ ] **`[UnityTest]` return type 為 `IEnumerator`**：不可用 `void` 或 `Task`

若任一項不通過，應暫停並向使用者說明，而非嘗試自行修正（避免連鎖錯誤）。

### 核心流程

```
修改 C# 代碼
    │
    ▼
recompile_scripts ──→ 有編譯錯誤？──→ 讀取錯誤訊息 → 修正 → 重新編譯
    │
    ▼ (編譯成功)
run_tests (預設 EditMode；僅例外用 PlayMode)
    │
    ▼
有失敗測試？──→ 分析失敗原因 → 修正 → 回到 recompile
    │
    ▼ (全部通過)
get_console_logs ──→ 有 warning/error？──→ 分析 → 修正
    │
    ▼
完成 ✓
```

### 關鍵規則（草案）

1. **編譯優先**：修改 C# 後必須先 `recompile_scripts`，確認無編譯錯誤才能執行測試。
2. **EditMode 優先原則**：預設使用 EditMode 測試。僅在上述 4 種場景才考慮 PlayMode，且必須通過 Pre-flight Checklist。
3. **架構引導**：建議使用者將可測試邏輯與 MonoBehaviour 分離，讓 EditMode 測試覆蓋更多場景。
4. **測試篩選**：使用 `testFilter` 縮小範圍，避免跑全部測試浪費時間。
5. **Log 閱讀策略**：
   - 預設 `includeStackTrace: false` 節省 token
   - 出現無法定位的 error 時才開啟 stack trace
   - 善用 `logType` 篩選（先看 error，再看 warning）
6. **迭代上限**：連續 3 次 compile-test 循環仍失敗時，暫停並向使用者報告分析結果，不可繼續盲目重試。

### 與其他 Skill 的關係（方案 B）

| Skill | 關係 |
|-------|------|
| `unity-mcp-workflow` | **雙向**：(1) 引用其錯誤處理表、工具注意事項；(2) 擴充它，新增「修改後驗證規則」和「EditMode 優先原則」簡述 |
| `test-engineer` | 引用：測試文件生成格式（`TestDoc_*.md`） |
| `unity-ui-builder` | 間接受益：透過 `unity-mcp-workflow` 的新增驗證規則，建構涉及 C# 時會自動 recompile |
| Phase 2 擴充 | 直接：Prefab Variant、Material 流程、Scene 管理新增至 `unity-mcp-workflow` |

### `unity-mcp-workflow` 擴充內容（方案 B 具體變更）

需在 `unity-mcp-workflow` 新增以下段落（純新增，不修改既有內容）：

**1. 修改後驗證規則（新增段落）**
```
修改 C# 代碼後，必須執行 `recompile_scripts` 確認編譯通過，才能繼續後續操作。
若編譯失敗，優先讀取錯誤訊息修正，不可跳過。
```

**2. EditMode 優先原則（新增段落）**
```
撰寫 Unity 測試時預設使用 EditMode。PlayMode 僅在需要 MonoBehaviour 生命週期、
Coroutine、Physics、場景載入時使用，且必須通過 Pre-flight Checklist。
完整指引參見 `unity-test-debug`。
```

**3. 禁止事項追加**
```
- 不要修改 C# 後不執行 recompile_scripts 驗證
```

### 已解決的討論事項

- [x] ~~是否需要定義 Unity 專用測試 patterns（如 `[UnityTest]` + `yield return` 的使用時機）？~~
  → **是**，已定義 PlayMode 適用場景表 + Pre-flight Checklist
- [x] ~~PlayMode 測試的場景載入策略是否要規範？~~
  → **採 EditMode 優先策略**，PlayMode 為例外路線且需 Pre-flight Checklist 門檻

### 不納入項目

- ~~Code Coverage 流程~~ → **不納入**。`com.unity.testtools.codecoverage` 是團隊品質管控工具，MCP 工具無專門讀取 coverage report 的能力，且非 compile→test→debug 迭代循環的核心需求。未來可擴充。
- ~~EditorCoroutineUtility 測非同步邏輯~~ → **不納入**。EditorCoroutineUtility 無法重現真實的幀、物理、渲染時序，不能替代 PlayMode 測 coroutine。它是 Editor 工具開發的實作細節，非測試策略。Skill 的 EditMode 優先策略已涵蓋：抽離邏輯 → EditMode 測邏輯 → 必要時才走 PlayMode。
- ~~`.asmdef` 模板~~ → **不納入**。Unity Test Runner 的「Create Test Assembly Folder」功能已提供正確模板。大多數專案 test assembly 已存在，AI 通常是往既有 assembly 加測試檔案。Pre-flight Checklist 已文檔正確設定規則。

### 待討論事項

Phase 1 所有討論事項已結案。可進入方案比較階段。

---

## Phase 2: 擴充 `unity-mcp-workflow`

### 定位

> **背景**：原計劃為獨立 skill `unity-3d-scene-builder`，但經分析使用者實際工作流後決定合併。
>
> 實際手遊專案架構：僅 2 個極簡 Scene（GameStarter + Download），真正的遊戲內容全在 AssetBundle 的 Prefab 中。Scene 建構需求極低，獨立 skill 內容過薄，有價值的部分應直接擴充 `unity-mcp-workflow`。

將以下內容新增至 `unity-mcp-workflow`（純新增段落，不修改既有內容）：

### 擴充內容清單

#### 1. Prefab Variant 工作流（新增段落）

現有 `unity-mcp-workflow` 的 Prefab 操作僅涵蓋「新建 Prefab」和「修改既有 Prefab」，缺少 Prefab Variant 工作流。

**新增內容（草案）：**

```
C. Prefab Variant：
   create_prefab(prefabName, basePrefab) 建立 Variant
   → open_prefab_contents 開啟 Variant
   → 修改差異屬性（override）
   → save_prefab_contents 儲存

使用時機：多個物件共用基底結構但有屬性差異（如不同怪物、不同等級按鈕）。
基底 Prefab 修改後，所有 Variant 自動繼承變更。
```

#### 2. Material 設計流程（新增段落）

現有 `unity-mcp-workflow` 僅在工具依賴關係列出 `create_material → assign_material`，缺乏完整 Material 設計引導。

**新增內容（草案）：**

```
Material 工作流：
1. get_material_info 查詢既有 Material 的 shader 屬性（先查再改）
2. create_material 建立新 Material（存至 Assets/Materials/）
   - 未指定 shader 時自動偵測 Render Pipeline（URP → Universal Render Pipeline/Lit）
3. modify_material 調整屬性（顏色、金屬度、貼圖等）
4. assign_material 指定給 GameObject（可指定 slot）
5. batch_execute 批量 assign 同一 Material 給多個物件
```

#### 2.1 Shader 引導（新增段落）

**Shader 使用層級：**

| 方式 | 做法 | 適用場景 |
|------|------|---------|
| 引用既有 Shader | `create_material` 指定 shader 名稱（如 `"Shader Graphs/MyShader"`） | 專案已有 Shader Graph shader |
| 寫 URP ShaderLab | AI 用 Write 工具寫 `.shader` 檔 → `recompile_scripts` → `create_material` 引用 | 需要自訂 shader 效果 |
| 建立 Shader Graph | **不支援** — `.shadergraph` 為複雜 JSON 結構，應在 Unity Editor 中視覺化建立 | — |

**Shader property 名稱差異（關鍵陷阱）：**

| 屬性 | URP (Universal Render Pipeline/Lit) | Built-in (Standard) |
|------|-------------------------------------|---------------------|
| 基底顏色 | `_BaseColor` | `_Color` |
| 基底貼圖 | `_BaseMap` | `_MainTex` |
| 金屬度 | `_Metallic` | `_Metallic` |
| 平滑度 | `_Smoothness` | `_Glossiness` |

> 規則：修改 Material 前必須先用 `get_material_info` 確認 shader 可用屬性名稱，不可假設。

**前置工具需求（建議）：**

新增 `unity://shaders` 資源或擴充 `unity://assets` 支援 shader 篩選，讓 AI 能查詢專案中可用的 shader 清單。目前 AI 只能猜測 shader 名稱或靠使用者提供。

#### 3. Scene 管理基本操作（新增段落）

現有 `unity-mcp-workflow` 未涵蓋 Scene 生命週期管理。

**新增內容（草案）：**

```
Scene 管理：
- 建立前用 get_scene_info 確認當前場景狀態（名稱、dirty、已載入場景列表）
- dirty scene 先 save_scene 再切換，避免遺失修改
- create_scene 可用 addToBuildSettings: true 直接加入 Build Settings
- Multi-scene 編輯：load_scene + additive: true
- 卸載場景：unload_scene（saveIfDirty: true 避免遺失）
```

#### 4. Transform 最佳實踐（新增段落）

現有 `unity-mcp-workflow` 的工具依賴關係列出 Transform 工具，但無使用引導。

**新增內容（草案）：**

```
Transform 操作：
- 優先用 set_transform 一次設定 position + rotation + scale（減少 API 呼叫）
- world vs local space：放置物件用 world，子物件微調用 local
- relative: true 做增量調整（如「往右移 2 單位」）
- 批量佈置：duplicate_gameobject(count: N) → 逐一 set_transform
```

### 涉及的 MCP 工具

| 工具 | 擴充內容 |
|------|---------|
| `create_material` / `modify_material` / `assign_material` / `get_material_info` | Material 設計流程 |
| `create_scene` / `load_scene` / `save_scene` / `unload_scene` / `get_scene_info` | Scene 管理 |
| `set_transform` / `move_gameobject` / `rotate_gameobject` / `scale_gameobject` | Transform 最佳實踐 |
| `create_prefab`（**需擴充：Variant 支援**） | Prefab Variant 工作流 |
| `unity://shaders`（**建議新增**） | 查詢可用 shader 清單 |

### 前置任務：MCP 工具變更

Phase 2 的 skill 擴充依賴以下 MCP 工具變更：

#### 必要變更

**`create_prefab` 支援 Prefab Variant**

現有實作用 `PrefabUtility.SaveAsPrefabAsset` 建立全新 prefab。需新增：

- 新增 `basePrefabPath` 參數（可選，string）
- 有 `basePrefabPath` 時：`PrefabUtility.InstantiatePrefab(basePrefab)` → 套用修改 → `SaveAsPrefabAsset`（保留與 base 的連結，Unity 自動識別為 Variant）
- 無 `basePrefabPath` 時：維持原有邏輯（建立獨立 prefab）
- TypeScript 端同步更新 schema

#### 建議變更

**新增 `unity://shaders` 資源**

讓 AI 能查詢專案中可用的 shader 清單（名稱 + 類型），避免猜測 shader 名稱。可延後至後續版本。

### 禁止事項（新增至 `unity-mcp-workflow`）

```
- 不要在未用 get_material_info 查詢 shader 屬性前盲目 modify_material
- 不要假設 shader property 名稱（URP 用 _BaseColor，Built-in 用 _Color，需先查詢）
- 不要嘗試手寫 .shadergraph 檔案（結構過於複雜，應在 Unity Editor 中用 Shader Graph 視覺化建立）
```

### 已解決的討論事項

- [x] ~~MCP 工具 `create_prefab` 是否支援建立 Prefab Variant？~~
  → **不支援，需擴充**。新增 `basePrefabPath` 參數，列為 Phase 2 前置任務
- [x] ~~Material 流程是否需要涵蓋 Shader Graph 相關操作？~~
  → **是**。引用既有 Shader Graph shader + 撰寫 URP ShaderLab `.shader` 納入引導；手寫 `.shadergraph` 不支援
- [x] ~~是否需要新增禁止事項？~~
  → **是**。新增 3 條禁止事項（Material 查詢、shader property 名稱、Shader Graph 手寫）

### 待討論事項

Phase 2 所有討論事項已結案。可進入方案比較階段。

---

## Phase 3: 擴充 `unity-mcp-workflow`（續）

### 定位

> **背景**：原計劃為獨立 skill `unity-project-setup`，但經分析後 Scene 管理已在 Phase 2 覆蓋，資料夾結構暫緩，剩餘內容（Package 管理、Menu Item、ScriptableObject）偏薄，不足以支撐獨立 skill。決定合併至 `unity-mcp-workflow`。

將以下內容新增至 `unity-mcp-workflow`（純新增段落，不修改既有內容）：

### 擴充內容清單

#### 1. Package 管理（新增段落）

現有 `unity-mcp-workflow` 未涵蓋 Package 安裝與管理流程。

**新增內容（草案）：**

```
Package 管理：
1. unity://packages 查詢已安裝 Package（先查再裝，避免重複）
2. add_package 安裝 Package：
   - registry：官方 Package（如 com.unity.textmeshpro）
   - github：社群 Package（需 repository URL + 可選 branch / path）
   - disk：本地 Package（需完整路徑）
3. recompile_scripts 確認安裝後無編譯衝突
4. unity://packages 確認安裝成功
```

#### 2. Menu Item 執行（新增段落）

現有 `unity-mcp-workflow` 未涵蓋 Menu Item 使用引導。

**新增內容（草案）：**

```
Menu Item 執行：
- 先用 unity://menu-items 查詢可用項目，確認路徑正確
- execute_menu_item 執行（路徑格式如 "GameObject/Create Empty"）
- 用途：觸發 Unity 內建或自訂選單功能（如 TMP Importer、Asset 匯入設定）
```

#### 3. ScriptableObject 建立（新增段落）

現有 `unity-mcp-workflow` 未涵蓋 ScriptableObject 建立流程。

**新增內容（草案）：**

```
ScriptableObject 建立：
- create_scriptable_object 需指定已存在的 C# 類別名（繼承 ScriptableObject）
- 工具不會建立 C# 腳本，類別必須先存在且編譯通過
- 建立前先 recompile_scripts 確認類別可用
```

### 涉及的 MCP 工具

| 工具/資源 | 擴充內容 |
|-----------|---------|
| `add_package` | Package 管理流程 |
| `execute_menu_item` | Menu Item 執行引導 |
| `create_scriptable_object` | ScriptableObject 建立流程 |
| `unity://packages` | Package 查詢（先查再裝） |
| `unity://menu-items` | Menu Item 查詢（先查再執行） |

### 禁止事項（新增至 `unity-mcp-workflow`）

```
- 不要在未用 unity://packages 確認前重複安裝已有的 Package
- 不要在未用 unity://menu-items 確認路徑前執行 execute_menu_item
- 不要在 C# 類別未編譯通過前嘗試 create_scriptable_object
```

### 不納入項目

- ~~常用 Package 推薦清單~~ → **不納入**。Package 需求因專案而異，提供推薦清單反而可能誤導 AI 安裝不需要的 Package。
- ~~資料夾結構規劃~~ → **暫緩**。需配合 Addressables / AssetBundle 打包策略，待策略確定後再擴充。
- ~~Project Settings 配置~~ → **不納入**。MCP 工具不直接支援 PlayerSettings 等，需新增工具後才有意義。

---

## 3. 技術方案

### 方案比較

#### 方案 A：獨立完整 Skill

`unity-test-debug` 自包含所有測試/除錯規則，不修改任何既有 skill。

- **Pros**：結構簡單、零迴歸風險、觸發條件單純
- **Cons**：其他 Unity skill 不會自動觸發編譯驗證；EditMode 優先原則僅限此 skill 內有效

#### 方案 B：分層整合方案 ✅ 已選定

將測試知識拆為共用層（`unity-mcp-workflow` 擴充）和專用層（`unity-test-debug`）。

- **Pros**：所有 Unity skill 受益於「修改後驗證」；EditMode 優先成為共用知識；職責更清晰
- **Cons**：需修改 `unity-mcp-workflow`（但為純新增段落，不改既有內容）

### 決策記錄

| 日期 | 決策 | 理由 |
|------|------|------|
| 2026-02-15 | Phase 1 採用方案 B（分層整合） | 「修改 C# 後 recompile 驗證」是基本衛生規則，應在共用層生效，讓所有 Unity skill 受益 |
| 2026-02-16 | Phase 2 從獨立 skill 改為擴充 `unity-mcp-workflow` | 實際手遊專案僅 2 個極簡 Scene，內容以 Prefab 為載體，獨立 `unity-3d-scene-builder` skill 內容過薄，有價值的部分（Prefab Variant、Material 流程、Scene 管理、Transform）直接合併至共用基底 |
| 2026-02-16 | `create_prefab` 需擴充支援 Prefab Variant | 新增 `basePrefabPath` 參數，列為 Phase 2 前置任務 |
| 2026-02-16 | Shader 引導：引用既有 + 寫 ShaderLab，不支援手寫 .shadergraph | `.shadergraph` 結構過於複雜，AI 手寫極易出錯；引用既有 shader 和撰寫 `.shader` 文字檔為可行方案 |
| 2026-02-16 | 新增 3 條 Material 相關禁止事項 | 避免 AI 盲目猜測 shader property 名稱導致錯誤 |
| 2026-02-16 | Phase 3 從獨立 skill 改為擴充 `unity-mcp-workflow` | Scene 管理已在 Phase 2 覆蓋，資料夾結構暫緩，剩餘內容（Package 管理、Menu Item、ScriptableObject）偏薄，合併更合理 |

---

## 4. 任務清單

- [x] Phase 1：設計並實作 `unity-test-debug` skill
  - [x] 確認核心規則與流程
  - [x] 方案比較與決策（方案 B：分層整合）
  - [x] 擴充 `unity-mcp-workflow`（新增修改後驗證規則 + EditMode 優先原則簡述 + 禁止事項）
  - [x] 撰寫 `unity-test-debug/SKILL.md`
  - [x] 撰寫 `unity-test-debug/claude-rule.md`
  - [x] ~~更新 `install-claude.sh` 註冊新 skill~~ — 不需要，`install-claude.sh` 自動發現 `skills/*/claude-rule.md`
- [x] Phase 2：擴充 `unity-mcp-workflow`
  - [x] 確認擴充內容（Prefab Variant、Material 流程、Shader 引導、Scene 管理、Transform）
  - [x] 確認禁止事項（3 條 Material 相關）
  - [x] 前置：擴充 `create_prefab` 支援 Prefab Variant（C# + TypeScript）
  - [x] 前置（建議）：新增 `unity://shaders` 資源
  - [x] 更新 `unity-mcp-workflow/SKILL.md`
  - [x] 更新 `unity-mcp-workflow/claude-rule.md`
- [ ] Phase 3：擴充 `unity-mcp-workflow`（續）
  - [x] 確認擴充內容（Package 管理、Menu Item、ScriptableObject）
  - [x] 確認禁止事項（3 條）
  - [x] 確認不納入項目（Package 推薦清單、資料夾結構、Project Settings）
  - [ ] 更新 `unity-mcp-workflow/SKILL.md`
  - [ ] 更新 `unity-mcp-workflow/claude-rule.md`
- [ ] 整合測試：確認 3 個新 skill 的觸發條件不互相衝突
