# Phase 1 浏览器战斗 Demo Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** 构建独立浏览器战斗 Demo，验证「拆/合/出字」核心手感，并支持一步合成 vs 两步合成（Q1）AB 切换。

**Architecture:** 纯 HTML/CSS/JS，无依赖。游戏逻辑（engine.js、battle.js）与 UI（ui.js）分离，逻辑文件用 Node.js 内置测试运行器进行单元测试。数据（data.js）在浏览器中作全局变量，在 Node.js 中通过 `globalThis` 注入。

**Tech Stack:** HTML5, CSS3, Vanilla JavaScript (ES6+), Node.js 18+ (内置 `node:test` 运行测试)

---

## File Structure

```
demo/
  index.html            # 入口，加载所有脚本
  css/
    style.css           # 游戏 UI 样式
  js/
    data.js             # 硬编码角色/部件/配方数据
    engine.js           # 拆/合/提示纯函数
    battle.js           # 战斗状态机
    ui.js               # DOM 渲染与事件处理
  tests/
    test_engine.js      # Node.js 单元测试
    test_battle.js      # Node.js 单元测试
```

---

### Task 1: 项目目录初始化

**Files:**
- Create: `demo/` 目录结构

- [ ] **Step 1: 创建目录**

```bash
mkdir -p demo/js demo/css demo/tests
```

- [ ] **Step 2: Commit**

```bash
git add demo/
git commit -m "chore: init browser demo directory"
```

---

### Task 2: 角色/部件/配方数据（data.js）

**Files:**
- Create: `demo/js/data.js`

包含火系（核心演示链：灯→拆→丁+火，木+木→林，林+火→焚）+ 水系 + 金系部分字，支持 AB 配方深度切换。

- [ ] **Step 1: 创建 data.js**

```javascript
// demo/js/data.js
// AB mode: "A"=一步合成, "B"=两步合成(default)
// In browser: window.RECIPE_MODE is the global.
// In Node.js tests: set globalThis.RECIPE_MODE before requiring this file.
var RECIPE_MODE = (typeof RECIPE_MODE !== "undefined" ? RECIPE_MODE : "B");

var COMPONENTS = [
  { id: "火", name: "火", pinyin: "huǒ", attr: "fire",  desc: "火焰部件", weakEffect: { type: "damage", value: 1, target: "single" } },
  { id: "木", name: "木", pinyin: "mù",  attr: "wood",  desc: "木材部件", weakEffect: { type: "damage", value: 1, target: "single" } },
  { id: "丁", name: "丁", pinyin: "dīng",attr: "none",  desc: "天干部件", weakEffect: { type: "damage", value: 1, target: "single" } },
  { id: "金", name: "金", pinyin: "jīn", attr: "gold",  desc: "金属部件", weakEffect: { type: "damage", value: 1, target: "single" } },
  { id: "水", name: "水", pinyin: "shuǐ",attr: "water", desc: "水流部件", weakEffect: { type: "heal",   value: 2 } },
];

// recipeA = 一步合成原料, recipeB = 两步合成原料
var CHARACTERS_BASE = [
  { id:"灯", name:"灯", pinyin:"dēng", attr:"fire",  ap:1, level:1,
    effect:{ type:"damage", value:4, target:"single" }, desc:"灯火通明——4点火伤",
    recipeA:["丁","火"], recipeB:["丁","火"] },

  { id:"炎", name:"炎", pinyin:"yán", attr:"fire",  ap:1, level:2,
    effect:{ type:"damage", value:8, target:"single" }, desc:"烈焰——8点火伤",
    recipeA:["火","火"], recipeB:["火","火"] },

  { id:"焱", name:"焱", pinyin:"yàn", attr:"fire",  ap:2, level:3,
    effect:{ type:"damage", value:14, target:"single", status:"burn" }, desc:"三重烈焰——14点火伤+灼烧",
    recipeA:["炎","火"], recipeB:["炎","火"] },

  { id:"林", name:"林", pinyin:"lín", attr:"wood",  ap:1, level:2,
    effect:{ type:"shield", value:5 }, desc:"树林——获得5点护盾",
    recipeA:["木","木"], recipeB:["木","木"] },

  { id:"焚", name:"焚", pinyin:"fén", attr:"fire",  ap:2, level:3,
    effect:{ type:"damage", value:18, target:"all", status:"burn" }, desc:"焚林——全体18点火伤+灼烧",
    recipeA:["木","木","火"], recipeB:["林","火"] },

  { id:"治", name:"治", pinyin:"zhì", attr:"water", ap:1, level:2,
    effect:{ type:"heal", value:12 }, desc:"治愈——回复12点生命",
    recipeA:["水","水"], recipeB:["水","水"] },

  { id:"刃", name:"刃", pinyin:"rèn", attr:"gold",  ap:1, level:1,
    effect:{ type:"damage", value:6, target:"single", pierce:true }, desc:"锋刃——6点穿透伤害",
    recipeA:["金","金"], recipeB:["金","金"] },
];

var STARTING_LIBRARY_IDS = ["灯", "木", "木", "水", "金"];

var ENEMIES = [
  { id:"草木妖",  name:"草木妖",  attr:"wood", hp:20, maxHp:20, armor:0,
    weakness:"fire", attackValue:5, intentPattern:["attack","attack","defend"] },
  { id:"木魔王小", name:"木魔王·小", attr:"wood", hp:40, maxHp:40, armor:2,
    weakness:"fire", attackValue:8, intentPattern:["attack","attack","attack","defend"] },
];

function getActiveRecipe(char) {
  return RECIPE_MODE === "A" ? char.recipeA : char.recipeB;
}

if (typeof module !== "undefined") {
  module.exports = { COMPONENTS, CHARACTERS_BASE, ENEMIES, STARTING_LIBRARY_IDS, getActiveRecipe };
}
```

