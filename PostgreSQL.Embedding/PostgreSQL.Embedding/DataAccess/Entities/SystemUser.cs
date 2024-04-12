using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{

    [SugarTable("sys_user")]
    public class SystemUser : BaseEntity
    {
        [SugarColumn(ColumnName = "user_name", ColumnDataType = "varchar(32)")]
        public string UserName { get; set; }

        [SugarColumn(ColumnName = "password", ColumnDataType = "varchar(255)")]
        public string Password { get; set; }

        [SugarColumn(ColumnName = "avatar", ColumnDataType = "varchar(255)", IsNullable = true)]
        public string Avatar { get; set; }
    }
}
