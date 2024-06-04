namespace PostgreSQL.Embedding.Common.Models.User
{
    public class UpdateProfileRequest
    {
        public long Id { get; set; }
        public string NickName { get; set; }
        public string Intro { get; set; }
        public int Gender { get; set; }
    }
}