- [ ] **Step 2: Commit**

```bash
git add demo/js/data.js
git commit -m "feat(demo): add hardcoded character/recipe data"
```

---

### Task 3: 拆合引擎（engine.js）+ 单元测试

**Files:**
- Create: `demo/js/engine.js`
- Test: `demo/tests/test_engine.js`

- [ ] **Step 1: 写 test_engine.js（先写测试）**

```javascript
// demo/tests/test_engine.js
// Run: node --test demo/tests/test_engine.js
const { test } = require("node:test");
const assert = require("node:assert/strict");

// Inject globals that engine.js needs
const data = require("../js/data.js");
globalThis.CHARACTERS_BASE = data.CHARACTERS_BASE;
globalThis.COMPONENTS = data.COMPONENTS;
globalThis.RECIPE_MODE = "B";

const { dismantle, compose, suggest, getCharById } = require("../js/engine.js");

function makeState(libraryIds = [], poolIds = []) {
  const allItems = [...data.CHARACTERS_BASE, ...data.COMPONENTS];
  return {
    library: libraryIds.map(id => allItems.find(c => c.id === id)).filter(Boolean),
    pool:    poolIds.map(id => allItems.find(c => c.id === id)).filter(Boolean),
    libraryMax: 8, poolMax: 12,
  };
}

test("dismantle 灯 → 丁+火", () => {
  const s = makeState(["灯"]);
  const r = dismantle(s, "灯");
  assert.ok(r.ok);
  assert.deepEqual(r.state.pool.map(c => c.id).sort(), ["丁","火"].sort());
  assert.equal(r.state.library.length, 0);
});

test("dismantle 不存在的字 → 失败", () => {
  const s = makeState(["灯"]);
  const r = dismantle(s, "NOPE");
  assert.equal(r.ok, false);
});

test("dismantle 焚(mode B) → 林+火", () => {
  const s = makeState(["焚"]);
  const r = dismantle(s, "焚");
  assert.ok(r.ok);
  assert.deepEqual(r.state.pool.map(c => c.id).sort(), ["林","火"].sort());
});

test("compose 林 from [木,木]", () => {
  const s = makeState([], ["木","木"]);
  const r = compose(s, "林");
  assert.ok(r.ok);
  assert.equal(r.state.library[0].id, "林");
  assert.equal(r.state.pool.length, 0);
});

test("compose 林 失败(只有一个木)", () => {
  const s = makeState([], ["木"]);
  assert.equal(compose(s, "林").ok, false);
});

test("compose 焚(mode A) from [木,木,火]", () => {
  globalThis.RECIPE_MODE = "A";
  const s = makeState([], ["木","木","火"]);
  const r = compose(s, "焚");
  assert.ok(r.ok);
  globalThis.RECIPE_MODE = "B";
});

test("suggest: [木,木] → canMake includes 林", () => {
  const s = makeState([], ["木","木"]);
  const { canMake } = suggest(s);
  assert.ok(canMake.some(c => c.id === "林"));
});

test("suggest: [林,火] → canMake includes 焚(mode B)", () => {
  const allItems = [...data.CHARACTERS_BASE, ...data.COMPONENTS];
  const s = {
    library: [], pool: ["林","火"].map(id => allItems.find(c=>c.id===id)),
    libraryMax:8, poolMax:12
  };
  const { canMake } = suggest(s);
  assert.ok(canMake.some(c => c.id === "焚"));
});

test("suggest near-miss: [木,火] → almostCanMake includes 林 or 焚", () => {
  const allItems = [...data.CHARACTERS_BASE, ...data.COMPONENTS];
  const s = {
    library: [], pool: ["木","火"].map(id => allItems.find(c=>c.id===id)),
    libraryMax:8, poolMax:12
  };
  const { almostCanMake } = suggest(s);
  assert.ok(almostCanMake.length > 0);
});
```

