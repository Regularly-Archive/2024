namespace PostgreSQL.Embedding.Common.Settings
{
    public class JwtSetting
    {
        public string Secret { get; set; } 
        public string Issuer { get; set; }
        public string Audience { get; set; }
        public TimeSpan Expires { get; set; }
    }
}
