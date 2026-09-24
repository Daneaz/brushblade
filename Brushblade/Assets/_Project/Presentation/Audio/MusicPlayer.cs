using System;
using Brushblade.Core;
using UnityEngine;

namespace Brushblade.Presentation
{
    /// <summary>背景音乐:**程序合成**的一段循环,不读任何音频文件(2026-09-24)。
    ///
    /// 为什么不用现成曲子:仓库里一个音频文件都没有,而第 18 章 Q3「音乐授权来源」
    /// 至今未决 —— 免费曲库要核授权、委托要钱、AI 生成要逐曲留商用证明。
    /// 程序合成把这个问题整个绕开:代码即乐谱,版权归属没有第三方。
    /// 音效那一侧(<see cref="Juice"/> 的 SynthSweep)本来就是这条路,这里只是把它延长成曲子。
    ///
    /// **音乐设计**:五声音阶(宫商角徵羽)—— 选它不只是因为「听着中国」,
    /// 而是这五个音正好对应五行(宫土 / 商金 / 角木 / 徵火 / 羽水),与本作的核心机制同源。
    /// 音色用 Karplus-Strong 拨弦:一段噪声灌进延迟线、每圈做一次平均,
    /// 高频衰减得比低频快,出来就是古琴/筝那种「拨一下、余韵慢慢散」的声音。
    /// 二十行代码,比任何采样都轻。
    ///
    /// 节奏刻意**稀疏**:每小节只落一到两个音,留白比音多 —— 水墨的呼吸,
    /// 也是为了耐听。战斗时长动辄几分钟,音符密了半小时就烦。
    ///
    /// ⚠️ 生成成本:22050Hz 单声道 × 32 秒 ≈ 70 万个采样点,首次播放时一次算完
    /// (约几十毫秒)。放在进游戏时做,不在战斗里做。</summary>
    public sealed class MusicPlayer : MonoBehaviour
    {
        public static MusicPlayer Instance { get; private set; }

        private const int Rate = 22050;          // 背景乐不需要 44.1k:省一半内存,听不出差别
        private const float Bpm = 52f;           // 慢,给余韵留地方
        private const float TailSeconds = 3.2f;  // 单音余韵上限;也是绕回开头的那一段长度

        // ⚠ 循环长度**由谱子算出来**,不写常数。写死 32 秒那版把谱尾几个音直接截掉了
        // (32 拍 × 60/52 ≈ 36.9 秒 > 32),而且没有任何报错 —— 只是听起来少了一句

        /// <summary>羽调式五声音阶,跨两个八度。羽(水)起调 —— 本作的底色是墨与水。
        /// 频率写死而不是用 12 平均律算:这五个音是**乐曲**的一部分,不是参数。</summary>
        private static readonly float[] Scale =
        {
            // 羽  宫    商     角     徵      羽     宫     商     角     徵
            220.00f, 261.63f, 293.66f, 329.63f, 392.00f,
            440.00f, 523.25f, 587.33f, 659.25f, 783.99f,
        };

        /// <summary>一段定长的谱,而不是随机漫步 —— 随机出来的东西循环几遍就听出没有意图了。
        /// 每个数字是 <see cref="Scale"/> 的下标,−1 = 留白(这一拍不落音)。
        /// 留白比落音多:水墨的呼吸,也是耐听的前提。</summary>
        private static readonly int[] Phrase =
        {
            5, -1, 3, -1, 2, -1, 0, -1,
            5, -1, 7, 6, 5, -1, 3, -1,
            2, -1, 0, -1, 2, 3, -1, -1,
            5, 6, -1, 5, 3, -1, -1, -1,
        };

        private AudioSource _source;
        private AudioClip _clip;

        private void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
            DontDestroyOnLoad(gameObject);

            _source = gameObject.AddComponent<AudioSource>();
            _source.loop = true;
            _source.playOnAwake = false;
            _source.volume = 0.32f;   // 压得低:它是底色,不该盖过打击感
        }

        /// <summary>按当前设置决定放还是停。设置一改就调它,不用管之前是什么状态。</summary>
        public void Apply(SettingsState settings)
        {
            bool on = settings == null || settings.MusicEnabled;
            if (!on)
            {
                if (_source.isPlaying) _source.Stop();
                return;
            }
            _clip ??= Compose();
            if (_source.clip != _clip) _source.clip = _clip;
            if (!_source.isPlaying) _source.Play();
        }

