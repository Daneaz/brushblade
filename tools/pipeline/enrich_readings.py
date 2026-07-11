"""拼音/释义充实:新华字典数据(data/raw/xinhua_word.json)→ 字 → (拼音, 短释义)。

数据源:pwxcoo/chinese-xinhua(开发期用;正式发行需换有明确授权的字典源)。
"""
import re

_GLOSS_MAX = 24

# 新华数据的拼音混用 IPA 小写脚本 g(ɡ),归一为普通 g
_PINYIN_FIXES = {"ɡ": "g"}


def normalize_pinyin(raw):
    """拼音归一:替换异体字母、去首尾空白。"""
    text = (raw or "").strip()
    for bad, good in _PINYIN_FIXES.items():
        text = text.replace(bad, good)
    return text


def short_gloss(explanation):
    """从字典释义抽一句短释义:优先「本义…」句,否则取首个语义片段;截断到 24 字。"""
    text = (explanation or "").strip()
    if not text:
        return ""

    match = re.search(r"本义[::]?(.+?)[。;)]", text)
    if match:
        gloss = match.group(1)
    else:
        # 首片段:跳过开头的字头,按句读/大空白切第一段
        body = re.sub(r"^\S\s+", "", text)
        gloss = re.split(r"[。;,\n]|\s{2,}", body, maxsplit=1)[0]
    gloss = gloss.strip().strip("\"“”「」")
    gloss = gloss.replace("?", "").replace("?", "")  # 数据源夹带的乱码问号
    return gloss[:_GLOSS_MAX]


def readings_map(entries):
    """字典条目列表 → {字: (拼音, 短释义)};无拼音的条目跳过。"""
    result = {}
    for entry in entries:
        pinyin = normalize_pinyin(entry.get("pinyin"))
        if not pinyin:
            continue
        result[entry["word"]] = (pinyin, short_gloss(entry.get("explanation")))
    return result
