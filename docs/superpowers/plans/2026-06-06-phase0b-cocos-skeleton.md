# Phase 0B Cocos 项目骨架 Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 在 Cocos Creator 3.x 项目中建立目录结构、TypeScript 类型定义、ESLint 配置和 CLAUDE.md，为后续编码阶段奠定规范基础。

**Architecture:** 纯配置和接口定义。类型定义文件（`.ts`）只含接口和枚举，无业务逻辑，无需单元测试——验收标准是 `tsc --noEmit` 无报错。

**Tech Stack:** Cocos Creator 3.x, TypeScript 5.x, ESLint

**前置条件（用户操作）：**
在 Cocos Creator Hub 中新建项目：
- 模板选：`Empty（TypeScript）`
- 项目名：`zidou`
- 引擎版本：`3.8.x`（当前稳定版）
- 确认后，所有后续步骤在该项目目录内执行

---

## File Structure

```
zidou/                          ← Cocos Creator 项目根目录
  CLAUDE.md                     ← Claude Code 协作规范（本计划创建）
  .gitignore                    ← Cocos 专用 gitignore（本计划创建）
  .gitattributes                ← LFS 配置（本计划创建）
  .eslintrc.js                  ← ESLint 配置（本计划创建）
  tsconfig.json                 ← 已由 Cocos 生成，本计划追加 strict 选项
  assets/
    scripts/
      data/
        CharacterTypes.ts       ← 角色/部件/配方的 TS 接口（本计划创建）
        RecipeGraph.ts          ← 配方 DAG 数据结构接口（本计划创建）
      battle/
        BattleTypes.ts          ← 战斗状态机类型（本计划创建）
      platform/
        PlatformSDK.ts          ← 三端平台适配接口（本计划创建）
```

---

### Task 1: .gitignore + .gitattributes

**Files:**
- Create: `.gitignore`
- Create: `.gitattributes`

- [ ] **Step 1: 创建 .gitignore**

```gitignore
# Cocos Creator
temp/
local/
build/
.creator/
native/
*.meta.bak

# Node
node_modules/
npm-debug.log*

# OS
.DS_Store
Thumbs.db

# IDE
.vscode/settings.json
.idea/

# 大资源走 LFS，不直接提交
*.png
*.jpg
*.psd
*.mp3
*.ogg
*.wav
*.ttf
*.otf
```

- [ ] **Step 2: 创建 .gitattributes（LFS）**

```gitattributes
# Images
*.png filter=lfs diff=lfs merge=lfs -text
*.jpg filter=lfs diff=lfs merge=lfs -text
*.psd filter=lfs diff=lfs merge=lfs -text

# Audio
*.mp3 filter=lfs diff=lfs merge=lfs -text
*.ogg filter=lfs diff=lfs merge=lfs -text
*.wav filter=lfs diff=lfs merge=lfs -text

# Fonts
*.ttf filter=lfs diff=lfs merge=lfs -text
*.otf filter=lfs diff=lfs merge=lfs -text
```

- [ ] **Step 3: Commit**

```bash
git add .gitignore .gitattributes
git commit -m "chore: add gitignore and LFS config for Cocos project"
```

---

### Task 2: ESLint + tsconfig strict

**Files:**
- Create: `.eslintrc.js`
- Modify: `tsconfig.json`

- [ ] **Step 1: 安装 ESLint**

```bash
npm install --save-dev eslint @typescript-eslint/parser @typescript-eslint/eslint-plugin
```

- [ ] **Step 2: 创建 .eslintrc.js**

```javascript
// .eslintrc.js
module.exports = {
  root: true,
  parser: "@typescript-eslint/parser",
  plugins: ["@typescript-eslint"],
  extends: [
    "eslint:recommended",
    "plugin:@typescript-eslint/recommended",
  ],
  parserOptions: {
    ecmaVersion: 2020,
    sourceType: "module",
  },
  rules: {
    "@typescript-eslint/no-explicit-any": "warn",
    "@typescript-eslint/explicit-function-return-type": "off",
    "no-console": "off",
  },
  env: {
    browser: true,
    es2020: true,
  },
};
```

- [ ] **Step 3: 在 tsconfig.json 中追加 strict 配置**

找到 Cocos 生成的 `tsconfig.json`，在 `compilerOptions` 中追加：

```json
{
  "compilerOptions": {
    "strict": true,
    "noUnusedLocals": true,
    "noImplicitReturns": true
  }
}
```

- [ ] **Step 4: 验证 lint 通过**

```bash
npx eslint assets/scripts --ext .ts
```

预期：无报错（新项目无 TS 文件时，输出为空）

- [ ] **Step 5: Commit**

```bash
git add .eslintrc.js tsconfig.json package.json package-lock.json
git commit -m "chore: add ESLint and strict TypeScript config"
```

---

### Task 3: 核心 TypeScript 类型定义

**Files:**
- Create: `assets/scripts/data/CharacterTypes.ts`
- Create: `assets/scripts/data/RecipeGraph.ts`
- Create: `assets/scripts/battle/BattleTypes.ts`
- Create: `assets/scripts/platform/PlatformSDK.ts`

