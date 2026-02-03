namespace PostgreSQL.Embedding.Infrastructure.UserIdentity
{
    /// <summary>
    /// 用户身份信息（不含敏感信息）
    /// </summary>
    public class UserIdentityInfo
    {
        public long Id { get; set; }
        public string UserName { get; set; } = string.Empty;
        public string? NickName { get; set; }
        public string? Avatar { get; set; }
        public string? Intro { get; set; }
        public int Gender { get; set; }
        public string Role { get; set; } = string.Empty;
    }
}