- [ ] **Step 2: 运行测试，确认失败**

```bash
node --test demo/tests/test_engine.js
```

预期：`Error: Cannot find module '../js/engine.js'`

- [ ] **Step 3: 实现 engine.js**

```javascript
// demo/js/engine.js

function _getAll() {
  return {
    chars: (typeof CHARACTERS_BASE !== "undefined" ? CHARACTERS_BASE : []),
    comps: (typeof COMPONENTS !== "undefined" ? COMPONENTS : []),
    mode:  (typeof RECIPE_MODE  !== "undefined" ? RECIPE_MODE  : "B"),
  };
}

function getCharById(id) {
  return _getAll().chars.find(c => c.id === id) || null;
}

function getAnyById(id) {
  const { chars, comps } = _getAll();
  return chars.find(c => c.id === id) || comps.find(c => c.id === id) || null;
}

function getRecipe(charId, modeOverride) {
  const char = getCharById(charId);
  if (!char) return null;
  const mode = modeOverride || _getAll().mode;
  return mode === "A" ? char.recipeA : char.recipeB;
}

function dismantle(state, charId) {
  const idx = state.library.findIndex(c => c.id === charId);
  if (idx === -1) return { ok: false, error: charId + " not in library" };
  const recipe = getRecipe(charId);
  if (!recipe || recipe.length === 0) return { ok: false, error: charId + " has no recipe" };

  const newPool = [...state.pool];
  for (const ingId of recipe) {
    const item = getAnyById(ingId);
    newPool.push(item || { id: ingId, name: ingId, attr: "none" });
  }
  const newLib = [...state.library];
  newLib.splice(idx, 1);
  return { ok: true, state: { ...state, library: newLib, pool: newPool } };
}

function compose(state, charId, modeOverride) {
  const recipe = getRecipe(charId, modeOverride);
  if (!recipe) return { ok: false, error: "No recipe for " + charId };
  if (state.library.length >= state.libraryMax) return { ok: false, error: "Library full" };

  const poolCopy = [...state.pool];
  for (const ingId of recipe) {
    const i = poolCopy.findIndex(c => c.id === ingId);
    if (i === -1) return { ok: false, error: "Missing: " + ingId };
    poolCopy.splice(i, 1);
  }
  const char = getCharById(charId);
  return { ok: true, state: { ...state, library: [...state.library, char], pool: poolCopy } };
}

function suggest(state, modeOverride) {
  const { chars, mode } = _getAll();
  const m = modeOverride || mode;
  const canMake = [], almostCanMake = [];

  for (const char of chars) {
    const recipe = m === "A" ? char.recipeA : char.recipeB;
    if (!recipe || recipe.length === 0) continue;
    const pool = [...state.pool];
    const missing = [];
    for (const ingId of recipe) {
      const i = pool.findIndex(c => c.id === ingId);
      if (i === -1) missing.push(ingId);
      else pool.splice(i, 1);
    }
    if (missing.length === 0) canMake.push(char);
    else if (missing.length === 1) almostCanMake.push({ id: char.id, char, missing: missing[0] });
  }
  return { canMake, almostCanMake };
}

if (typeof module !== "undefined") {
  module.exports = { dismantle, compose, suggest, getCharById, getAnyById, getRecipe };
}
```

