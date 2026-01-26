namespace PostgreSQL.Embedding.Domain.Models.User
{
    public class LoginRequest
    {
        public string UserName { get; set; }
        public string Password { get; set; }
    }
}
