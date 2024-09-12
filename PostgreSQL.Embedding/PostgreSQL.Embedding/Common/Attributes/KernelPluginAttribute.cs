namespace PostgreSQL.Embedding.Common.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class KernelPluginAttribute : Attribute
    {
        public bool Enabled { get; set; } = true;
        public string Description { get; set; }
        public string Version { get; set; } = "v1.0";
    }
}
