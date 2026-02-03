namespace PostgreSQL.Embedding.Infrastructure.DataAccess
{
    /// <summary>
    /// 数据隔离特性，标记需要进行数据隔离的实体
    /// 被标记的实体在查询时会自动过滤当前用户创建的数据
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = true)]
    public class DataIsolationAttribute : Attribute
    {
        /// <summary>
        /// 使用哪个字段进行数据隔离，默认为 CreatedBy
        /// </summary>
        public string OwnerField { get; set; } = "CreatedBy";
    }
}
