namespace PostgreSQL.Embedding.Common.Attributes
{
    [AttributeUsage(AttributeTargets.Class, AllowMultiple = false)]
    public class KernelPluginAttribute : Attribute
    {
        public string PluginName { get; set; }
        public string Description { get; set; }
    }
}