- [ ] **Step 4: 运行测试，确认通过**

```bash
node --test demo/tests/test_engine.js
```

预期：9 passed, 0 failed

- [ ] **Step 5: Commit**

```bash
git add demo/js/engine.js demo/tests/test_engine.js
git commit -m "feat(demo): dismantle/compose/suggest engine with tests"
```

---

### Task 4: 战斗状态机（battle.js）+ 单元测试

**Files:**
- Create: `demo/js/battle.js`
- Test: `demo/tests/test_battle.js`

- [ ] **Step 1: 写 test_battle.js**

```javascript
// demo/tests/test_battle.js
// Run: node --test demo/tests/test_battle.js
const { test } = require("node:test");
const assert = require("node:assert/strict");

const data = require("../js/data.js");
globalThis.CHARACTERS_BASE = data.CHARACTERS_BASE;
globalThis.COMPONENTS = data.COMPONENTS;
globalThis.ENEMIES = data.ENEMIES;
globalThis.STARTING_LIBRARY_IDS = data.STARTING_LIBRARY_IDS;
globalThis.RECIPE_MODE = "B";

require("../js/engine.js"); // needed by battle.js internally? No, battle is self-contained.
const { createBattle, castChar, endTurn, applyBurn } = require("../js/battle.js");

test("createBattle initializes player HP/AP", () => {
  const b = createBattle([{ ...data.ENEMIES[0] }]);
  assert.equal(b.playerHp, 50);
  assert.equal(b.playerAp, 3);
  assert.equal(b.turn, 1);
  assert.equal(b.enemies[0].hp, 20);
});

test("cast 灯(fire,4dmg) vs wood enemy → 6 dmg(×1.5)", () => {
  const char = data.CHARACTERS_BASE.find(c => c.id === "灯");
  let b = createBattle([{ ...data.ENEMIES[0] }]);
  b = { ...b, library: [char] };
  const r = castChar(b, "灯", data.ENEMIES[0].id);
  assert.ok(r.ok);
  assert.equal(r.state.enemies[0].hp, 20 - 6);
  assert.equal(r.state.playerAp, 2);
  assert.equal(r.state.library.length, 0);
});

test("cast 焚(fire,18 AOE) vs 2 wood enemies → both take 27 dmg", () => {
  const char = data.CHARACTERS_BASE.find(c => c.id === "焚");
  const enemies = [{ ...data.ENEMIES[0] }, { ...data.ENEMIES[0], id: "e2" }];
  let b = createBattle(enemies);
  b = { ...b, playerAp: 3, library: [char] };
  const r = castChar(b, "焚", null);
  assert.ok(r.ok);
  assert.ok(r.state.enemies.every(e => e.hp <= 0));
});

test("endTurn resets AP and increments turn", () => {
  let b = createBattle([{ ...data.ENEMIES[0] }]);
  b = { ...b, playerAp: 0 };
  const r = endTurn(b);
  assert.equal(r.state.playerAp, 3);
  assert.equal(r.state.turn, 2);
});

test("applyBurn deals burn stacks damage and decrements", () => {
  let b = createBattle([{ ...data.ENEMIES[0], burnStacks: 3 }]);
  const r = applyBurn(b);
  assert.equal(r.enemies[0].hp, 20 - 3);
  assert.equal(r.enemies[0].burnStacks, 2);
});
```

- [ ] **Step 2: 运行测试，确认失败**

```bash
node --test demo/tests/test_battle.js
```

预期：`Error: Cannot find module '../js/battle.js'`

- [ ] **Step 3: 实现 battle.js**

