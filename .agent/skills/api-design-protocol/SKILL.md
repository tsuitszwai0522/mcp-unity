---
name: api-design-protocol
description: 當使用者需要設計新的 API 端點、修改現有 API 或進行 API 版本規劃時使用。
---

# API Design Protocol (API 設計規範)

此 Skill 用於規範 API 設計流程，採用「契約優先 (Contract-First)」原則，確保 API 設計經過充分討論後才進入實作。

## 核心規則 (Core Rules)

1. **語言要求**：所有文件與溝通必須使用 **zh-TW**。
2. **契約優先 (Contract-First)**：
   - **必須**先完成 API 契約定義，才能開始實作。
   - 契約包含：端點、方法、請求/回應格式、錯誤碼。
3. **禁止搶跑**：在使用者明確 **Approve (批准)** API 設計之前，**嚴格禁止**實作代碼。
4. **檔案優先 (File Only)**：
   - **必須**將完整的 API 設計文件存檔至 `doc/apiDesign/`。
   - **禁止**在對話視窗中輸出設計全文。
5. **版本意識**：設計時必須考慮向後相容性與版本策略。

## API 設計原則 (Design Principles)

### RESTful 設計原則
| 原則 | 說明 |
|------|------|
| **資源導向** | URL 代表資源，不是動作 (`/users` 而非 `/getUsers`) |
| **HTTP 動詞語意** | GET=讀取, POST=建立, PUT=完整更新, PATCH=部分更新, DELETE=刪除 |
| **狀態碼正確** | 2xx=成功, 4xx=客戶端錯誤, 5xx=伺服器錯誤 |
| **一致性** | 命名、格式、錯誤處理在整個 API 中保持一致 |

### gRPC 設計原則
| 原則 | 說明 |
|------|------|
| **Service 粒度** | 依業務領域劃分 Service |
| **方法命名** | 使用動詞開頭 (`GetUser`, `CreateOrder`) |
| **串流選擇** | 大量資料用 Server Streaming，雙向互動用 Bidirectional |

## 執行流程 (Workflow)

### 第一階段：需求釐清 (Requirement Clarification)

當使用者提出 API 需求時：

1. **建立檔案**：
   - 在 `doc/apiDesign/` 目錄下建立 Markdown 文件。
   - 命名規則：`API_{YYYYMMDD}_{ResourceName}.md`
   - 例如：`API_20260115_UserAuthentication.md`

2. **釐清需求**：
   - 這個 API 要解決什麼業務問題？
   - 誰是 API 的消費者？（前端、第三方、內部服務）
   - 預期的呼叫頻率？（高頻需考慮快取、限流）
   - 是否需要即時性？（REST vs WebSocket vs gRPC）

### 第二階段：契約設計 (Contract Design)

1. **撰寫設計文件**：

   #### 1. API 概述 (Overview)
   - **API 名稱**：簡潔描述此 API 的用途。
   - **版本**：v1 / v2 / ...
   - **協議**：REST / gRPC / GraphQL。
   - **Base Path**：`/api/v1/...`

   #### 2. 端點定義 (Endpoints)
   針對每個端點，定義：
   - **Method & Path**：`GET /users/{id}`
   - **描述**：此端點的用途。
   - **認證**：需要 / 不需要 / 選擇性。
   - **權限**：需要的 Role 或 Permission。

   #### 3. 請求格式 (Request)
   - **Path Parameters**：`{id}` 等路徑參數。
   - **Query Parameters**：分頁、篩選等參數。
   - **Request Body**：JSON Schema 或 Protobuf 定義。
   - **Headers**：必要的 Header（Authorization, Content-Type）。

   #### 4. 回應格式 (Response)
   - **成功回應**：HTTP 200/201 時的回應格式。
   - **錯誤回應**：標準化的錯誤格式。
   - **分頁格式**：若有分頁，定義分頁結構。

   #### 5. 錯誤碼定義 (Error Codes)
   - 列出所有可能的錯誤碼與訊息。
   - 區分業務錯誤與系統錯誤。

   #### 6. 範例 (Examples)
   - 提供完整的請求/回應範例。
   - 包含成功與失敗的範例。

   #### 7. 版本策略 (Versioning Strategy)
   - 此版本與前版本的差異。
   - 向後相容性考量。
   - Deprecation 計畫（若有）。

2. **通知使用者**：「API 設計文件已建立，請查看：[檔案路徑]」。

### 第三階段：設計審閱 (Design Review)

1. **相容性檢查**：
   - 與現有 API 風格是否一致？
   - 是否會破壞現有客戶端？

2. **安全性檢查**：
   - 認證機制是否適當？
   - 是否有敏感資料暴露風險？
   - 是否需要限流 (Rate Limiting)？

3. **效能考量**：
   - 是否需要快取？
   - 是否需要分頁？
   - 是否有 N+1 Query 風險？

### 第四階段：批准與實作 (Approval & Implementation)

當使用者明確表示「批准」後：

1. **生成代碼骨架**（若適用）：
   - Controller / Handler 骨架。
   - DTO / Request / Response 類別。
   - Protobuf 定義（若為 gRPC）。

2. **更新文件**：
   - 將設計文件連結到實作代碼。
   - 更新 API 總覽文件（若有）。

## API 設計 Checklist

### 基本設計
- [ ] 端點命名符合 RESTful 慣例（或 gRPC 慣例）
- [ ] HTTP 方法語意正確
- [ ] 路徑參數與查詢參數區分適當
- [ ] 回應格式一致

### 安全性
- [ ] 敏感操作需要認證
- [ ] 權限控制適當
- [ ] 無敏感資料洩漏
- [ ] 考慮 Rate Limiting

### 效能
- [ ] 大量資料有分頁
- [ ] 考慮快取策略
- [ ] 避免 N+1 Query

### 相容性
- [ ] 與現有 API 風格一致
- [ ] 向後相容（若為更新）
- [ ] 有版本策略

### 文件
- [ ] 所有端點都有描述
- [ ] 有請求/回應範例
- [ ] 錯誤碼完整

## 觸發時機 (When to use)

- 使用者說：「設計一個 API 來...」
- 使用者說：「新增一個端點」
- 使用者說：「幫我規劃 API 結構」
- 使用者說：「這個功能需要什麼 API？」
- 使用者說：「API 版本升級怎麼做？」

## 禁止事項 (Don'ts)

1. ❌ 未完成設計文件就開始寫實作代碼
2. ❌ 設計時不考慮錯誤處理
3. ❌ 忽略版本相容性
4. ❌ 不提供請求/回應範例
5. ❌ 端點命名不一致（混用 camelCase 和 snake_case）
6. ❌ 在 URL 中使用動詞（如 `/getUser` 而非 `/users/{id}`）

## 與其他 Skills 的協作

| 情境 | 協作方式 |
|------|---------|
| 新功能需要 API | 先用 `feature-design-protocol` 設計功能，再用 `api-design-protocol` 設計 API |
| API 涉及 SharedLib | 設計完成後，優先在 SharedLib 定義 DTO |
| API 需要測試 | 設計完成後，用 `test-engineer` 規劃 Integration Test |
