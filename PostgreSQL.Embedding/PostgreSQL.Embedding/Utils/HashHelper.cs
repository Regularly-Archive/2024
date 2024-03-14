using System.Security.Cryptography;
using System.Text;

namespace PostgreSQL.Embedding.Utils
{
    public class HashHelper
    {
        public static string GetFileSHA256(string filePath)
        {
            using var sha256 = SHA256.Create();
            using var fs = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            byte[] hashBytes = sha256.ComputeHash(fs);
            var builder = new StringBuilder();
            for (var i = 0; i < hashBytes.Length; i++)
            {
                builder.Append(hashBytes[i].ToString("x2"));
            }

            return builder.ToString();
        }
    }
}