- [ ] **Step 1: 创建目录**

```bash
mkdir -p assets/scripts/data assets/scripts/battle assets/scripts/platform
```

- [ ] **Step 2: 创建 CharacterTypes.ts**

```typescript
// assets/scripts/data/CharacterTypes.ts

export type Attribute = "fire" | "water" | "wood" | "gold" | "heart" | "none";
export type Rarity = "common" | "uncommon" | "rare";
export type EffectTarget = "single" | "all" | "self";

export interface EffectStatus {
  type: "burn" | "freeze" | "paralyze" | "shield_decay";
  stacks: number;
}

export interface CharacterEffect {
  type: "damage" | "heal" | "shield" | "summon" | "status" | "debuff";
  value?: number;
  target?: EffectTarget;
  pierce?: boolean;        // 穿透护甲
  status?: EffectStatus;
}

export interface Component {
  id: string;
  name: string;
  pinyin: string;
  attr: Attribute;
  rarity: Rarity;
  weakEffect: CharacterEffect;  // 部件直出的弱效果（防卡手）
}

export interface Character {
  id: string;
  name: string;
  pinyin: string;
  definition: string;
  attr: Attribute;
  level: 1 | 2 | 3;
  apCost: number;
  rarity: Rarity;
  effect: CharacterEffect;
  recipeIds: string[];   // 配方原料 ID 列表（引用 Component.id 或 Character.id）
}
```

- [ ] **Step 3: 创建 RecipeGraph.ts**

```typescript
// assets/scripts/data/RecipeGraph.ts

import { Character, Component } from "./CharacterTypes";

export type RecipeNode = Character | Component;

export interface RecipeGraph {
  characters: Map<string, Character>;
  components: Map<string, Component>;
}

export function isComponent(node: RecipeNode): node is Component {
  return "weakEffect" in node;
}

export function isCharacter(node: RecipeNode): node is Character {
  return "recipeIds" in node;
}

export function getRecipe(graph: RecipeGraph, charId: string): RecipeNode[] | null {
  const char = graph.characters.get(charId);
  if (!char) return null;
  return char.recipeIds.map(id =>
    graph.characters.get(id) ?? graph.components.get(id) ?? null
  ).filter((n): n is RecipeNode => n !== null);
}

export function canDismantle(graph: RecipeGraph, charId: string): boolean {
  const char = graph.characters.get(charId);
  return !!char && char.recipeIds.length > 0;
}
```

- [ ] **Step 4: 创建 BattleTypes.ts**

```typescript
// assets/scripts/battle/BattleTypes.ts

import { Attribute } from "../data/CharacterTypes";

export type BattlePhase = "player" | "enemy" | "resolution" | "end";

export interface StatusEffect {
  type: "burn" | "freeze" | "armor_break" | "heart_demon" | "shield";
  stacks: number;
}

export interface Enemy {
  id: string;
  name: string;
  attr: Attribute;
  hp: number;
  maxHp: number;
  armor: number;
  weakness: Attribute;
  statusEffects: StatusEffect[];
  intentQueue: EnemyIntent[];
  currentIntentIndex: number;
}

export type EnemyIntentType = "attack" | "defend" | "debuff" | "special";

export interface EnemyIntent {
  type: EnemyIntentType;
  value?: number;
}

export interface PlayerState {
  hp: number;
  maxHp: number;
  ap: number;
  maxAp: number;
  armor: number;
  statusEffects: StatusEffect[];
}

export interface BattleState {
  player: PlayerState;
  enemies: Enemy[];
  library: string[];      // Character IDs in player's library
  pool: string[];         // Component/Character IDs in piece pool
  libraryMax: number;
  poolMax: number;
  usedThisCombat: string[];  // Character IDs used, restored after combat
  phase: BattlePhase;
  turn: number;
  log: string[];
}

export function createInitialBattleState(enemies: Enemy[]): BattleState {
  return {
    player: { hp: 50, maxHp: 50, ap: 3, maxAp: 3, armor: 0, statusEffects: [] },
    enemies,
    library: [],
    pool: [],
    libraryMax: 8,
    poolMax: 12,
    usedThisCombat: [],
    phase: "player",
    turn: 1,
    log: [],
  };
}
```

- [ ] **Step 5: 创建 PlatformSDK.ts**