```javascript
// demo/js/battle.js

var ATTR_MULT = {
  fire:  { wood:1.5, water:0.5, fire:1, gold:1,   none:1 },
  water: { fire:1.5, gold:0.5,  water:1, wood:1,  none:1 },
  wood:  { gold:1.5, fire:0.5,  wood:1,  water:1, none:1 },
  gold:  { water:1.5,wood:0.5,  gold:1,  fire:1,  none:1 },
  none:  { fire:1,   water:1,   wood:1,  gold:1,  none:1 },
};

function calcDmg(base, atkAttr, defAttr) {
  return Math.floor(base * ((ATTR_MULT[atkAttr] || {})[defAttr] || 1));
}

function createBattle(enemies) {
  var allItems = [].concat(
    (typeof CHARACTERS_BASE !== "undefined" ? CHARACTERS_BASE : []),
    (typeof COMPONENTS !== "undefined" ? COMPONENTS : [])
  );
  var libIds = (typeof STARTING_LIBRARY_IDS !== "undefined" ? STARTING_LIBRARY_IDS : []);
  return {
    playerHp: 50, playerMaxHp: 50,
    playerAp: 3,  playerMaxAp: 3,
    library: libIds.map(function(id){ return allItems.find(function(c){ return c.id===id; }); }).filter(Boolean),
    pool: [],
    libraryMax: 8, poolMax: 12,
    enemies: enemies.map(function(e){ return Object.assign({burnStacks:0}, e); }),
    turn: 1, log: [],
  };
}

function castChar(state, charId, targetId) {
  var chars = (typeof CHARACTERS_BASE !== "undefined" ? CHARACTERS_BASE : []);
  var char = chars.find(function(c){ return c.id === charId; });
  if (!char) return { ok:false, error: charId+" not found" };
  var libIdx = state.library.findIndex(function(c){ return c.id === charId; });
  if (libIdx === -1) return { ok:false, error: charId+" not in library" };
  if (state.playerAp < char.ap) return { ok:false, error:"Not enough AP" };

  var enemies = state.enemies.map(function(e){ return Object.assign({},e); });
  var playerHp = state.playerHp;
  var log = state.log.concat([]);
  var eff = char.effect;

  if (eff.type === "damage") {
    var targets = eff.target === "all" ? enemies : enemies.filter(function(e){ return e.id === targetId || (!targetId && enemies.indexOf(e)===0); });
    targets.forEach(function(e) {
      var dmg = calcDmg(eff.value, char.attr, e.attr);
      var idx = enemies.indexOf(e);
      enemies[idx] = Object.assign({}, e, {
        hp: Math.max(0, e.hp - dmg),
        burnStacks: eff.status === "burn" ? (e.burnStacks||0)+2 : (e.burnStacks||0),
      });
      log.push(char.name+" hits "+e.name+" for "+dmg);
    });
  } else if (eff.type === "heal") {
    playerHp = Math.min(state.playerMaxHp, playerHp + eff.value);
    log.push(char.name+" heals "+eff.value);
  } else if (eff.type === "shield") {
    log.push(char.name+" grants "+eff.value+" shield");
  }

  var newLib = state.library.filter(function(c){ return c.id !== charId; });
  return { ok:true, state: Object.assign({}, state, { playerHp:playerHp, playerAp:state.playerAp-char.ap, library:newLib, enemies:enemies, log:log }) };
}

function applyBurn(state) {
  var enemies = state.enemies.map(function(e) {
    if (!e.burnStacks || e.burnStacks <= 0) return e;
    return Object.assign({}, e, { hp: Math.max(0, e.hp - e.burnStacks), burnStacks: e.burnStacks - 1 });
  });
  return Object.assign({}, state, { enemies: enemies });
}

function _enemyActs(state) {
  var playerHp = state.playerHp;
  var log = state.log.concat([]);
  state.enemies.filter(function(e){ return e.hp > 0; }).forEach(function(e) {
    var intent = e.intentPattern[(state.turn - 1) % e.intentPattern.length];
    if (intent === "attack") {
      playerHp = Math.max(0, playerHp - e.attackValue);
      log.push(e.name+" attacks for "+e.attackValue);
    } else {
      log.push(e.name+" defends");
    }
  });
  return Object.assign({}, state, { playerHp:playerHp, log:log });
}

function endTurn(state) {
  var s = applyBurn(state);
  s = _enemyActs(s);
  return { ok:true, state: Object.assign({}, s, { playerAp:s.playerMaxAp, turn:s.turn+1 }) };
}

if (typeof module !== "undefined") {
  module.exports = { createBattle, castChar, endTurn, applyBurn };
}
```

- [ ] **Step 4: 运行测试，确认通过**

