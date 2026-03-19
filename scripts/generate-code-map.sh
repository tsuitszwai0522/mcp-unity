#!/bin/bash
# generate-code-map.sh
# 掃描 codebase 中的關鍵 class，生成 per-module code map + INDEX
#
# 使用方式:
#   bash .sop/scripts/generate-code-map.sh           # 從項目根目錄執行
#   bash .sop/scripts/generate-code-map.sh /path/to  # 指定項目根目錄

set -e

# ─── 路徑解析 ───────────────────────────────────────────────────────

SCRIPT_DIR="$(cd "$(dirname "${BASH_SOURCE[0]}")" && pwd)"

# 接受參數或自動推導項目根目錄
# 支援三種執行位置:
#   1. 參數指定: bash generate-code-map.sh /path/to/project
#   2. 從 .sop/scripts/ 執行: PROJECT_ROOT = ../../
#   3. 從 scripts/ 執行（安裝後）: PROJECT_ROOT = ../
find_project_root() {
    local dir="$1"
    while [ "$dir" != "/" ]; do
        if [ -f "$dir/sop-config.json" ]; then
            echo "$dir"
            return 0
        fi
        dir="$(dirname "$dir")"
    done
    return 1
}

if [ -n "$1" ]; then
    PROJECT_ROOT="$(cd "$1" && pwd)"
else
    PROJECT_ROOT=$(find_project_root "$SCRIPT_DIR") || {
        echo "❌ 找不到 sop-config.json（從 $SCRIPT_DIR 向上搜索）"
        exit 1
    }
fi

CONFIG_FILE="$PROJECT_ROOT/sop-config.json"

if ! command -v jq &> /dev/null; then
    echo "❌ 需要 jq。安裝: brew install jq (macOS) / apt install jq (Linux)"
    exit 1
fi

# ─── 讀取配置 ───────────────────────────────────────────────────────

OUTPUT_DIR="$PROJECT_ROOT/$(jq -r '.paths.codeMap // "doc/code-map/"' "$CONFIG_FILE")"
SCAN_PATHS_JSON=$(jq -c '.codeMap.scanPaths // []' "$CONFIG_FILE")
SCAN_COUNT=$(echo "$SCAN_PATHS_JSON" | jq length)

# 讀取 filePatterns，預設值
FILE_PATTERNS_JSON=$(jq -c '.codeMap.filePatterns // ["*Manager.cs","*Controller.cs","*Service.cs","*System.cs","*Handler.cs","*SO.cs"]' "$CONFIG_FILE")

if [ "$SCAN_COUNT" -eq 0 ]; then
    echo "⚠️  sop-config.json 中未配置 codeMap.scanPaths"
    exit 0
fi

mkdir -p "$OUTPUT_DIR"

echo "🔍 Generating code map..."
echo "   Project: $PROJECT_ROOT"
echo "   Output:  $OUTPUT_DIR"
echo ""

# ─── 工具函數 ───────────────────────────────────────────────────────

