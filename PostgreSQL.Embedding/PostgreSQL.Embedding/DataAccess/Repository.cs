using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess
{
    public class Repository<T> : SimpleClient<T> where T : class, new()
    {
        public Repository(ISqlSugarClient db) : base(db)
        {

        }
    }
}