```typescript
// assets/scripts/platform/PlatformSDK.ts
// 三端平台适配接口。游戏逻辑只依赖此接口，不依赖具体平台 API。

export interface SaveData {
  version: number;
  unlocked: string[];      // unlocked character/artifact IDs
  runHistory: RunSummary[];
  settings: GameSettings;
}

export interface RunSummary {
  date: number;
  winStyle: string;
  chapters: number;
  difficulty: number;
}

export interface GameSettings {
  musicVolume: number;
  sfxVolume: number;
  fullscreen: boolean;
}

export interface Achievement {
  id: string;
  unlocked: boolean;
}

export interface PlatformSDK {
  saveGame(data: SaveData): Promise<void>;
  loadGame(): Promise<SaveData | null>;
  unlockAchievement(achievementId: string): Promise<void>;
  getAchievements(): Promise<Achievement[]>;
  getPlatformName(): "steam" | "ios" | "android" | "dev";
}

export class DevPlatformSDK implements PlatformSDK {
  private _save: SaveData | null = null;

  async saveGame(data: SaveData): Promise<void> {
    this._save = data;
    localStorage.setItem("zidou_save", JSON.stringify(data));
  }

  async loadGame(): Promise<SaveData | null> {
    const raw = localStorage.getItem("zidou_save");
    return raw ? JSON.parse(raw) : null;
  }

  async unlockAchievement(_id: string): Promise<void> {}

  async getAchievements(): Promise<Achievement[]> { return []; }

  getPlatformName() { return "dev" as const; }
}
```

- [ ] **Step 6: 运行 TypeScript 类型检查**

```bash
npx tsc --noEmit
```

预期：无报错

- [ ] **Step 7: Commit**

```bash
git add assets/scripts/
git commit -m "feat(types): add core TypeScript interfaces for character, recipe, battle, platform"
```

---

### Task 4: CLAUDE.md

**Files:**
- Create: `CLAUDE.md`

- [ ] **Step 1: 创建 CLAUDE.md**

```markdown
# CLAUDE.md — 《字·斗》项目协作规范

## 项目概况

- **游戏名称：** 字·斗
- **引擎：** Cocos Creator 3.8.x（TypeScript）
- **目标平台：** Steam(PC/Mac) + 海外 iOS + 海外 Google Play
- **商业模式：** Premium 买断制（无服务端、无内购、无广告）

## 目录结构

```
assets/scripts/
  data/       # 类型定义 + 配方图谱 + 角色/部件数据
  battle/     # 战斗状态机与结算逻辑
  ui/         # UI 组件（依赖 Cocos cc 模块）
  platform/   # 三端平台适配层（PlatformSDK 接口）
  utils/      # 纯工具函数（无 Cocos 依赖）
scripts/      # Python 数据管线（独立于 Cocos 项目）
data/         # 管线输出的 JSON 配置文件
```

## 核心原则

1. **无服务端**：存档走平台云（Steam Cloud / iCloud / Google Play）。不自建后端。
2. **数据驱动**：所有角色/配方/数值以 `data/*.json` 存储，改平衡不改代码。
3. **三端就绪**：业务逻辑不引用平台 API，只依赖 `PlatformSDK` 接口。
4. **纯逻辑优先**：`battle/` 和 `data/` 下的文件不依赖 Cocos `cc` 模块，便于单元测试。

## 平台适配层约定

- 游戏代码只 `import { PlatformSDK } from "../platform/PlatformSDK"`
- 具体实现（SteamSDK / AppleSDK / GoogleSDK）在 `platform/impl/` 下，由启动场景注入
- 开发环境使用 `DevPlatformSDK`（localStorage 存档）

## 数据结构约定

- 配方图谱：`RecipeGraph`（见 `assets/scripts/data/RecipeGraph.ts`）
- 配方 JSON 路径：`data/characters.json`（由 `scripts/build_data.py` 生成）
- 角色 ID 即汉字本身（如 `"焚"`）

## Claude Code 分工

| 角色 | 职责 |
|---|---|
| Opus | 架构设计、拆合引擎算法、战斗结算核心逻辑、平台适配层设计 |
| Sonnet | 配方表批量装配、UI 组件、配置读取、单元测试、文档 |
| 人工 | 场景搭建、UI 布局、美术资源接入、数值平衡迭代 |

## 命名规范

- 接口以大写字母开头：`BattleState`, `Character`, `RecipeGraph`
- 文件名 PascalCase（TS）：`BattleTypes.ts`, `RecipeGraph.ts`
- 函数名 camelCase：`dismantle()`, `compose()`, `suggest()`
- 配置 JSON key 用 camelCase：`apCost`, `recipeIds`

## 禁止事项

- 不在代码中硬编码角色数值（改配置文件）
- 不直接调用平台 API（通过 PlatformSDK 接口）
- 不在 `battle/` 和 `data/` 下 import `cc`（Cocos 模块）
```

- [ ] **Step 2: Commit**

```bash
git add CLAUDE.md
git commit -m "docs: add CLAUDE.md project collaboration guide"
```

---

## Self-Review

**Spec coverage:**
- ✅ 目录结构（scene/battle/data/ui/platform）
- ✅ TypeScript + ESLint 配置
- ✅ .gitignore + LFS
- ✅ CLAUDE.md（引擎版本/目录约定/三端适配层/数据结构定义/核心架构原则）
- ✅ TypeScript 类型定义：CharacterTypes, RecipeGraph, BattleTypes, PlatformSDK

**Placeholder scan:** 无 TBD/TODO。

**Type consistency:** `RecipeGraph.ts` 中引用 `Character` 和 `Component` 与 `CharacterTypes.ts` 定义一致；`BattleTypes.ts` 中引用 `Attribute` 与 `CharacterTypes.ts` 一致。