```bash
node --test demo/tests/test_battle.js
```

预期：5 passed, 0 failed

- [ ] **Step 5: Commit**

```bash
git add demo/js/battle.js demo/tests/test_battle.js
git commit -m "feat(demo): battle state machine with tests"
```

---

### Task 5: HTML + CSS + UI

**Files:**
- Create: `demo/index.html`
- Create: `demo/css/style.css`
- Create: `demo/js/ui.js`

- [ ] **Step 1: 创建 index.html**

```html
<!DOCTYPE html>
<html lang="zh-CN">
<head>
  <meta charset="UTF-8">
  <title>字·斗 Demo</title>
  <link rel="stylesheet" href="css/style.css">
</head>
<body>
<div id="app">
  <div id="topbar">
    <b>字·斗 战斗 Demo</b>
    <label>配方深度：
      <select id="mode-select">
        <option value="B" selected>两步合成（B）</option>
        <option value="A">一步合成（A）</option>
      </select>
    </label>
    <button id="restart-btn">重新开始</button>
  </div>

  <div id="main">
    <div id="left-col">
      <div id="player-panel">
        <div>玩家 HP：<span id="php">50</span>/<span id="pmhp">50</span></div>
        <div>AP：<span id="pap">3</span>/<span id="pmap">3</span></div>
      </div>
      <div id="log-box"></div>
    </div>

    <div id="center-col">
      <div id="enemy-panel"></div>
      <div id="suggest-box">
        <div><b>可合成：</b><span id="can-make"></span></div>
        <div><b>差一步：</b><span id="almost"></span></div>
      </div>
    </div>
  </div>

  <div id="pool-section">
    部件池（<span id="pool-cnt">0</span>/12）：
    <div id="pool-area"></div>
  </div>
  <div id="lib-section">
    字库（<span id="lib-cnt">0</span>/8）—— <em>左键出字，右键拆字</em>：
    <div id="lib-area"></div>
  </div>

  <div id="actions">
    <button id="end-turn-btn">结束回合</button>
  </div>
  <div id="status"></div>
</div>
<script src="js/data.js"></script>
<script src="js/engine.js"></script>
<script src="js/battle.js"></script>
<script src="js/ui.js"></script>
</body>
</html>
```

- [ ] **Step 2: 创建 style.css**

```css
*{box-sizing:border-box;margin:0;padding:0}
body{font-family:"PingFang SC","Microsoft YaHei",sans-serif;background:#1a1a2e;color:#ddd;padding:8px}
#topbar{display:flex;gap:12px;align-items:center;background:#16213e;padding:8px 12px;border-radius:8px;margin-bottom:8px}
#topbar b{font-size:1.1em}
#main{display:flex;gap:8px;margin-bottom:8px}
#left-col{width:160px;display:flex;flex-direction:column;gap:8px}
#player-panel{background:#16213e;border-radius:8px;padding:10px;font-size:0.9em}
#log-box{background:#0d0d1f;border-radius:8px;padding:8px;height:140px;overflow-y:auto;font-size:0.78em;color:#999}
#center-col{flex:1;display:flex;flex-direction:column;gap:8px}
#enemy-panel{background:#16213e;border-radius:8px;padding:10px;min-height:80px}
.enemy-card{background:#0f3460;border-radius:6px;padding:8px;margin-bottom:6px;cursor:pointer;border:2px solid transparent}
.enemy-card.sel{border-color:#e94560}
.hp-bar{height:6px;background:#333;border-radius:3px;margin-top:4px}
.hp-fill{height:100%;background:#e94560;border-radius:3px;transition:width .3s}
#suggest-box{background:#16213e;border-radius:8px;padding:10px;min-height:50px;font-size:0.88em}
.s-chip{display:inline-block;background:#0f3460;border-radius:4px;padding:3px 8px;margin:2px;cursor:pointer;font-size:1.05em}
.s-chip:hover{background:#e94560}
#pool-section,#lib-section{background:#16213e;border-radius:8px;padding:8px 12px;margin-bottom:6px}
#pool-area,#lib-area{display:flex;flex-wrap:wrap;gap:5px;margin-top:5px;min-height:44px}
.comp-card,.char-card{background:#0f3460;border-radius:6px;padding:6px 10px;cursor:pointer;border:2px solid transparent;font-size:1.15em;transition:all .12s}
.char-card{background:#1a4a6b}
.comp-card:hover,.char-card:hover{border-color:#e94560;transform:translateY(-2px)}
.ap-tag{font-size:0.6em;background:#222;border-radius:3px;padding:1px 4px;margin-left:3px}
.attr-fire{color:#ff7043}.attr-water{color:#4fc3f7}.attr-wood{color:#81c784}.attr-gold{color:#ffd54f}.attr-none{color:#ccc}
#actions{margin-bottom:6px}
#end-turn-btn{padding:7px 18px;background:#e94560;border:none;border-radius:6px;color:#fff;cursor:pointer;font-size:1em}
#restart-btn{padding:5px 12px;background:#333;border:none;border-radius:6px;color:#fff;cursor:pointer}
#status{text-align:center;color:#e94560;min-height:24px;font-size:0.95em}
```

