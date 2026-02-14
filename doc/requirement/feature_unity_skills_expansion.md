# Feature Design: Unity Skills 擴充

> **狀態**：Phase 1 方案已確定，待實作
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

| Phase | Skill | 優先級 | 狀態 |
|-------|-------|--------|------|
| Phase 1 | [`unity-test-debug`](#phase-1-unity-test-debug) | P0 | 待設計 |
| Phase 2 | [`unity-3d-scene-builder`](#phase-2-unity-3d-scene-builder) | P1 | 待設計 |
| Phase 3 | [`unity-project-setup`](#phase-3-unity-project-setup) | P2 | 待設計 |

### Skill 引用關係（方案 B：分層整合）

```
unity-mcp-workflow (共用基底，Phase 1 擴充)
├── [新增] 修改後驗證規則：改 C# → recompile_scripts → 確認編譯通過
├── [新增] EditMode 優先原則（簡述 + 指向 unity-test-debug 取得完整 checklist）
├── unity-ui-builder       (引用共用規則 + Figma/UI 專用，自動受益於驗證規則)
├── unity-3d-scene-builder (引用共用規則 + 3D 專用，自動受益於驗證規則)
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
| `unity-3d-scene-builder` | 間接受益：同上 |

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

## Phase 2: `unity-3d-scene-builder`

### 定位

3D 場景建構引導，與 `unity-ui-builder`（2D UI）互補。涵蓋場景管理、3D 物件佈局、Material 視覺設計。

### 觸發條件

- 「建一個 3D 場景」「搭建環境」
- 「放置物件到場景」「排列這些物件」
- 「建立材質」「設定 shader」「改顏色」
- 「管理場景」「建立新場景」「多場景編輯」

### 涵蓋的 MCP 工具與資源

| 工具/資源 | 用途 |
|-----------|------|
| `create_scene` / `load_scene` / `save_scene` / `delete_scene` / `unload_scene` | 場景生命週期管理 |
| `get_scene_info` | 查詢場景狀態 |
| `update_gameobject` / `get_gameobject` / `delete_gameobject` | GameObject CRUD |
| `move_gameobject` / `rotate_gameobject` / `scale_gameobject` / `set_transform` | Transform 操作 |
| `duplicate_gameobject` / `reparent_gameobject` | 複製與重組階層 |
| `create_material` / `modify_material` / `assign_material` / `get_material_info` | Material 全流程 |
| `add_asset_to_scene` | 放置模型/Prefab 到場景 |
| `batch_execute` | 批次操作 |
| `unity://scenes_hierarchy` | 查詢場景階層 |
| `unity://assets` | 查詢專案資源 |

### 核心流程（草案）

```
第一階段：場景規劃
    ├── 確認場景用途（遊戲關卡 / 測試場景 / Prototype）
    ├── 規劃物件階層樹
    └── 規劃 Material 清單

第二階段：場景建立
    ├── create_scene / load_scene
    └── 建立根層級結構（Environment / Characters / Props / Lighting）

第三階段：Material 準備
    ├── create_material（指定 shader、顏色）
    └── modify_material（調整屬性）

第四階段：物件佈局
    ├── update_gameobject 建立物件
    ├── add_asset_to_scene 放置外部資源
    ├── set_transform 設定位置/旋轉/縮放
    ├── assign_material 指定材質
    ├── duplicate_gameobject 批量佈置
    └── reparent_gameobject 整理階層

第五階段：Prefab 化
    ├── 可複用物件 → save_as_prefab
    └── add_asset_to_scene 放置更多實例

第六階段：儲存
    └── save_scene
```

### 關鍵規則（草案）

1. **先查詢再操作**：引用 `unity-mcp-workflow` 核心規則。
2. **階層結構規範**：建議使用語意化根節點（Environment、Props、Characters、UI、Managers）。
3. **Transform 最佳實踐**：
   - 優先用 `set_transform` 一次設定 position + rotation + scale
   - world vs local space 選擇：放置用 world，子物件微調用 local
   - 使用 `relative: true` 做增量調整
4. **Material 工作流**：
   - 先 `create_material` 存到 `Assets/Materials/`
   - 再 `assign_material` 指定給物件
   - 用 `get_material_info` 查詢 shader 可用屬性再修改
5. **批量佈置**：用 `duplicate_gameobject` + `count` 批量建立，再逐一 `set_transform`。
6. **場景管理**：
   - 建立前用 `get_scene_info` 確認當前場景狀態
   - dirty scene 先 `save_scene` 再 `load_scene` 切換
   - Multi-scene：`load_scene` + `additive: true`

### 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `unity-mcp-workflow` | 引用：核心規則、batch_execute、Prefab 操作、錯誤處理、工具注意事項 |
| `unity-ui-builder` | 互補：UI Builder 處理 2D UI，此 skill 處理 3D 場景 |

### 待討論事項

- [ ] 是否需要規範光照設定流程？（目前 MCP 工具不直接支援 Light 屬性，但可透過 `update_component` 操作）
- [ ] 是否需要提供常見場景模板（如 Prototype 場景的標準結構）？
- [ ] Terrain 操作是否納入？（需透過 `update_component` 操作 Terrain 組件）

---

## Phase 3: `unity-project-setup`

### 定位

專案初始化與配置引導。涵蓋 Package 管理、Menu Item 執行、ScriptableObject 建立、Build Settings 場景管理。

> **注意**：資料夾結構規劃因需配合 Addressables / AssetBundle 打包策略，暫不納入本 skill 範圍。待策略確定後再擴充。

### 觸發條件

- 「設定專案」「初始化」
- 「安裝 package」「加入 TextMeshPro」
- 「執行選單功能」
- 「建立 ScriptableObject」「建立配置資料」
- 「設定 Build Settings」「加場景到 Build」

### 涵蓋的 MCP 工具與資源

| 工具/資源 | 用途 |
|-----------|------|
| `add_package` | 安裝 Package（registry / github / disk） |
| `execute_menu_item` | 執行 Unity 選單功能 |
| `create_scriptable_object` | 建立 ScriptableObject 資產 |
| `create_scene` | 建立場景（含 `addToBuildSettings`） |
| `unity://packages` | 查詢已安裝 Package |
| `unity://menu-items` | 查詢可用選單項目 |
| `unity://assets` | 查詢專案資源 |

### 核心流程（草案）

```
第一階段：專案狀態確認
    ├── unity://packages → 列出已安裝 Package
    ├── unity://assets → 了解現有資源結構
    └── get_scene_info → 確認場景配置

第二階段：Package 管理
    ├── 確認需要安裝的 Package 清單
    ├── add_package（source: registry / github / disk）
    └── recompile_scripts → 確認無編譯錯誤

第三階段：專案配置
    ├── execute_menu_item → 執行必要的初始化選單（如 TMP Importer）
    ├── create_scriptable_object → 建立配置資料
    └── create_scene → 建立並加入 Build Settings

第四階段：驗證
    ├── recompile_scripts → 確認編譯正常
    └── unity://packages → 確認 Package 安裝成功
```

### 關鍵規則（草案）

1. **先查詢再安裝**：用 `unity://packages` 確認是否已安裝，避免重複。
2. **Package 來源選擇**：
   - `registry`：官方 Unity Package（如 `com.unity.textmeshpro`）
   - `github`：社群 Package（需 repository URL + 可選 branch / path）
   - `disk`：本地 Package（需完整路徑）
3. **安裝後驗證**：每次 `add_package` 後 `recompile_scripts` 確認無衝突。
4. **Menu Item 使用**：先用 `unity://menu-items` 查詢可用項目，確認路徑正確。
5. **ScriptableObject**：需指定已存在的 C# 類別名（繼承 `ScriptableObject`），工具不會建立腳本。

### 與其他 Skill 的關係

| Skill | 關係 |
|-------|------|
| `unity-mcp-workflow` | 引用：核心規則、錯誤處理 |
| `unity-test-debug` | 銜接：專案設定完成後可進入測試驗證 |

### 待討論事項

- [ ] 是否需要提供常用 Package 推薦清單？（如 TMP、Cinemachine、Input System）
- [ ] 資料夾結構規劃待 Addressables / AssetBundle 策略確定後再納入
- [ ] 是否需要涵蓋 Project Settings 的配置？（目前 MCP 工具不直接支援 PlayerSettings 等）

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
| 2026-02-15 | 採用方案 B（分層整合） | 「修改 C# 後 recompile 驗證」是基本衛生規則，應在共用層生效，讓所有 Unity skill 受益 |

---

## 4. 任務清單

- [ ] Phase 1：設計並實作 `unity-test-debug` skill
  - [x] 確認核心規則與流程
  - [x] 方案比較與決策（方案 B：分層整合）
  - [ ] 擴充 `unity-mcp-workflow`（新增修改後驗證規則 + EditMode 優先原則簡述 + 禁止事項）
  - [ ] 撰寫 `unity-test-debug/SKILL.md`
  - [ ] 撰寫 `unity-test-debug/claude-rule.md`
  - [ ] 更新 `install-claude.sh` 註冊新 skill
- [ ] Phase 2：設計並實作 `unity-3d-scene-builder` skill
  - [ ] 確認核心規則與流程
  - [ ] 評估是否需要擴充 `unity-mcp-workflow` 的 3D 通用內容
  - [ ] 撰寫 `SKILL.md`
  - [ ] 撰寫 `claude-rule.md`
  - [ ] 更新 `install-claude.sh` 註冊新 skill
- [ ] Phase 3：設計並實作 `unity-project-setup` skill
  - [ ] 確認核心規則與流程
  - [ ] 撰寫 `SKILL.md`
  - [ ] 撰寫 `claude-rule.md`
  - [ ] 更新 `install-claude.sh` 註冊新 skill
- [ ] 整合測試：確認 3 個新 skill 的觸發條件不互相衝突
