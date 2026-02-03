using SqlSugar;

namespace PostgreSQL.Embedding.Domain.Entities
{

    [SugarTable("sys_users")]
    public class SystemUser : BaseEntity
    {
        [SugarColumn(ColumnName = "user_name", ColumnDataType = "varchar(32)")]
        public string UserName { get; set; }

        [SugarColumn(ColumnName = "password", ColumnDataType = "varchar(255)")]
        public string Password { get; set; }

        [SugarColumn(ColumnName = "avatar", ColumnDataType = "varchar(255)", IsNullable = true)]
        public string Avatar { get; set; }

        [SugarColumn(ColumnName = "email", ColumnDataType = "varchar(32)", IsNullable = true)]
        public string Email { get; set; }

        [SugarColumn(ColumnName = "mobile", ColumnDataType = "varchar(32)", IsNullable = true)]
        public string Mobile { get; set; }

        [SugarColumn(ColumnName = "nick_name", ColumnDataType = "varchar(32)", IsNullable = true)]
        public string NickName { get; set; }

        [SugarColumn(ColumnName = "intro", IsNullable = true)]
        public string Intro { get; set; }

        [SugarColumn(ColumnName = "gender")]
        public int Gender { get; set; }

        /// <summary>
        /// 角色：SA=管理员，User=普通用户
        /// </summary>
        [SugarColumn(ColumnName = "role", ColumnDataType = "varchar(32)", DefaultValue = "User")]
        public string Role { get; set; } = "User";
    }
}