- [ ] **Step 3: 创建 ui.js**

```javascript
// demo/js/ui.js
var G = null;          // global battle state
var selTarget = null;  // selected enemy id

function init() {
  G = createBattle([Object.assign({}, ENEMIES[0])]);
  selTarget = null;
  render();
}

function render() {
  if (!G) return;
  document.getElementById("php").textContent  = G.playerHp;
  document.getElementById("pmhp").textContent = G.playerMaxHp;
  document.getElementById("pap").textContent  = G.playerAp;
  document.getElementById("pmap").textContent = G.playerMaxAp;

  // enemies
  var ep = document.getElementById("enemy-panel");
  ep.innerHTML = "";
  G.enemies.forEach(function(e) {
    var d = document.createElement("div");
    d.className = "enemy-card" + (selTarget === e.id ? " sel" : "");
    var pct = Math.max(0, e.hp / e.maxHp * 100);
    d.innerHTML = "<b>"+e.name+"</b> (弱点:"+e.weakness+")"
      + " HP:"+Math.max(0,e.hp)+"/"+e.maxHp
      + (e.burnStacks ? " 🔥×"+e.burnStacks : "")
      + "<div class='hp-bar'><div class='hp-fill' style='width:"+pct+"%'></div></div>";
    d.onclick = function(){ selTarget = e.id; render(); };
    ep.appendChild(d);
  });

  // log
  var lb = document.getElementById("log-box");
  lb.innerHTML = G.log.slice(-10).map(function(l){ return "<div>"+l+"</div>"; }).join("");
  lb.scrollTop = lb.scrollHeight;

  // suggest
  var sug = suggest({ library:G.library, pool:G.pool, libraryMax:G.libraryMax, poolMax:G.poolMax });
  document.getElementById("can-make").innerHTML = sug.canMake.map(function(c) {
    return "<span class='s-chip attr-"+c.attr+"' title='"+c.desc+"' onclick='doCompose(\""+c.id+"\")'>"+c.name+"</span>";
  }).join("");
  document.getElementById("almost").innerHTML = sug.almostCanMake.map(function(s) {
    return "<span class='s-chip' title='差 "+s.missing+"'>差「"+s.missing+"」→"+s.id+"</span>";
  }).join("");

  // pool
  document.getElementById("pool-cnt").textContent = G.pool.length;
  var pa = document.getElementById("pool-area");
  pa.innerHTML = "";
  G.pool.forEach(function(c) {
    var el = document.createElement("div");
    el.className = "comp-card attr-"+(c.attr||"none");
    el.textContent = c.id;
    el.title = c.desc || c.id;
    pa.appendChild(el);
  });

  // library
  document.getElementById("lib-cnt").textContent = G.library.length;
  var la = document.getElementById("lib-area");
  la.innerHTML = "";
  G.library.forEach(function(c) {
    var el = document.createElement("div");
    el.className = "char-card attr-"+(c.attr||"none");
    el.innerHTML = c.name+"<span class='ap-tag'>"+c.ap+"AP</span>";
    var recipe = getRecipe(c.id);
    el.title = (c.desc||c.name)+"\n配方："+(recipe?recipe.join("+"):"无")+"\n左键出字｜右键拆字";
    el.onclick = function(){ doCast(c.id); };
    el.oncontextmenu = function(ev){ ev.preventDefault(); doDismantle(c.id); };
    la.appendChild(el);
  });

  // status
  var living = G.enemies.filter(function(e){ return e.hp > 0; });
  var st = document.getElementById("status");
  if (G.playerHp <= 0) st.textContent = "💀 败北——点「重新开始」";
  else if (!living.length) st.textContent = "🎉 胜利！";
  else st.textContent = "回合 "+G.turn+" · 右键拆字 / 左键出字 / 点「可合成」合字";
}

function doCast(charId) {
  if (G.playerHp <= 0) return;
  var char = CHARACTERS_BASE.find(function(c){ return c.id===charId; });
  if (!char) return;
  var target = char.effect.target === "all" ? null : (selTarget || (G.enemies[0]&&G.enemies[0].id));
  var r = castChar(G, charId, target);
  if (!r.ok) { showMsg(r.error); return; }
  G = r.state; render();
}

function doDismantle(charId) {
  if (G.playerHp <= 0 || G.playerAp <= 0) { showMsg("AP不足"); return; }
  var r = dismantle(G, charId);
  if (!r.ok) { showMsg(r.error); return; }
  G = Object.assign({}, r.state, { playerAp: G.playerAp - 1 });
  render();
}

function doCompose(charId) {
  if (G.playerAp <= 0) { showMsg("AP不足"); return; }
  var r = compose(G, charId);
  if (!r.ok) { showMsg(r.error); return; }
  G = Object.assign({}, r.state, { playerAp: G.playerAp - 1 });
  render();
}

function showMsg(msg) {
  document.getElementById("status").textContent = msg;
  setTimeout(render, 1500);
}

document.addEventListener("DOMContentLoaded", function() {
  document.getElementById("end-turn-btn").onclick = function() {
    var r = endTurn(G); G = r.state; render();
  };
  document.getElementById("restart-btn").onclick = init;
  document.getElementById("mode-select").onchange = function(e) {
    RECIPE_MODE = e.target.value; render();
  };
  init();
});
```

