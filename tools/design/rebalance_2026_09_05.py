# -*- coding: utf-8 -*-
"""字表平衡重做(2026-09-05):60 字换载体,不召回(唯一例外 浴→活)。
数值一律从 §1.4 锚点 + §1.4.1 价目表 × 档位系数 K 推出,不手填。"""
ANCHOR = {  # 档: 单攻 全体 护盾 治疗 召数 召血 召攻 流血 护甲
    '白': (60, 30, 40, 40, 1, 40, 20, 10, 3),
    '绿': (90, 50, 70, 60, 1, 60, 30, 10, 4),
    '蓝': (130, 70, 100, 80, 1, 80, 40, 20, 5),
    '紫': (200, 100, 150, 120, 1, 220, 70, 20, 7),
    '金': (400, 200, 300, 240, 2, 220, 90, 40, 9),
    '橙': (480, 240, 360, 280, 3, 260, 100, 50, 11),
    '红': (600, 300, 450, 350, 4, 300, 120, 60, 13),
}
ORDER = ['白','绿','蓝','紫','金','橙','红']
QUOTA = {'白':(0,1),'绿':(1,1),'蓝':(1,2),'紫':(2,2),'金':(2,2),'橙':(2,3),'红':(3,3)}
K = {'白':1.00,'绿':1.00,'蓝':0.90,'紫':0.80,'金':0.65,'橙':0.55,'红':0.45}
SHIELD_F, HEAL_F, GROUP_F = 0.65, 1.00, 0.60
# 召唤物基础攻系数:2026-09-05 用 tools/balance 实测定标 ——
# ×1.00 木系均卒层 22.4(远超水 15.6 / 土 17.4),×0.70 → 15.9 落在两者之间,×0.55 → 12.8 过头。
SUM_ATK_F = 0.70
# 攻血转换率(2026-09-05):召唤字可把**锚点攻击**按 1 攻 = 3 血 转成血量。
# 比例只取三档 {0, 50%, 100%},便于读牌。r=100%(纯肉盾)时被动**免计价** ——
# 它已经把全部攻击预算付出去了,再收特性费是双重收费。
# 1:3 是从现行肉盾字反推的:碉 放弃 20 攻 → +80 血(1:4)、堡 放弃 40 → +160(1:4)、
# 柘 放弃 45 → +110(1:2.4)、桂 放弃 50 → +130(1:2.6);1:3 是这四个的中点。
ATK_TO_HP = 3

PRICE = {
 '冻结1':0.35,'冻结2':0.55,'减速2':0.30,'致盲':0.30,'反伤50':0.35,'破甲':0.33,
 '暴击20':0.40,'增攻50':0.45,'AP+1':0.50,'驱散1':0.15,'驱散全部':0.35,'净化':0.25,
 '免疫1':0.35,'免疫2':0.60,'复活1':0.40,'残血加伤':0.15,'斩杀':0.20,
 '灼烧增威10':0.30,'灼烧增威20':0.45,'穿透20':0.35,'穿透30':0.45,
 '立即结算灼烧':0.15,'引爆':0.30,'全体引爆':0.40,'偷袭':0.25,'分2段':0.20,
 '对灼烧':0.25,'对流血':0.25,'对控制':0.25,'对破甲':0.25,'流血':0.25,
 '战意+2':0.20,'战意+3':0.30,'护甲':0.33,'免一次清盾':0.20,
 '溅射':0.20,'贯穿':0.20,'横扫':0.30,'连发2':0.15,'弹射3':0.25,'终极技':0.50,
 '嘲讽':0.30,'荆棘':0.30,'迅捷':0.20,'自愈':0.25,'命中挂灼烧':0.25,'命中冻结':0.20,
 '光环攻':0.35,'光环盾':0.35,   # 2026-09-05 起含自己(只数收归 1 只后,只加别人会空转)
 '叠战意':0.20,'随行治疗':0.30,'闪避':0.25,'远程':0.25,
 '入场护盾':0.25,
}
FORM_MOD = {'群疗','群盾','持续治疗'}      # 形态,不是特性
MARK_TRAIT = {'金':'叠战意','土':'入场护盾'}  # 系印记(召唤字侧),免配额免计价

