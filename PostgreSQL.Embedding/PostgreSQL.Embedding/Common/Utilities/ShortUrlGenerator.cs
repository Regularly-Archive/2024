using System;
using System.Security.Cryptography;
using System.Text;


namespace PostgreSQL.Embedding.Utils
{

    public class ShortUrlGenerator
    {
        private static readonly char[] Alphabet = "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789".ToCharArray();

        public static string GenerateShortCode(string longUrl)
        {
            using (MD5 md5 = MD5.Create())
            {
                byte[] hashBytes = md5.ComputeHash(Encoding.UTF8.GetBytes(longUrl));
                string hash = BitConverter.ToString(hashBytes).Replace("-", "");

                // 将32位签名串分为4段，每段8个字节
                string[] segments = new string[4];
                for (int i = 0; i < 4; i++)
                {
                    segments[i] = hash.Substring(i * 8, 8);
                }

                // 对每一段进行处理，生成6位的短字符串
                string shortCode;
                for (int i = 0; i < segments.Length; i++)
                {
                    shortCode = ProcessSegment(segments[i]);
                    if (!string.IsNullOrEmpty(shortCode))
                        return shortCode;
                }

                return string.Empty;
            }
        }

        private static string ProcessSegment(string segment)
        {
            // 将 16 进制字符串与 0x3fffffff 进行与操作，忽略超过 30 位的1
            long value = Convert.ToInt64(segment, 16) & 0x3fffffff;

            // 将 30 位分成 6 段，每5位的数字作为字母表的索引
            char[] shortCodeChars = new char[6];
            for (int i = 0; i < 6; i++)
            {
                int index = (int)((value & 0x1f) % Alphabet.Length);
                shortCodeChars[i] = Alphabet[index];
                // 移除已处理的 5 位
                value >>= 5; 
            }

            return new string(shortCodeChars);
        }
    }
}