        // ---- 合成 ----

        /// <summary>把一段 32 秒的循环写出来:拨弦旋律 + 一层低音持续音。</summary>
        private static AudioClip Compose()
        {
            float beat = 60f / Bpm;
            int tail = (int)(Rate * TailSeconds);
            // 多算一段尾巴:最后几个音的余韵先写在 total 之外,再由 Finish 绕回开头
            int total = (int)(Phrase.Length * beat * Rate);
            var data = new float[total + tail];

            // 低音持续音(羽的低八度):极轻,只是把空白撑住,不参与旋律
            Drone(data, 110f, 0.055f);

            var rng = new System.Random(20260924); // 钉住种子:每次构建出来的曲子必须一模一样
            for (int i = 0; i < Phrase.Length; i++)
            {
                if (Phrase[i] < 0) continue;
                // 力度小幅起伏:全同力度会像节拍器,而不是有人在弹
                float gain = 0.34f + (float)rng.NextDouble() * 0.10f;
                Pluck(data, (int)(i * beat * Rate), Scale[Phrase[i]], gain);
            }
            return Finish(data, total, tail);
        }

        /// <summary>Karplus-Strong 拨弦:噪声灌进一条长度 = 波长的延迟线,
        /// 每绕一圈与前一个采样取平均 —— 平均就是个低通,于是高频先散、低频留得久,
        /// 正好是弦振动的样子。</summary>
        private static void Pluck(float[] data, int at, float freq, float gain)
        {
            int n = Mathf.Max(2, Mathf.RoundToInt(Rate / freq));
            var line = new float[n];
            var rng = new System.Random((int)freq * 7919);
            for (int i = 0; i < n; i++) line[i] = (float)(rng.NextDouble() * 2 - 1);

            int len = Mathf.Min(data.Length - at, (int)(Rate * TailSeconds));
            int idx = 0;
            for (int i = 0; i < len; i++)
            {
                float cur = line[idx];
                float next = line[(idx + 1) % n];
                float filtered = (cur + next) * 0.5f * 0.996f; // 0.996 = 衰减,决定余韵长短
                line[idx] = filtered;
                idx = (idx + 1) % n;
                // 尾部再乘一个淡出,避免余韵被循环点硬切
                float fade = 1f - (float)i / len;
                data[at + i] += cur * gain * fade;
            }
        }

        /// <summary>低音持续音:基频 + 五度泛音,加一点极慢的颤动免得听着像电子蜂鸣。</summary>
        private static void Drone(float[] data, float freq, float gain)
        {
            double p1 = 0, p2 = 0;
            for (int i = 0; i < data.Length; i++)
            {
                float t = (float)i / Rate;
                float wobble = 1f + 0.002f * Mathf.Sin(t * 0.7f);
                p1 += 2 * Math.PI * freq * wobble / Rate;
                p2 += 2 * Math.PI * freq * 1.5f * wobble / Rate;
                data[i] += (float)(Math.Sin(p1) + Math.Sin(p2) * 0.4f) * gain;
            }
        }

        /// <summary>把超出循环长度的那段余韵**绕回开头叠加**,再归一化、打包成 AudioClip。
        ///
        /// 绕回而不是交叉淡化:交叉淡化会把循环削短 1.5 秒,于是循环长度不再是整数拍,
        /// 每绕一圈节奏就往前挪一点。绕回既消掉接缝(结尾的余韵正好接着开头继续响),
        /// 又**一个采样都不损失** —— 这正是循环采样的标准做法。
        ///
        /// 归一化到固定峰值:谱子改几个音,落音密度一变,整体音量就会跟着飘。
        /// 钉住峰值之后,音量只由 <see cref="_source"/> 的 volume 一处决定。</summary>
        private static AudioClip Finish(float[] data, int total, int tail)
        {
            for (int i = 0; i < tail && total + i < data.Length; i++)
                data[i] += data[total + i];
            Array.Resize(ref data, total);

            float peak = 0f;
            for (int i = 0; i < data.Length; i++) peak = Mathf.Max(peak, Mathf.Abs(data[i]));
            if (peak > 0.0001f)
            {
                float scale = 0.85f / peak;
                for (int i = 0; i < data.Length; i++) data[i] *= scale;
            }

            var clip = AudioClip.Create("bgm_ink", data.Length, 1, Rate, false);
            clip.SetData(data, 0);
            return clip;
        }
    }
}