R=[]
# 召唤只数(2026-09-05 用户裁定):除 森(2)与 𣛧(3)外一律 1 只。
# 只数变了按**总量守恒**折算单只血/攻 —— 沿用 2026-09-04「只数 2→1,血/攻 ×2」那次的口径。
def C(i,el,ra,form,traits,note='',tank=0,count=1):
    R.append(dict(id=i,el=el,ra=ra,form=form,traits=traits,note=note,tank=tank,count=count))

# 🟡 金 12 —— 主攻击 · 攻击字自带战意(印记免第 1 层)
C('锥','金','白','sum',['连发2','叠战意'],'金系召唤靠 onHitMorale 拿印记')
C('利','金','绿','atk',['AP+1'])
C('锐','金','绿','atk',['穿透20'],'穿透梯队·低')
C('剑','金','蓝','sum',['横扫','叠战意'])
C('剿','金','蓝','atk',['残血加伤'],'斩杀梯队·低')
C('锋','金','蓝','atk',['暴击20'])
C('铡','金','紫','atk',['斩杀','对流血'],'斩杀梯队·中')
C('剁','金','紫','atk',['分2段','流血'],'给铡喂流血')
C('鍂','金','金','atk',['穿透30','分2段'],'穿透梯队·中;补 180 断层')
C('鑫','金','橙','atk',['暴击20','增攻50'])
C('刲','金','橙','atk',['斩杀','偷袭','分2段'],'斩杀梯队·高')
C('䥱','金','红','atk',['穿透30','战意+3','对破甲'],'穿透梯队·高')

# 🟢 木 13 —— 主召唤 · 每只召唤物必带被动(印记免第 1 条)
C('枪','木','白','sum',['贯穿'],'被动=印记')
C('藤','木','绿','sum',['嘲讽'],'嘲讽梯队·低',tank=100)
C('葬','木','蓝','aoe',['光环攻'],'非召唤木字→召唤协同')
C('箭','木','紫','atk',['贯穿','光环攻'],'同上')
C('楸','木','紫','sum',['远程','命中挂灼烧'])
C('荆','木','紫','sum',['荆棘','嘲讽'],'嘲讽梯队·中',tank=100)
C('桤','木','紫','sum',['迅捷','随行治疗'])
C('林','木','金','sum',['迅捷','自愈'])
C('柘','木','金','sum',['嘲讽','荆棘'],'嘲讽梯队·高',tank=100)
C('森','木','橙','sum',['荆棘','迅捷','自愈'],count=2)
C('桂','木','橙','sum',['光环盾','自愈'],'边缘辅助:放大全场召唤物',tank=50)
C('藻','木','橙','sum',['光环攻','自愈'],'同上')
C('𣛧','木','红','sum',['光环攻','迅捷','荆棘'],count=3)

# 🔵 水 12 —— 主治疗 · 治疗即攒泉(引擎自动,不占字面)
C('冻','水','白','dual_h',['冻结1'],'控制梯队·低')
C('海','水','绿','dual_h',['群疗','驱散1'])
C('活','水','蓝','dual_h',['复活1'],'召回,替下 浴')
C('溃','水','蓝','dual_h',['终极技'],'涌泉梯队·低')
C('冷','水','蓝','dual_h',['减速2','偷袭'],'控制梯队·中')
C('澡','水','紫','dual_h',['净化','弹射3'])
C('湮','水','紫','dual_h',['驱散全部','对控制'])
C('冰','水','金','dual_h',['终极技','对控制'],'涌泉梯队·中')
C('沐','水','金','dual_h',['持续治疗','净化','复活1'])
C('淼','水','橙','dual_h',['冻结2','对控制'],'控制梯队·高')
C('淋','水','橙','dual_h',['群疗','净化','减速2'])
C('㵘','水','红','dual_h',['终极技','冻结2','对控制'],'涌泉梯队·高')

