using System;
using System.Security.Cryptography;
using System.Text;

namespace XUnity_LLMTranslatePlus.Utils
{
    /// <summary>
    /// 使用 Windows DPAPI 加密敏感数据
    /// </summary>
    public static class SecureDataProtection
    {
        // 标记用于识别加密数据
        private const string EncryptedPrefix = "ENC:";

        /// <summary>
        /// 加密字符串（使用 DPAPI）
        /// </summary>
        /// <param name="plainText">明文</param>
        /// <returns>加密后的 Base64 字符串</returns>
        public static string Protect(string plainText)
        {
            if (string.IsNullOrEmpty(plainText))
                return plainText;

            try
            {
                byte[] plainBytes = Encoding.UTF8.GetBytes(plainText);
                byte[] encryptedBytes = ProtectedData.Protect(
                    plainBytes,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser
                );

                return EncryptedPrefix + Convert.ToBase64String(encryptedBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"数据加密失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 解密字符串（使用 DPAPI）
        /// </summary>
        /// <param name="encryptedText">加密的 Base64 字符串</param>
        /// <returns>解密后的明文</returns>
        public static string Unprotect(string encryptedText)
        {
            if (string.IsNullOrEmpty(encryptedText))
                return encryptedText;

            // 如果没有加密标记，直接返回（向后兼容）
            if (!encryptedText.StartsWith(EncryptedPrefix))
                return encryptedText;

            try
            {
                string base64Data = encryptedText.Substring(EncryptedPrefix.Length);
                byte[] encryptedBytes = Convert.FromBase64String(base64Data);
                byte[] plainBytes = ProtectedData.Unprotect(
                    encryptedBytes,
                    optionalEntropy: null,
                    scope: DataProtectionScope.CurrentUser
                );

                return Encoding.UTF8.GetString(plainBytes);
            }
            catch (Exception ex)
            {
                throw new InvalidOperationException($"数据解密失败: {ex.Message}", ex);
            }
        }

        /// <summary>
        /// 检查字符串是否已加密
        /// </summary>
        public static bool IsEncrypted(string text)
        {
            return !string.IsNullOrEmpty(text) && text.StartsWith(EncryptedPrefix);
        }
    }
}
