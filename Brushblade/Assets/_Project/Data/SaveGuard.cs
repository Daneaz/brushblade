using System;
using System.Security.Cryptography;
using System.Text;

namespace Brushblade.Data
{
    /// <summary>存档签名(19.9):HMAC-SHA256 包裹,篡改即拒。
    /// 密钥内嵌仅为混淆——单机防线以「挡住改档器脚本小子」为目标,不承诺不可破。</summary>
    public static class SaveGuard
    {
        private const string Header = "BB1:"; // 格式版本
        private static readonly byte[] Key = Encoding.UTF8.GetBytes("brushblade-zidou-2026-v1");

        /// <summary>包裹:BB1:签名十六进制:payload。</summary>
        public static string Seal(string payload)
        {
            return Header + Sign(payload) + ":" + payload;
        }

        /// <summary>验签拆包;签名不符/格式非法返回 false。</summary>
        public static bool TryOpen(string sealedText, out string payload)
        {
            payload = null;
            if (string.IsNullOrEmpty(sealedText) || !sealedText.StartsWith(Header, StringComparison.Ordinal))
                return false;

            int sigEnd = sealedText.IndexOf(':', Header.Length);
            if (sigEnd < 0)
                return false;

            string signature = sealedText.Substring(Header.Length, sigEnd - Header.Length);
            string body = sealedText.Substring(sigEnd + 1);
            if (!string.Equals(signature, Sign(body), StringComparison.Ordinal))
                return false;

            payload = body;
            return true;
        }

        private static string Sign(string payload)
        {
            using var hmac = new HMACSHA256(Key);
            var hash = hmac.ComputeHash(Encoding.UTF8.GetBytes(payload));
            var text = new StringBuilder(hash.Length * 2);
            foreach (var b in hash)
                text.Append(b.ToString("x2"));
            return text.ToString();
        }
    }
}