# 🔴 火 12 —— 主爆发 · 攻击即挂 DOT(印记免第 1 层)
C('灭','火','白','aoe',['驱散全部'],'语义例外:不挂 DOT')
C('热','火','绿','atk',['灼烧1'],'灼烧梯队·低(印记那一层)')
C('爆','火','蓝','aoe',['全体灼烧2'],'铺:给炸喂层')
C('炸','火','蓝','aoe',['全体引爆'],'引爆梯队·低;语义例外:不挂 DOT')
C('烈','火','紫','aoe',['全体灼烧2','对灼烧'])
C('燥','火','紫','atk',['灼烧2','引爆'],'引爆梯队·中')
C('蒸','火','紫','atk',['灼烧2','灼烧增威10'],'边缘辅助:抬引爆当量')
C('炎','火','金','atk',['灼烧3','对灼烧'],'灼烧梯队·中')
C('灿','火','金','atk',['灼烧2','偷袭'])
C('焚','火','橙','aoe',['全体灼烧4','对灼烧'],'灼烧梯队·高')
C('焱','火','橙','aoe',['全体灼烧3','灼烧增威20'])
C('燚','火','红','aoe',['全体灼烧5','全体引爆','对灼烧'],'引爆梯队·高')

# 🟤 土 11 —— 主护盾 · 护盾即攒厚(引擎自动)
C('碉','土','白','sum',['荆棘','入场护盾'],tank=100)
C('垒','土','绿','dual_s',['护甲'],'护甲梯队·低')
C('壁','土','绿','dual_s',['反伤50'])
C('堡','土','蓝','sum',['荆棘','嘲讽','入场护盾'],tank=100)
C('崩','土','蓝','dual_s',['终极技','群盾'],'厚积薄发·低')
C('碎','土','蓝','dual_s',['破甲'],'破甲梯队·低')
C('塔','土','紫','sum',['迅捷','光环盾','入场护盾'],tank=50)
C('圭','土','金','dual_s',['终极技','对破甲'],'厚积薄发·中')
C('杜','土','金','dual_s',['免疫1','护甲'],'护甲梯队·中高')
C('垚','土','橙','dual_s',['破甲','对破甲','护甲'],'护甲梯队·高')
C('㙓','土','红','dual_s',['终极技','免一次清盾','护甲'],'厚积薄发·高')

import re
def dot_equiv(n): return n*(n+1)//2*20
def parse_burn(t):
    m=re.fullmatch(r'(全体)?灼烧(\d+)',t)
    return (int(m.group(2)),bool(m.group(1))) if m else None

rows,problems=[],[]
for c in R:
    ra,el,form,k=c['ra'],c['el'],c['form'],K[c['ra']]
    A=dict(zip('单攻 全体 护盾 治疗 召数 召血 召攻 流血 护甲'.split(),ANCHOR[ra]))
    ts=[t for t in c['traits'] if t not in FORM_MOD]
    mark=MARK_TRAIT.get(el)
    if mark and mark in ts: ts.remove(mark)          # 系印记:免配额免计价
    lo,hi=QUOTA[ra]
    if not (lo<=len(ts)<=hi): problems.append(f"{c['id']}({ra}) {len(ts)} 条 vs 配额 {lo}~{hi}:{ts}")

    ratio,dot_abs,burn='' ,0,''
    ratio=0.0
    for t in ts:
        b=parse_burn(t)
        if b:
            n,all_=b
            dot_abs+=int(round((dot_equiv(n)-dot_equiv(1))*k))   # 印记免第 1 层;DOT 同样吃档位系数
            burn=f"{'全体' if all_ else ''}灼烧 {n}"
            continue
        if t not in PRICE: problems.append(f"{c['id']}: {t} 没有定价"); continue
        ratio+=PRICE[t]*k
    if el=='木' and form=='sum' and ts:
        ratio-=PRICE.get(ts[0],0)*k   # 木系印记:第 1 条被动免计价(但占配额)
    if not burn:
        for t in c['traits']:
            b=parse_burn(t)
            if b: burn=f"{'全体' if b[1] else ''}灼烧 {b[0]}"

    def bud(base): return max(0,int(round(base*(1-ratio)))-dot_abs)
    atk=sh=hl=sm=ar=ul=''
    if form=='atk': atk=bud(A['单攻'])
    elif form=='aoe': atk=bud(A['全体'])
    elif form=='sum':
        r=c['tank']/100.0
        keep = 0.0 if c['tank']==100 else ratio      # 纯肉盾:被动免计价
        n=c['count']
        # 总量守恒:锚点是「只数 × 单只」的总预算,只数改了就把总量摊到新的只数上
        hp_tot=A['召数']*A['召血']; at_tot=A['召数']*A['召攻']
        hp=int(round((hp_tot/n+at_tot/n*ATK_TO_HP*r)*(1-keep)))
        at=int(round(at_tot/n*(1-r)*SUM_ATK_F*(1-keep)))
        sm=f"{n} 只 · {hp} 血 / {at} 攻"
    elif form=='dual_s':
        g=GROUP_F if '群盾' in c['traits'] else 1.0
        atk=bud(A['全体'] if g<1 else A['单攻'])
        sh=int(round(A['护盾']*SHIELD_F*g*(1-ratio)))
    elif form=='dual_h':
        g=GROUP_F if '群疗' in c['traits'] else 1.0
        atk=bud(A['全体'] if g<1 else A['单攻'])
        hl=int(round(A['治疗']*HEAL_F*g*(1-ratio)))
        if '持续治疗' in c['traits']: hl=f"{hl//3}×3"
    if '护甲' in ts: ar=A['护甲']
    if '流血' in ts: pass
    if '终极技' in ts: ul=A['全体']//5
    rows.append(dict(**c,atk=atk,sh=sh,hl=hl,sm=sm,ar=ar,ul=ul,burn=burn,ratio=ratio,dot=dot_abs,ts=ts))