# 從 .cs 檔案中提取 class 宣告
# 輸出格式: ClassName|BaseClass
extract_classes() {
    local file="$1"
    # 不用 -n，避免行號干擾 sed 解析
    grep -E '^\s*(public|internal|private|protected|abstract|sealed|static|partial|\s)*class\s+[A-Za-z_]' "$file" 2>/dev/null \
    | while IFS= read -r line; do
        local class_name base_class
        class_name=$(echo "$line" | sed -E 's/.*class[[:space:]]+([A-Za-z_][A-Za-z0-9_]*).*/\1/')
        # 嘗試提取 : 後的 base class
        if echo "$line" | grep -qE ':\s*[A-Za-z_]'; then
            base_class=$(echo "$line" | sed -E 's/.*class[[:space:]]+[A-Za-z_][A-Za-z0-9_]*[[:space:]]*:[[:space:]]*([A-Za-z_][A-Za-z0-9_<>]*).*/\1/')
        else
            base_class="-"
        fi
        # 防呆：如果 sed 解析失敗，fallback
        if [ ${#base_class} -gt 80 ]; then
            base_class="-"
        fi
        echo "${class_name}|${base_class}"
    done
}

# 從已有的 code-map 檔案中讀取 Description 欄
# 輸出格式: ClassName|Description
load_existing_descriptions() {
    local file="$1"
    [ -f "$file" ] || return 0
    # 跳過表頭和分隔線，只處理數據行
    awk -F'|' '
        /^\|[[:space:]]*Class[[:space:]]*\|/ { next }
        /^\|[-]+/ { next }
        /^\|[[:space:]]*$/ { next }
        NF >= 5 {
            class = $2; desc = $5
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", class)
            gsub(/^[[:space:]]+|[[:space:]]+$/, "", desc)
            if (class != "" && desc != "")
                print class "|" desc
        }
    ' "$file"
}

# ─── 掃描各模組 ────────────────────────────────────────────────────

# INDEX 暫存
INDEX_ROWS=""
TOTAL_ALL_CLASSES=0

for i in $(seq 0 $((SCAN_COUNT - 1))); do
    SCAN_PATH=$(echo "$SCAN_PATHS_JSON" | jq -r ".[$i].path")
    LABEL=$(echo "$SCAN_PATHS_JSON" | jq -r ".[$i].label")
    FILENAME=$(echo "$LABEL" | tr ' ' '-' | tr '[:upper:]' '[:lower:]')
    FULL_SCAN_PATH="$PROJECT_ROOT/$SCAN_PATH"
    TARGET_FILE="$OUTPUT_DIR/${FILENAME}.md"

    if [ ! -d "$FULL_SCAN_PATH" ]; then
        echo "   ⚠️  跳過: $SCAN_PATH (目錄不存在)"
        continue
    fi

    echo "   📂 掃描: $SCAN_PATH"

    # 載入已有描述到 temp file
    DESC_CACHE=$(mktemp)
    load_existing_descriptions "$TARGET_FILE" > "$DESC_CACHE"

    # 構建 find 參數
    FIND_EXPR=""
    PATTERN_COUNT=$(echo "$FILE_PATTERNS_JSON" | jq length)
    for pi in $(seq 0 $((PATTERN_COUNT - 1))); do
        pat=$(echo "$FILE_PATTERNS_JSON" | jq -r ".[$pi]")
        if [ -n "$FIND_EXPR" ]; then
            FIND_EXPR="$FIND_EXPR -o"
        fi
        FIND_EXPR="$FIND_EXPR -name $pat"
    done

    # 收集所有 class 資訊到 temp file
    # 格式: Section|ClassName|BaseClass|RelPath
    DATA_FILE=$(mktemp)

    eval "find '$FULL_SCAN_PATH' \\( $FIND_EXPR \\) -type f" 2>/dev/null | sort | while IFS= read -r file; do
        [ -z "$file" ] && continue
        rel_from_scan="${file#$FULL_SCAN_PATH}"
        rel_from_scan="${rel_from_scan#/}"

        # Section = 第一層子目錄
        section=$(echo "$rel_from_scan" | cut -d'/' -f1)
        if [[ "$section" == *.cs ]]; then
            section="(Root)"
        fi

        # 顯示路徑 = scanPath + 相對路徑
        display_path="${SCAN_PATH}${rel_from_scan}"

        extract_classes "$file" | while IFS='|' read -r cname bclass; do
            [ -z "$cname" ] && continue
            echo "${section}|${cname}|${bclass}|${display_path}" >> "$DATA_FILE"
        done
    done

    # 生成 module 檔案
    {
        echo "# ${LABEL} Code Map"
        echo "> Auto-generated by \`.sop/scripts/generate-code-map.sh\`"
        echo "> Last updated: $(date +%Y-%m-%d)"
        echo ""

        current_section=""
        while IFS='|' read -r section cname bclass dpath; do
            if [ "$section" != "$current_section" ]; then
                if [ -n "$current_section" ]; then
                    echo ""
                fi
                echo "## ${section}"
                echo "| Class | Base | File | Description |"
                echo "|-------|------|------|-------------|"
                current_section="$section"
            fi
            # 查找已有描述
            existing_desc=$(grep "^${cname}|" "$DESC_CACHE" 2>/dev/null | head -1 | cut -d'|' -f2)
            echo "| ${cname} | ${bclass} | \`${dpath}\` | ${existing_desc} |"
        done < <(sort -t'|' -k1,1 -k2,2 "$DATA_FILE")
    } > "$TARGET_FILE"

    # 統計
    module_classes=$(wc -l < "$DATA_FILE" | tr -d ' ')
    module_sections=$(cut -d'|' -f1 "$DATA_FILE" | sort -u | wc -l | tr -d ' ')
    TOTAL_ALL_CLASSES=$((TOTAL_ALL_CLASSES + module_classes))

    INDEX_ROWS="${INDEX_ROWS}| ${LABEL} | ${module_sections} | ${module_classes} | [${FILENAME}.md](${FILENAME}.md) |\n"

    echo "      → ${module_sections} sections, ${module_classes} classes"

    # 清理
    rm -f "$DESC_CACHE" "$DATA_FILE"
done

# ─── 生成 INDEX.md ──────────────────────────────────────────────────

# 保留既有的 MANUAL_SECTION 內容（Agent 手動補充的 Entry Points）
EXISTING_INDEX="$OUTPUT_DIR/INDEX.md"
MANUAL_CONTENT=""
if [ -f "$EXISTING_INDEX" ]; then
    MANUAL_CONTENT=$(sed -n '/<!-- MANUAL_SECTION_START -->/,/<!-- MANUAL_SECTION_END -->/p' "$EXISTING_INDEX")
fi

# 如果沒有既有內容，使用預設模板
if [ -z "$MANUAL_CONTENT" ]; then
    MANUAL_CONTENT='<!-- MANUAL_SECTION_START -->
| System | Module | Entry File | Description |
|--------|--------|------------|-------------|
| | | | |
<!-- MANUAL_SECTION_END -->'
fi

cat > "$OUTPUT_DIR/INDEX.md" << EOF
# Code Map Index
> Auto-generated by \`.sop/scripts/generate-code-map.sh\`
> Last updated: $(date +%Y-%m-%d)
>
> Agent 導航規則: 先讀此檔定位模組，再按需讀取對應的模組檔案。禁止一次讀取所有模組檔。

## Modules
| Module | Sections | Classes | Detail |
|--------|----------|---------|--------|
$(echo -e "$INDEX_ROWS")

## System Entry Points
> 以下由 AI Agent 在工作中增量補充，script 不會覆蓋此區段。

${MANUAL_CONTENT}

---
Total: ${TOTAL_ALL_CLASSES} key classes indexed.
EOF

echo ""
echo "✅ Code map 已生成: $OUTPUT_DIR"
echo "   INDEX.md + $(echo -e "$INDEX_ROWS" | grep -c '|') module files"
echo "   共索引 ${TOTAL_ALL_CLASSES} 個關鍵 class"
