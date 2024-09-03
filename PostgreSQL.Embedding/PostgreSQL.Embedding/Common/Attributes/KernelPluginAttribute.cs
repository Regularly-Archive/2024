namespace PostgreSQL.Embedding.Common.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class KernelPluginAttribute : Attribute
    {
        public string Description { get; set; }
        public bool Enabled { get; set; } = true;
    }
}
