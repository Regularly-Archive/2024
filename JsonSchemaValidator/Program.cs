using Microsoft.Json.Schema.Validation;
using Microsoft.Json.Schema;
using Microsoft.CodeAnalysis.Sarif;

namespace JsonSchemaValidator
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var schema = File.ReadAllText("Schema.json");
            var jsonSchema = SchemaReader.ReadSchema(schema, string.Empty);
            var validator = new Validator(jsonSchema);

            var payload = File.ReadAllText("Sample.json");
            var results = validator.Validate(payload, string.Empty);
            var messages = results.Select(r => r.FormatForVisualStudio(RuleFactory.GetRuleFromRuleId(r.RuleId))).ToList();
            foreach (var message in messages)
            {
                Console.WriteLine(message);
            }

            Console.ReadKey();
        }
    }
}