# 档位单调性:同系同形态,高档不得低于低档
def total(r):
    v=r['atk'] if isinstance(r['atk'],int) else 0
    for t in r['traits']:
        b=parse_burn(t)
        if b: v+=dot_equiv(b[0])
    if isinstance(r['ul'],int): v+=r['ul']*10        # 终极技满 10 层
    if isinstance(r['sm'],str) and r['sm']:
        # 召唤当量沿用 tools/balance 的 Power() 口径:(血 + 攻×3) × 只数 ÷ 2。
        # 只按攻击算会把肉盾字判成倒挂(桂 3×30 攻 < 林 2×53 攻,而 桂 血量是 林 的 1.9 倍)。
        import re as _r; m=_r.findall(r'(\d+)',r['sm'])
        v+=(int(m[1])+int(m[2])*3)*int(m[0])//2
    return v
def mono(col,label,fn=None):
    bad=[]
    for el in '金木水火土':
        pass
    for el,fm in [(e,f) for e in '金木水火土' for f in ('atk','aoe','sum','dual_s','dual_h')]:
        best={}
        for r in [x for x in rows if x['el']==el and x['form']==fm
                  and '终极技' not in x['traits']
                  and not ({'群疗','群盾'} & set(x['traits']))]:
            v=fn(r) if fn else (r[col] if isinstance(r[col],int) else None)
            if not v: continue
            best.setdefault(ORDER.index(r['ra']),[]).append((r['id'],v))
        tiers=sorted(best)
        for a,b in zip(tiers,tiers[1:]):
            hi_a=max(v for _,v in best[a]); lo_b=min(v for _,v in best[b])
            if lo_b<hi_a:
                nb=[i for i,v in best[b] if v==lo_b][0]; na=[i for i,v in best[a] if v==hi_a][0]
                bad.append(f"{el}系·{fm} {label}:{ORDER[b]}档 {nb} {lo_b} < {ORDER[a]}档 {na} {hi_a}")
    return bad
issues=mono(None,'总当量',total)+mono('hl','治疗')+mono('sh','护盾')
issues+=[f"{r['id']}({r['ra']}) 预算 {r['ratio']:.2f} 超 0.60 上限" for r in rows if r['ratio']>0.601]
if problems: print('⚠ 配额:'); [print('  ',p) for p in problems]
if issues: print('⚠ 档位倒挂:'); [print('  ',i) for i in issues]
print()
h=['字','系','档','攻击','护盾','治疗','护甲','召唤','终极技/层','灼烧','特性技能','预算']
print('| '+' | '.join(h)+' |'); print('|'+'---|'*len(h))
EL={'金':0,'木':1,'水':2,'火':3,'土':4}
for r in sorted(rows,key=lambda r:(EL[r['el']],ORDER.index(r['ra']))):
    print('| '+' | '.join(str(x) if x!='' else '—' for x in
      [r['id'],r['el'],r['ra'],r['atk'],r['sh'],r['hl'],r['ar'],r['sm'],r['ul'],r['burn'],
       ' / '.join(r['ts']) or '—', f"{r['ratio']:.2f}"+(f"+D{r['dot']}" if r['dot'] else '')])+' |')
print(f"\n共 {len(rows)} 字")