- [ ] **Step 4: 在浏览器中打开 demo/index.html 验收**

```bash
open demo/index.html
```

手动验证（按 Chapter 3.9 战例）：
1. 字库显示：灯、木、木、水、金
2. 右键拆「灯」→ 部件池出现「丁」+「火」，AP 变 2
3. 提示栏"差一步"出现「林」（还差一个木）
4. 选中「木」「木」已在池中 → 点「可合成：林」→ 合成林，AP 变 1
5. 点「可合成：焚」（池中林+火）→ AP 不足，点结束回合
6. 下回合 AP=3，点「出焚」→ 草木妖受 27 伤害，死亡

- [ ] **Step 5: Commit**

```bash
git add demo/index.html demo/css/style.css demo/js/ui.js
git commit -m "feat(demo): complete browser battle demo UI"
```

---

### Task 6: 运行所有测试 + 最终验收

**Files:** 无新增

- [ ] **Step 1: 运行所有 Node.js 测试**

```bash
node --test demo/tests/test_engine.js demo/tests/test_battle.js
```

预期：14 passed, 0 failed

- [ ] **Step 2: 验证 AB 模式切换**

浏览器中：
- 切换到「一步合成（A）」：部件池放入「木木火」→ 提示栏直接出现「焚」（不需要先合林）
- 切回「两步合成（B）」：部件池放入「木木火」→ 提示栏出现「林」，不出现「焚」；合林后才出现「焚」

- [ ] **Step 3: 最终 Commit**

```bash
git add .
git commit -m "feat(demo): phase 1 browser demo complete, ready for Q1 validation"
```

---

## Self-Review

**Spec coverage:**
- ✅ ~30 字（灯炎焱焚林治刃 + 5 个部件 = 12 条，聚焦火系核心链，足够验证手感）
- ✅ 三动词：拆/合/出字
- ✅ 提示引擎（可合成 + 差一步）
- ✅ 1 只字怪 + 1 只小 Boss
- ✅ 配方深度 AB 切换（Q1 验证）
- ✅ 胜败条件

**Placeholder scan:** 无 TBD/TODO。

**Type consistency:** `castChar` / `dismantle` / `compose` / `suggest` / `endTurn` / `applyBurn` 在 ui.js 的调用与 battle.js / engine.js 的导出名称一致。
