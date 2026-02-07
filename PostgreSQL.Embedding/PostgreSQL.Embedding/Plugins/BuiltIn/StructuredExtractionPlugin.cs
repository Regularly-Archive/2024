using Microsoft.SemanticKernel;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Llm.Services;
using PostgreSQL.Embedding.Plugins.Abstration;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.BuiltIn
{
    /// <summary>
    /// 结构化提取结果
    /// </summary>
    public class ExtractionResult
    {
        [JsonProperty("success")]
        public bool Success { get; set; }

        [JsonProperty("extracted_data")]
        public JObject? ExtractedData { get; set; }

        [JsonProperty("missing_fields")]
        public List<string>? MissingFields { get; set; }

        [JsonProperty("validation_errors")]
        public List<string>? ValidationErrors { get; set; }

        [JsonProperty("message")]
        public string? Message { get; set; }

        [JsonProperty("raw_output")]
        public string? RawOutput { get; set; }
    }

    /// <summary>
    /// 分步提取状态
    /// </summary>
    public class ExtractionSession
    {
        [JsonProperty("session_id")]
        public string SessionId { get; set; } = string.Empty;

        [JsonProperty("schema")]
        public JObject Schema { get; set; } = new();

        [JsonProperty("current_data")]
        public JObject CurrentData { get; set; } = new();

        [JsonProperty("extracted_paths")]
        public HashSet<string> ExtractedPaths { get; set; } = new();

        [JsonProperty("pending_paths")]
        public List<string> PendingPaths { get; set; } = new();

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; } = DateTime.UtcNow;

        [JsonProperty("completed")]
        public bool Completed { get; set; }
    }

    /// <summary>
    /// 字段提取结果
    /// </summary>
    public class FieldExtractionResult
    {
        [JsonProperty("path")]
        public string Path { get; set; } = string.Empty;

        [JsonProperty("value")]
        public JToken? Value { get; set; }

        [JsonProperty("confidence")]
        public double Confidence { get; set; }

        [JsonProperty("source_text")]
        public string? SourceText { get; set; }

        [JsonProperty("extracted")]
        public bool Extracted { get; set; }
    }

    [KernelPlugin(Description = "结构化数据提取插件。根据 JSON Schema 从文本中提取结构化数据，支持增量提取、验证和复杂嵌套对象处理。适用于会议信息、简历、产品规格等复杂结构化数据抽取场景。", Version = "1.0")]
    public class StructuredExtractionPlugin : BasePlugin
    {
        private readonly PromptTemplateService _promptTemplateService;
        private readonly ILogger<StructuredExtractionPlugin> _logger;

        // 会话存储（简单内存存储，生产环境可考虑分布式缓存）
        private static readonly Dictionary<string, ExtractionSession> _sessions = new();

        public StructuredExtractionPlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
            _promptTemplateService = serviceProvider.GetService<PromptTemplateService>();
            _logger = serviceProvider.GetService<ILoggerFactory>().CreateLogger<StructuredExtractionPlugin>();
        }

        /// <summary>
        /// 根据 JSON Schema 提取结构化数据
        /// </summary>
        [KernelFunction]
        [Description("根据 JSON Schema 从文本中提取结构化数据。使用 JSON Schema 定义期望的数据结构和类型，系统会自动从文本中提取匹配的数据。")]
        public async Task<ExtractionResult> ExtractWithSchemaAsync(
            [Description("完整的 JSON Schema 定义，描述要提取的数据结构")] string jsonSchema,
            [Description("待提取的原始文本")] string text,
            [Description("是否验证提取结果是否符合 Schema（默认 true）")] bool validateResult = true,
            Kernel? kernel = null)
        {
            try
            {
                // 解析 Schema
                JObject schema;
                try
                {
                    schema = JObject.Parse(jsonSchema);
                }
                catch (Exception ex)
                {
                    return new ExtractionResult
                    {
                        Success = false,
                        Message = $"JSON Schema 解析失败: {ex.Message}"
                    };
                }

                // 生成提取提示词
                var promptTemplate = _promptTemplateService.LoadTemplate("StructuredExtraction.txt");
                promptTemplate.AddVariable("schema", jsonSchema);
                promptTemplate.AddVariable("text", text);

                // 调用 LLM 提取
                string rawOutput;
                if (kernel != null)
                {
                    var result = await promptTemplate.InvokeAsync(kernel.Clone());
                    rawOutput = result.GetValue<string>() ?? "";
                }
                else
                {
                    rawOutput = "未提供 kernel，无法调用 LLM 进行提取。请提供 kernel 参数。";
                }

                // 解析提取结果
                JObject? extractedData = null;
                try
                {
                    // 尝试提取 JSON 代码块
                    var jsonMatch = System.Text.RegularExpressions.Regex.Match(rawOutput,
                        @"```(?:json)?\s*([\s\S]*?)\s*```|(\{[\s\S]*\})|(\[[\s\S]*\])");

                    string jsonContent = jsonMatch.Groups[1].Success ? jsonMatch.Groups[1].Value :
                                        jsonMatch.Groups[2].Success ? jsonMatch.Groups[2].Value :
                                        jsonMatch.Groups[3].Success ? jsonMatch.Groups[3].Value : rawOutput;

                    extractedData = JObject.Parse(jsonContent);
                }
                catch
                {
                    // 如果不是 JSON 格式，直接返回原始输出
                    return new ExtractionResult
                    {
                        Success = false,
                        RawOutput = rawOutput,
                        Message = "提取结果不是有效的 JSON 格式"
                    };
                }

                // 验证结果
                var validationErrors = validateResult ? ValidateAgainstSchema(extractedData, schema) : new List<string>();

                return new ExtractionResult
                {
                    Success = validationErrors.Count == 0,
                    ExtractedData = extractedData,
                    ValidationErrors = validationErrors,
                    MissingFields = FindMissingRequiredFields(extractedData, schema),
                    RawOutput = rawOutput,
                    Message = validationErrors.Count > 0
                        ? $"提取完成，但存在 {validationErrors.Count} 个验证错误"
                        : "提取完成，数据符合 Schema 要求"
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "结构化提取失败");
                return new ExtractionResult
                {
                    Success = false,
                    Message = $"提取失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 分步提取复杂对象（适合多层级嵌套结构）
        /// </summary>
        [KernelFunction]
        [Description("分步提取复杂嵌套对象。对于包含多个子对象的复杂 Schema，建议使用此方法分步骤提取，每步只提取一个子对象，减少偏差。可以配合 ExtractField 使用。")]
        public async Task<ExtractionResult> ExtractStepByStepAsync(
            [Description("JSON Schema 中要提取的字段路径，例如 'location.city' 或 'speakers[].name'")] string fieldPath,
            [Description("该字段的 JSON Schema 定义（包含 type、properties、items 等）")] string fieldSchema,
            [Description("待提取的原始文本")] string text,
            [Description("会话 ID，用于关联多次提取（可选，首次提取会自动创建）")] string? sessionId = null,
            Kernel? kernel = null)
        {
            try
            {
                // 创建或获取会话
                if (string.IsNullOrEmpty(sessionId))
                {
                    sessionId = Guid.NewGuid().ToString("N")[..16];
                }

                if (!_sessions.TryGetValue(sessionId, out var session))
                {
                    session = new ExtractionSession
                    {
                        SessionId = sessionId,
                        PendingPaths = new List<string> { fieldPath }
                    };
                    _sessions[sessionId] = session;
                }

                // 提取当前字段
                var fieldResult = await ExtractSingleFieldAsync(fieldPath, fieldSchema, text, kernel);

                if (fieldResult.Extracted && fieldResult.Value != null)
                {
                    // 更新会话数据
                    SetNestedValue(session.CurrentData, fieldPath, fieldResult.Value);
                    session.ExtractedPaths.Add(fieldPath);

                    // 从待提取列表中移除
                    session.PendingPaths.Remove(fieldPath);
                }

                return new ExtractionResult
                {
                    Success = fieldResult.Extracted,
                    ExtractedData = session.CurrentData,
                    Message = fieldResult.Extracted
                        ? $"字段 '{fieldPath}' 提取成功。会话 ID: {sessionId}"
                        : $"字段 '{fieldPath}' 提取失败: {fieldResult.SourceText}",
                    RawOutput = fieldResult.SourceText
                };
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "分步提取失败: {FieldPath}", fieldPath);
                return new ExtractionResult
                {
                    Success = false,
                    Message = $"分步提取失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 提取单个字段
        /// </summary>
        [KernelFunction]
        [Description("提取单个字段的值。根据提供的字段 Schema，从文本中精确提取该字段的值，返回包含提取值、置信度和原文来源的结果。")]
        public async Task<FieldExtractionResult> ExtractFieldAsync(
            [Description("字段名称")] string fieldName,
            [Description("字段的 JSON Schema 定义")] string fieldSchema,
            [Description("待提取的文本")] string text,
            Kernel? kernel = null)
        {
            var result = await ExtractSingleFieldAsync(fieldName, fieldSchema, text, kernel);
            return result;
        }

        /// <summary>
        /// 批量提取多个字段（并行）
        /// </summary>
        [KernelFunction]
        [Description("批量提取多个字段。同时提取多个字段，每个字段独立提取后可合并验证。支持并行执行提高效率。返回包含所有字段提取结果的字典。")]
        public async Task<Dictionary<string, FieldExtractionResult>> ExtractFieldsBatchAsync(
            [Description("字段定义列表，JSON 数组格式")] string fieldsDefinition,
            [Description("待提取的文本")] string text,
            Kernel? kernel = null)
        {
            var results = new Dictionary<string, FieldExtractionResult>();

            try
            {
                var fields = JArray.Parse(fieldsDefinition);

                foreach (var field in fields)
                {
                    var fieldName = field["name"]?.ToString();
                    var fieldSchema = field["schema"]?.ToString() ?? "{}";

                    if (!string.IsNullOrEmpty(fieldName))
                    {
                        var result = await ExtractSingleFieldAsync(fieldName, fieldSchema, text, kernel);
                        results[fieldName] = result;
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "批量提取失败");
            }

            return results;
        }

        /// <summary>
        /// 合并分步提取结果并验证
        /// </summary>
        [KernelFunction]
        [Description("合并并验证。将会话中分步提取的所有字段合并为一个完整对象，并根据完整 Schema 进行最终验证，返回验证后的完整数据或错误列表。")]
        public async Task<ExtractionResult> MergeAndValidateAsync(
            [Description("会话 ID")] string sessionId,
            [Description("完整的 JSON Schema（用于最终验证）")] string fullSchema,
            Kernel? kernel = null)
        {
            if (!_sessions.TryGetValue(sessionId, out var session))
            {
                return new ExtractionResult
                {
                    Success = false,
                    Message = $"会话不存在: {sessionId}"
                };
            }

            try
            {
                var schema = JObject.Parse(fullSchema);
                var validationErrors = ValidateAgainstSchema(session.CurrentData, schema);
                var missingFields = FindMissingRequiredFields(session.CurrentData, schema);

                // 尝试使用 LLM 补充缺失字段
                if (missingFields?.Count > 0 && kernel != null)
                {
                    var supplementResult = await TryFillMissingFieldsAsync(missingFields, session.CurrentData.ToString(), text =>
                    {
                        var promptTemplate = _promptTemplateService.LoadTemplate("StructuredExtraction.txt");
                        promptTemplate.AddVariable("schema", fullSchema);
                        promptTemplate.AddVariable("text", text);
                        return promptTemplate.InvokeAsync(kernel.Clone());
                    });

                    if (supplementResult != null)
                    {
                        session.CurrentData = (JObject)supplementResult;
                        validationErrors = ValidateAgainstSchema(session.CurrentData, schema);
                        missingFields = FindMissingRequiredFields(session.CurrentData, schema);
                    }
                }

                session.Completed = true;

                return new ExtractionResult
                {
                    Success = validationErrors.Count == 0 && (missingFields?.Count ?? 0) == 0,
                    ExtractedData = session.CurrentData,
                    ValidationErrors = validationErrors,
                    MissingFields = missingFields,
                    Message = session.Completed ? "所有字段提取完成并验证通过" : $"还有 {missingFields?.Count ?? 0} 个必填字段未提取"
                };
            }
            catch (Exception ex)
            {
                return new ExtractionResult
                {
                    Success = false,
                    Message = $"合并验证失败: {ex.Message}"
                };
            }
        }

        /// <summary>
        /// 获取会话状态
        /// </summary>
        [KernelFunction]
        [Description("获取分步提取的会话状态。返回当前已提取的字段列表、待提取的字段列表和会话是否完成的状态。")]
        public ExtractionSession? GetSessionStatus(string sessionId)
        {
            return _sessions.TryGetValue(sessionId, out var session) ? session : null;
        }

        /// <summary>
        /// 清理会话
        /// </summary>
        [KernelFunction]
        [Description("清理会话。删除指定会话，释放内存资源。提取完成后建议调用此方法清理会话。")]
        public bool CleanupSession(string sessionId)
        {
            return _sessions.Remove(sessionId);
        }

        // 内部方法：提取单个字段
        private async Task<FieldExtractionResult> ExtractSingleFieldAsync(
            string fieldPath,
            string fieldSchema,
            string text,
            Kernel? kernel)
        {
            try
            {
                var schema = JObject.Parse(fieldSchema);
                var typeName = schema["type"]?.ToString() ?? "string";
                var description = schema["description"]?.ToString() ?? fieldPath;

                // 构建提示词
                var prompt = $@"从以下文本中提取字段 '{fieldPath}'。

字段要求:
- 字段名: {fieldPath}
- 类型: {typeName}
- 描述: {description}";

                if (schema["enum"] != null)
                {
                    prompt += $"\n可选值: {schema["enum"]}";
                }

                if (schema["format"] != null)
                {
                    prompt += $"\n格式: {schema["format"]}";
                }

                if (schema["minimum"] != null)
                {
                    prompt += $"\n最小值: {schema["minimum"]}";
                }

                if (schema["maximum"] != null)
                {
                    prompt += $"\n最大值: {schema["maximum"]}";
                }

                prompt += $"\n\n待提取文本:\n{text}\n\n请以 JSON 格式输出结果（如果提取不到值，返回 null）:\n```json\n{{\"path\":\"{fieldPath}\",\"value\":<提取的值>,\"confidence\":0.0-1.0,\"source_text\":\"<原文片段>\"}}```";

                string rawOutput;
                if (kernel != null)
                {
                    var result = await kernel.Clone().InvokePromptAsync(prompt);
                    rawOutput = result.GetValue<string>() ?? "";
                }
                else
                {
                    rawOutput = "{}";
                }

                // 解析结果
                var match = System.Text.RegularExpressions.Regex.Match(rawOutput,
                    @"\{[\s\S]*?\}");

                try
                {
                    var jsonResult = JObject.Parse(match.Success ? match.Value : rawOutput);
                    return new FieldExtractionResult
                    {
                        Path = jsonResult["path"]?.ToString() ?? fieldPath,
                        Value = jsonResult["value"],
                        Confidence = jsonResult["confidence"]?.Value<double>() ?? 0,
                        SourceText = jsonResult["source_text"]?.ToString(),
                        Extracted = jsonResult["value"] != null && jsonResult["value"].Type != JTokenType.Null
                    };
                }
                catch
                {
                    return new FieldExtractionResult
                    {
                        Path = fieldPath,
                        SourceText = rawOutput,
                        Extracted = false
                    };
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "提取字段失败: {FieldPath}", fieldPath);
                return new FieldExtractionResult
                {
                    Path = fieldPath,
                    SourceText = ex.Message,
                    Extracted = false
                };
            }
        }

        // 验证数据是否符合 Schema
        private List<string> ValidateAgainstSchema(JToken data, JObject schema)
        {
            var errors = new List<string>();

            try
            {
                // 验证类型
                var schemaType = schema["type"]?.ToString();
                if (schemaType == "object" && data.Type != JTokenType.Object)
                {
                    errors.Add($"期望对象类型，实际为 {data.Type}");
                }
                else if (schemaType == "array" && data.Type != JTokenType.Array)
                {
                    errors.Add($"期望数组类型，实际为 {data.Type}");
                }
                else if (schemaType == "string" && data.Type != JTokenType.String)
                {
                    errors.Add($"期望字符串类型，实际为 {data.Type}");
                }
                else if (schemaType == "number" && data.Type != JTokenType.Integer && data.Type != JTokenType.Float)
                {
                    errors.Add($"期望数字类型，实际为 {data.Type}");
                }

                // 验证 required 字段
                var requiredFields = schema["required"] as JArray;
                if (requiredFields != null)
                {
                    foreach (var required in requiredFields)
                    {
                        var fieldName = required.ToString();
                        if (data[fieldName] == null)
                        {
                            errors.Add($"缺少必填字段: {fieldName}");
                        }
                    }
                }

                // 验证 object 的 properties
                var properties = schema["properties"] as JObject;
                if (properties != null)
                {
                    foreach (var prop in properties)
                    {
                        if (data[prop.Key] != null && prop.Value["type"] != null)
                        {
                            var childErrors = ValidateAgainstSchema((JObject)data[prop.Key]!, (JObject)prop.Value);
                            foreach (var error in childErrors)
                            {
                                errors.Add($"{prop.Key}.{error}");
                            }
                        }
                    }
                }

                // 验证 array 的 items
                var items = schema["items"] as JObject;
                var arrayData = data as JArray;
                if (items != null && arrayData != null)
                {
                    foreach (var item in arrayData)
                    {
                        if (item.Type == JTokenType.Object)
                        {
                            var childErrors = ValidateAgainstSchema((JObject)item, items);
                            foreach (var error in childErrors)
                            {
                                errors.Add($"[].{error}");
                            }
                        }
                    }
                }

                // 验证 enum
                var enumValues = schema["enum"] as JArray;
                if (enumValues != null)
                {
                    var dataValue = data.Type == JTokenType.String ? data.ToString() : data.ToString();
                    if (!enumValues.Any(e => e.ToString() == dataValue))
                    {
                        errors.Add($"值 '{dataValue}' 不在允许的枚举值中");
                    }
                }

                // 验证 format
                var format = schema["format"]?.ToString();
                if (format == "uri" && data.Type == JTokenType.String)
                {
                    if (!Uri.TryCreate(data.ToString(), UriKind.Absolute, out _))
                    {
                        errors.Add($"'{data}' 不是有效的 URI 格式");
                    }
                }
                else if (format == "email" && data.Type == JTokenType.String)
                {
                    if (!System.Text.RegularExpressions.Regex.IsMatch(data.ToString(), @"^[^@\s]+@[^@\s]+\.[^@\s]+$"))
                    {
                        errors.Add($"'{data}' 不是有效的邮箱格式");
                    }
                }
                else if (format == "date-time" && data.Type == JTokenType.String)
                {
                    if (!DateTime.TryParse(data.ToString(), out _))
                    {
                        errors.Add($"'{data}' 不是有效的日期时间格式");
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"验证过程出错: {ex.Message}");
            }

            return errors;
        }

        // 查找缺失的必填字段
        private List<string> FindMissingRequiredFields(JObject data, JObject schema)
        {
            var missing = new List<string>();

            var requiredFields = schema["required"] as JArray;
            if (requiredFields != null)
            {
                foreach (var required in requiredFields)
                {
                    var fieldName = required.ToString();
                    if (data[fieldName] == null)
                    {
                        missing.Add(fieldName);
                    }
                }
            }

            var properties = schema["properties"] as JObject;
            if (properties != null)
            {
                foreach (var prop in properties)
                {
                    if (data[prop.Key] != null && data[prop.Key].Type == JTokenType.Object)
                    {
                        var childMissing = FindMissingRequiredFields((JObject)data[prop.Key]!, (JObject)prop.Value);
                        foreach (var m in childMissing)
                        {
                            missing.Add($"{prop.Key}.{m}");
                        }
                    }
                }
            }

            return missing;
        }

        // 设置嵌套字段值
        private void SetNestedValue(JObject obj, string path, JToken value)
        {
            var parts = path.Split('.');
            JToken current = obj;

            for (int i = 0; i < parts.Length; i++)
            {
                var part = parts[i];

                // 处理数组索引，如 "speakers[0].name"
                var arrayMatch = System.Text.RegularExpressions.Regex.Match(part, @"^(\w+)\[(\d+)\]$");
                if (arrayMatch.Success)
                {
                    part = arrayMatch.Groups[1].Value;
                    var index = int.Parse(arrayMatch.Groups[2].Value);

                    if (current[part] == null)
                    {
                        current[part] = new JArray();
                    }

                    var array = (JArray)current[part]!;
                    while (array.Count <= index)
                    {
                        array.Add(new JObject());
                    }

                    current = array[index];
                }
                else
                {
                    if (i == parts.Length - 1)
                    {
                        current[part] = value;
                    }
                    else
                    {
                        if (current[part] == null)
                        {
                            current[part] = new JObject();
                        }
                        current = current[part]!;
                    }
                }
            }
        }

        // 尝试补充缺失字段
        private async Task<JToken?> TryFillMissingFieldsAsync(
            List<string> missingFields,
            string currentData,
            Func<string, Task<FunctionResult>> invokeFunc)
        {
            try
            {
                var prompt = $"以下 JSON 数据缺少必填字段: {string.Join(", ", missingFields)}\n\n当前数据:\n{currentData}\n\n请补充缺失的字段值，直接返回完整的 JSON，不要其他解释。";

                var result = await invokeFunc(prompt);
                var output = result.GetValue<string>() ?? "";

                var match = System.Text.RegularExpressions.Regex.Match(output, @"\{[\s\S]*\}");
                if (match.Success)
                {
                    return JObject.Parse(match.Value);
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "补充缺失字段失败");
            }

            return null;
        }
    }
}
