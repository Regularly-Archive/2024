using System.Linq;
using static PostgreSQL.Embedding.Common.Converters.EnumValuesConverter;
using System.Reflection;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Common.Converters
{
    public class EnumValuesConverter
    {
        public record EnumValueDescriptor(string Label, int Value);

        public IEnumerable<EnumValueDescriptor> Convert(Type enumType)
        {
            return enumType
                .GetFields(BindingFlags.Static | BindingFlags.Public)
                .Select(field =>
                {
                    var value = (int)field.GetValue(null);
                    var attributes = field.GetCustomAttributes(typeof(DescriptionAttribute), false);
                    string description = attributes.Length > 0 ? ((DescriptionAttribute)attributes[0]).Description : field.Name;
                    return new EnumValueDescriptor(description, value);
                });
        }
    }
}
