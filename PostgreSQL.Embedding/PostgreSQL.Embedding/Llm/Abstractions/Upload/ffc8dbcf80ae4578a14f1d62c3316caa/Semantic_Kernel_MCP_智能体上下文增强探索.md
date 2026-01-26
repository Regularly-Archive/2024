---
categories:
- 编程语言
date: 2025-03-09 20:42:23
description: 本文深入探讨了 MCP（模型上下文协议），由 Anthropic 设计的开放协议，它如同 AI 领域的 USB 接口，旨在通过统一接口解决大模型连接不同数据源和工具的问题。文章详细介绍了 MCP 的架构、核心角色、工作原理以及如何与 Semantic Kernel 集成，为 .NET 开发者提供高效接入社区 MCP 服务器的方法，减少重复性平台对接工作。此外，还展示了 MCP 在操作浏览器、访问文件系统等场景中的应用效果，并探讨了其局限性及未来发展方向。在 AI 技术快速发展的背景下，MCP 的出现为实现 AI 模型的 “万物互联” 提供了可能，值得开发者关注与探索。
slug: Semantic-Kernel-MCP-Agent-Context-Enhanced-Exploration
tags:
- MCP
- Agent
- Semantic Kernel
- Function Calling
title: Semantic Kernel × MCP：智能体的上下文增强探索
image: /posts/semantic-kernel-mcp-agent-context-enhanced-exploration/MCP_Overview.png
---
时光飞逝，转眼间已步入阳春三月，可我却迟迟未曾动笔写下 2025 年的第一篇 AI 博客。不知大家心中作何感想，从年初 DeepSeek 的爆火出圈，到近期 Manus 的刷屏热议，AI 领域的发展可谓是日新月异。例如，DeepSeek R1 的出现，让人们开始接受慢思考，可我们同样注意到，OpenAI 的 Deep Research 选择了一条和 R1 截然不同的路线，模型与智能体之间的界限开始变得模糊。对于这一点，使用过 Cursor Composer 或者 [Deep Research]() 的朋友，相信你们会有更深刻的感悟。有人说，Agent 会成为 2025 年的 AI 主旋律。我不知道大家是否清楚 AutoGPT 与 Manus 的差别，对我个人而言，最重要的事情是在喧嚣过后找到 “**值得亲手去做的事情**”。所以，今天这篇博客，我想分享一个 “**熟悉而陌生**” 的东西：[MCP](https://modelcontextprotocol.io/introduction)，即：模型上下文协议，并尝试将这个协议和 Semantic Kernel 连接起来。

# MCP 介绍

> [**TL;DR**] MCP 是由 Anthropic 设计的开放协议，其定位类似于 AI 领域的 USB 接口，旨在通过统一接口解决大模型连接不同数据源和工具的问题。该协议通过 JSON-RPC 规范定义了 **Prompt 管理**、**资源访问**和**工具调用**三大核心能力，使得任何支持 **Function Calling** 的模型都能无缝对接外部系统，从而帮助大语言模型实现 “**万物互联**”。

## 什么是 MCP?

MCP（Model Context Protocol）是由 [Anthropic](https://www.anthropic.com/news/model-context-protocol) 设计的一种开放协议，旨在标准化应用程序向大语言模型（LLMs）提供上下文的方式，使大模型能够以统一的方法连接各种数据源和工具。你可以将其理解为 AI 应用的 USB 接口，为 AI 模型连接到不同的数据源和工具提供了标准化的方法。架构设计上，MCP 采用了经典的 C/S 架构，客户端可以使用该协议灵活地连接多个 MCP Server，从而获取丰富的数据和功能支持，如下图所示：

![MCP 基本架构](/posts/semantic-kernel-mcp-agent-context-enhanced-exploration/MCP_Architecture.png)

具体而言，MCP 架构中包括四个核心角色：
* MCP Host：承载用户交互的终端，如 [Claude Desktop](https://claude.ai/download)、[Cusror](https://www.cursor.com/)、[VSCode](https://roocline.dev/) 等，负责发起请求
* MCP Client：协议客户端，负责建立、维护与服务器端的一对一连接，通常需要集成 SDK 到 MCP Host
* MCP Server: 协议服务器端，对外暴露三种核心能力：Prompts、Resources 和 Tools
* Data Source：数据源，是本地资源（如 SQLite、文件系统）与远程服务（如 Github API）的集合

## 为什么选择 MCP?

在过去的这一年里，AI 智能体的技术生态逐渐呈现出两种典型的演进方向。首先，是以 [LangChain](https://www.langchain.com/)、[Semantic Kernel](https://github.com/microsoft/semantic-kernel) 等为代表的 AI 框架；其次，是以 [Dify](https://dify.ai/)、[Coze](https://www.coze.com/) 等为代表的智能体编排平台。这实际上揭示了当前智能体技术发展的双重路径，即：**人们正试图从框架层和平台层两个维度去攻克 Agent 技术的高峰**。

![为什么选择 MCP?](/posts/semantic-kernel-mcp-agent-context-enhanced-exploration/WHY_WE_NEED_MCP.jpg)

然而，当你真正地深入实践这一切的时候，你会在这些框架和平台中发现许多痛点。例如：

* **语言框架的割裂性**：不同技术栈中对 Agent 基础元素的定义存在着根本性差异。例如，LangChain 中采用 Python 的 `@tool` 装饰器来标注工具方法，而 C# 系列的 Semantic Kernel 则通过 `[KernelFunction]` 特性来实现功能注册。这种语法层面的分歧，无形中增加了跨平台协作的成本。
* **平台生态的封闭性**：以 Coze 和 Dify 的插件系统为例，虽然二者均支持集成 [Jina AI](https://jina.ai/) 插件，但是其工作流编排和配置的规范完全不同。这种生态壁垒加剧了不同技术体系间的 “数字鸿沟”，造成应用迁移成本过高，最终导致智能体平台沦为信息孤岛。
* **开发资源的重复消耗**：目前，无论是服务供应商还是开发者，均需要参与智能体平台的适配工作，容易造成重复性工作，这对于 AI 时代而言是一种注意力的浪费。更重要是，这不利于 AI 技术的进一步发展，真正具有突破性的技术创新难以获得足够关注。

如你所见，有了 MCP 以后，开发人员只需要和 MCP 打交道，这是真正意义上的 “**Attention Is All You Need**”。

## MCP 如何工作?

现在，当我们将目光聚焦在 MCP 上面时，我们会发现情况开始有所好转，因为 Anthropic 使用 [JSON-RPC](https://www.jsonrpc.org/specification) 规范定义了一套与语言、平台无关的协议。在该协议中，定义了 Requests、Responses 和 Notifications 三种消息类型：

| Type          | Description                            | Requirements                            |
|---------------|----------------------------------------|-----------------------------------------|
| Requests      | Messages sent to initiate an operation | Must include unique ID and method name  |
| Responses     | Messages sent in reply to requests     | Must include same ID as request         |
| Notifications | One-way messages with no reply         | Must not include an ID                  |

在上文中我们提到，MCP 支持 Prompts、Resources 和 Tools 三大核心能力。以 [Tools](https://spec.modelcontextprotocol.io/specification/2024-11-05/server/tools/) 这个最常见的能力为例，MCP 支持工具的发现、调用和更新，其交互过程通常如下图所示：

![MCP-Tools 能力示意图](/posts/semantic-kernel-mcp-agent-context-enhanced-exploration/MCP_Tools_Message_Flow.png)

此时，我们会注意到，MCP 针对工具调用主要提供了三个 API：`tools/list`、`tools/call` 以及 `notifications/tools/list_changed`。其中，`notifications/tools/list_changed` 是可选的，属于 Notification 的一部分。顾名思义，当服务器端提供的工具列表发生变化时，它能够以通知的形式告知客户端这一变化。如果你熟悉 [JSON-RPC](https://www.jsonrpc.org/specification) 规范，相信你已经在脑海中推测出具体的消息结构。首先，客户端通过 `tools/list` 方法向服务器端发起请求：

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "method": "tools/list",
  "params": {
    "cursor": "optional-cursor-value"
  }
}
```

接下来，服务器端会返回它目前支持的工具列表。这里，我们以经典的 `get_weather` 方法为例：

```json
{
  "jsonrpc": "2.0",
  "id": 1,
  "result": {
    "tools": [{
    "name": "get_weather",
    "description": "get current weather information for a location",
    "inputSchema": {
      "type": "object",
      "properties": {
        "location": {
          "type": "string",
          "description": "city name or zip code"
        }
      },
      "required": ["location"]
    }
  }],
  "nextCursor": "next-page-cursor"
  }
}
```

如果你接触过 [ReAct](https://www.promptingguide.ai/zh/techniques/react)、Tool Use、[Function Calling](https://api-docs.deepseek.com/zh-cn/guides/function_calling) 这些概念，你会发现这一切是如此地熟悉和亲切。当我们将这些工具提供给 LLM 以后，由 LLM 决定是否要调用指定的工具。此时，我们可以通过 `tools/call` 方法来调用指定的工具。这里，同样以经典的 `get_weather` 方法为例：

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "method": "tools/call",
  "params": {
    "name": "get_weather",
    "arguments": {
      "location": "New York"
    }
  }
}
```

此时，我们会收到服务器端的响应消息，如下所示：

```json
{
  "jsonrpc": "2.0",
  "id": 2,
  "result": {
    "content": [
      {
        "type": "text",
        "text": "Current weather in New York:\nTemperature: 72°F\nConditions: Partly cloudy"
      }
    ],
    "isError": false
  }
}
```
读到这里，诸位看官心里一定在吐槽：有没有搞错，就这？这好像和 Function Calling 没什么区别嘛！我的理解是，MCP、Function Calling 和 Agent 本质上是一个层层递进的关系，MCP 提供一种与模型、语言、框架无关的工具抽象，任何支持 Function Calling 的模型都可以调用这些工具，而 Agent 框架则在此基础上对工具进行规划与编排。

![MCP、Function Calling 和 Agent 三者间的联系](/posts/semantic-kernel-mcp-agent-context-enhanced-exploration/Tools_Function_Calling_Agent.svg)

例如，去年年底的时候，Anthropic 和智谱相继发布了 Cumputer Use 功能，可这些功能大多都仅限于在厂商自家的产品中使用。如果你想在国内的 DeekSeek 或者 Kimi 上面尝试，基本上是痴心妄想。可有了 MCP 以后，情况就大不相同。你只需要使用 [Playwright MCP Server](https://github.com/executeautomation/mcp-playwright) 或者 [Browser-Use MCP Server](https://github.com/Saik0s/mcp-browser-use) 便可以轻松 “**尝鲜**”。这次 Manus 爆火后，社区在几个小时内迅速复刻出了[OpenManus](https://github.com/mannaandpoem/OpenManus)，这与该团队直接使用第三方库 [browser-use](https://pypi.org/project/browser-use/) 息息相关。由此可见，一个健康、开放的 AI 生态会极大地促进 AI 应用的繁荣。事实上，自去年 MCP 发布以来，社区里涌现出了大量的第三方 [MCP](https://github.com/punkpeye/awesome-mcp-servers) 服务器，这些服务器极大地扩展了 AI 的能力边界。现在，AI 可以连接到 Notion、Slack、Github、Elasticsearch 等众多平台，如下图所示，[mcpservers.org](https://mcpservers.org/)、[mcp.so](https://mcp.so/) 等网站收录了许多 MCP Server：

![Awesome MCP Servers](/posts/semantic-kernel-mcp-agent-context-enhanced-exploration/Awesome_MCP_Servers.png)

所以，我们为什么要了解 MCP 呢？因为只要接入了 MCP， 便可以拥抱 MCP 背后的整个生态，这意味着 AI 领域的 “**万物互联**” 时刻已悄然到来。唯一的问题在于，国内外的 AI 厂商是否有意愿一起将 MCP 发展为行业标准。我想，届时无论是服务供应商还是个人开发者，都能从 MCP 这个协议中受益。除了 Tools，MCP 还支持 [Resources](https://spec.modelcontextprotocol.io/specification/2024-11-05/server/resources/) 和 [Prompts](https://spec.modelcontextprotocol.io/specification/2024-11-05/server/prompts/) 相关的功能，它们负责对提示词、文件等进行管理。当然，这些并不是本文关注的重点，这里不再赘述。我们只需要知道一件事情，对一个 MCP Server 而言，最重要的是实现 `tools/list` 和 `tools/call` 这两个方法。目前，官方 SDK 支持 Python、TypeScript、Java 和 Kotlin 这四种语言，我们可以使用这些 SDK 来集成或者开发一个 MCP Server。下面是一个 Python 版本的 SQLite Explorer 示例：

```python
from mcp.server.fastmcp import FastMCP
import sqlite3

mcp = FastMCP("SQLite Explorer")

@mcp.resource("schema://main")
def get_schema() -> str:
    """Provide the database schema as a resource"""
    conn = sqlite3.connect("database.db")
    schema = conn.execute(
        "SELECT sql FROM sqlite_master WHERE type='table'"
    ).fetchall()
    return "\n".join(sql[0] for sql in schema if sql[0])

@mcp.tool()
def query_data(sql: str) -> str:
    """Execute SQL queries safely"""
    conn = sqlite3.connect("database.db")
    try:
        result = conn.execute(sql).fetchall()
        return "\n".join(str(row) for row in result)
    except Exception as e:
        return f"Error: {str(e)}"
```
如你所见，在该示例中，MCP Server 提供了一个 resource、一个 tool，前者负责返回当前数据库中的 DDL，后者提供一个查询数据的方法。恭喜你，现在你可以开始着手设计一个针对 Text2SQL 的 Agent 了。

## MCP 的局限性

当然，我们需要学会辩证地看待事物，MCP 并非完美无瑕。首先，我们不清楚国内外厂商适配这一协议的热情到底有多少；其次，类似于大多数 AI 框架，MCP 正处在迅速发展阶段，该协议的最新版本是 2024-11-05。截止目前，官方在 2025 年上半年的 Roadmap 主要集中在：认证/授权、服务发现、无状态操作。所以，未来走向到底如何，着实充满了变数。例如，按照官方的设计，MCP 在传输层（Transports）支持 **stdio** 和 **HTTP with Server-Sent Events (SSE)**，可目前大多数的 MCP Server 都是运行在本地的 **stdio**。对于终端用户而言，使用 MCP 依然需要了解 Python、Node.js 甚至 Docker，不得不说，这其实是一种隐形的成本。

# Semantic Kernel x MCP

![Semantic Kernel 集成 MCP 流程示意图](/posts/semantic-kernel-mcp-agent-context-enhanced-exploration/Semantic_Kernel_x_MCP.png)

在 Semantic Kernel 中，我们使用插件（Plugin）这个概念来描述一组工具，而每个工具则是一个 KernelFunction。因此，如果希望在 Semantic Kernel 中集成 MCP，本质上就是将 MCP 中的 Tools 转换为 Semantic Kernel 中的 KernelFunction。如上图所示，我们将在 Semantic Kernel 中集成一个 MCP 客户端，然后利用 `tools/list` 和 `tools/call` 这两个 API 分别实现工具获取、工具调用这两个流程。

## 工具获取

截止目前，MCP 官方还没有提供对 .NET 的支持，不过社区里还是出现了第三方实现。例如：

* MCPSharp: https://github.com/afrise/MCPSharp
* mcpdotnet: https://github.com/PederHP/mcpdotnet

博主这里选择的是 mcpdotnet，假设我们希望在 Semantic Kernel 中集成 [Playwright MCP Server](https://github.com/executeautomation/mcp-playwright)。此时，我们可以编写下面的代码来连接到对应的 MCP Server：

```c#
var clientOptions = new McpClientOptions()
{
  ClientInfo = new McpDotNet.Protocol.Types.Implementation() 
  { 
    Name = name, Version = "1.0.0" 
  },
};

var serverConfig = new McpServerConfig()
{
  Id = "playwright",
  Name = "playwright",
  TransportType = "stdio",
  TransportOptions = new Dictionary<string, string>
  {
    ["command"] = "npx",
    ["arguments"] = "-y @executeautomation/playwright-mcp-server",
  }
};

var loggerFactory = kernel.Services.GetRequiredService<ILoggerFactory>();
var clientFactory = new McpClientFactory(
  [serverConfig], 
  clientOptions, 
  NullLoggerFactory.Instance
);

var client = await clientFactory.GetClientAsync(serverConfig.Id).ConfigureAwait(false);
```

从 `clientFactory` 获取 `IMcpClient` 实例时，客户端会先调用 `initialize()` 方法。服务器端初始化完成后，会给客户端发送 `notifications/initialized` 通知，表明服务器端已完成初始化。此时，可调用 `ListToolsAsync()` 方法，获取 MCP 服务器端提供的工具列表:

```c#
var listToolsResult = await client.ListToolsAsync().ConfigureAwait(false);
var tools = listToolsResult.Tools;
```

## 协议转换

从前文中可知，MCP 使用 JSONSchema 来描述工具的输入参数，返回值则被定义为一个数组，如下图所示：

![MCP 工具调用输入 & 输出](/posts/semantic-kernel-mcp-agent-context-enhanced-exploration/MCP_Tools_Schema.png)

因此，我们需要写一个中间层，将 MCP 的工具转换为 KernelFunction，这部分内容非常简单，不再赘述：

```c#
// 将 MCP 中的 Tool 转换为 KernelFunction
private static KernelFunction ToKernelFunction(this Tool tool, IMcpClient client) {
  async Task<string> InvokeToolAsync(
    Kernel kernel, 
    KernelFunction function, 
    KernelArguments arguments, 
    CancellationToken cancellationToken
  ) {
    try {
      var mcpArguments = new Dictionary<string, object>();
      foreach (var arg in arguments) {
        if (arg.Value is not null) 
          mcpArguments[arg.Key] = function.ToArgumentValue(arg.Key, arg.Value);
      }

      var result = await client.CallToolAsync(
        tool.Name,
        mcpArguments,
        cancellationToken: cancellationToken
      ).ConfigureAwait(false);

      return string.Join("\n", result.Content
        .Where(c => c.Type == "text")
        .Select(c => c.Text));
      } catch {
        throw;
      }
  }

  return KernelFunctionFactory.CreateFromMethod(
    method: InvokeToolAsync,
    functionName: tool.Name,
    description: tool.Description,
    parameters: ToKernelParameters(tool),
    returnParameter: ToKernelReturnParameter()
  );
}

// 将 MCP 中工具的输入转换为 KernelFunction 输入
private static List<KernelParameterMetadata> ToKernelParameters(Tool tool) {
  var inputSchema = tool.InputSchema;
  var properties = inputSchema?.Properties;
  if (properties == null) return [];

  HashSet<string> requiredProperties = new(inputSchema!.Required ?? []);
  return properties.Select(kvp => new KernelParameterMetadata(kvp.Key) {
    Description = kvp.Value.Description,
    ParameterType = ConvertParameterDataType(kvp.Value, requiredProperties.Contains(kvp.Key)),
    IsRequired = requiredProperties.Contains(kvp.Key)
  })
  .ToList();
}

// 将 JSONSchema 中的数据类型转换为 C# 的数据类型
private static Type ConvertParameterDataType(JsonSchemaProperty property, bool required) {
  var type = property.Type switch {
    "string" => typeof(string),
    "integer" => typeof(int),
    "number" => typeof(double),
    "boolean" => typeof(bool),
    "array" => typeof(List<string>),
    "object" => typeof(Dictionary<string, object>),
    _ => typeof(object)
  };

  return !required && type.IsValueType ? typeof(Nullable<>).MakeGenericType(type) : type;
}

// 转换返回值，简化处理，直接返回字符串类型
private static KernelReturnParameterMetadata? ToKernelReturnParameter() {
  return new KernelReturnParameterMetadata() {
    ParameterType = typeof(string),
  };
}

// 将 KernelFunction 参数转换为 object
private static object ToArgumentValue(this KernelFunction function, string name, object value) {
  var parameter = function.Metadata.Parameters.FirstOrDefault(p => p.Name == name);
  return parameter?.ParameterType switch
  {
    Type t when Nullable.GetUnderlyingType(t) == typeof(int) => Convert.ToInt32(value),
    Type t when Nullable.GetUnderlyingType(t) == typeof(double) => Convert.ToDouble(value),
    Type t when Nullable.GetUnderlyingType(t) == typeof(bool) => Convert.ToBoolean(value),
    Type t when t == typeof(List<string>) => (value as IEnumerable<object>)?.ToList(),
    Type t when t == typeof(Dictionary<string, object>) => (value as Dictionary<string, object>)?.ToDictionary(kvp => kvp.Key, kvp => kvp.Value),
    _ => value,
  } ?? value;
}
```

现在，一切就变得简单了，我们可以封装一个如下的扩展方法：

```c#
public static async Task<IEnumerable<KernelFunction>> GetKernelFunctionsAsync(this IMcpClient client) {
  var listToolsResult = await client.ListToolsAsync().ConfigureAwait(false);
  return listToolsResult.Tools.Select(tool => ToKernelFunction(tool, client)).ToList();
}
```
## 工具调用

在 MCP 中，客户端调用服务器端提供的工具，可以直接使用 `CallToolAsync()` 方法：

```c#
var result = await client.CallToolAsync(
  tool.Name,
  mcpArguments,
  cancellationToken: cancellationToken
).ConfigureAwait(false);
```
当我们转换为 KernelFunction 以后，只需要调用 `InvokeAsync()` 方法即可调用对应的插件函数。考虑到，在 Agent 中，插件函数通常是由 LLM 来调用的，我们将编写下面的扩展方法来实现工具的注册：

```C#
// 注册 MCP Server
public static async Task AddMCPServer(
  this Kernel kernel, string name, string command, 
  string version = "1.0.0", 
  string[] args = null, 
  Dictionary<string, string> env = null
  ) {
    var clientOptions = new McpClientOptions() {
        ClientInfo = new McpDotNet.Protocol.Types.Implementation() { 
          Name = name, Version = "1.0.0" 
        },
    };

    var serverConfig = new McpServerConfig() {
        Id = name,
        Name = name,
        TransportType = "stdio",
        TransportOptions = new Dictionary<string, string> {
            ["command"] = command,
            ["arguments"] = string.Join(' ', args ?? []),
        }
    };

    var loggerFactory = kernel.Services.GetRequiredService<ILoggerFactory>();
    var clientFactory = new McpClientFactory([serverConfig], clientOptions, loggerFactory);

    var client = await clientFactory.GetClientAsync(serverConfig.Id).ConfigureAwait(false);
    var kernelFunctions = await client.GetKernelFunctionsAsync();
    kernel.Plugins.AddFromFunctions(name, kernelFunctions);
}
```
至此，我们便完成了 MCP 在 Semantic Kernel 中的集成。现在，你只需要使用下面的代码片段即可：

```c#
// 添加 playwright-mcp-server
await kernel.AddMCPServer(
  name: "playwright",
  command: "npx",
  version: "1.0.0",
  args: ["-y", "@executeautomation/playwright-mcp-server"],
  env: null
);
```

# 场景化效果展示

好的，当我们给 Semantic Kernel 集成 MCP 以后，现在我们来一起看看它具体能帮我们做什么事情？

## 操作浏览器

如图所示，用户请求：打开 Bing 主页，搜索 "**Model Context Protocol**" 

![MCP-操作浏览器-A](/posts/semantic-kernel-mcp-agent-context-enhanced-exploration/MCP_Example_Browser_Use_0.png)

![MCP-操作浏览器-B](/posts/semantic-kernel-mcp-agent-context-enhanced-exploration/MCP_Example_Browser_Use_1.jpg)

## 访问文件系统

如图所示，用户请求：我在 D:\\Projects\\2024 这个 Git 仓库中都提交过那些代码，最近的一次的更新是什么？

![MCP-访问文件、读取 Git 提交记录](/posts/semantic-kernel-mcp-agent-context-enhanced-exploration/MCP_Example_Git.png)

## 读取 Github 仓库

如图所示，用户请求：阅读 OpenManus 仓库的代码，帮我分析其架构、设计相关的细节

![MCP-读取 Github 仓库](/posts/semantic-kernel-mcp-agent-context-enhanced-exploration/MCP_Example_Github.png)

博主先后体验了四个 MCP Server。其中，[Knowledge Graph Memory Server](https://github.com/modelcontextprotocol/servers/tree/main/src/memory) 可以在本地构建知识图谱，从对话中提取并持久化三元组，实现长期记忆。当然，这里的关键在于，确定三元组的读写时机。今年，我准备将现有 RAG 和 Agent 融合，升级为 Agentic RAG。目前，考虑的是将 ReAct 模式应用于 RAG，显然，这里同样会遇到一个问题，即： LLM 如何判定上下文足以生成最终答案。因篇幅限制，此处不再展示更多截图，一切都需要大家去亲自体验。


# 本文小结

MCP 是 Anthropic 设计的开放协议，其定位类似于 AI 领域的 USB 接口，希望通过统一接口解决大模型连接不同数据源和工具的问题。Semantic Kernel 是微软开源的 Agent 框架，两者的结合可以让 .NET 开发者快速、高效地接入社区中的 MCP 服务器，减少重复性的平台对接工作。除工具调用外，还可以考虑将项目中的提示词模板统一放置到 MCP Server 上管理。博主撰写此文时，网络正在传播着被破解的 Manus 源代码。虽然经常有人说提示工程已不存在了，可在实际的项目中提示词依旧不可或缺。从这个角度来看，尽管提示词技术含量不高，但若能得到妥善管理，至少会比项目被破解、提示词被泄露更显体面。关于更多 MCP 的细节，请参考官方文档：[Introduction - Model Context Protocol](https://modelcontextprotocol.io/introduction) ，无论你是开发者还是普通用户，相信都能在那里找到答案。

# 参考链接
* [Model Context Protocol](https://modelcontextprotocol.io/introduction)
* [Model Context Protocol Specification](https://spec.modelcontextprotocol.io/specification/2024-11-05/)
* [LLM Function-Calling vs. Model Context Protocol (MCP)](https://www.gentoro.com/blog/function-calling-vs-model-context-protocol-mcp)
* [Integrating Model Context Protocol Tools with Semantic Kernel: A Step-by-Step Guide](https://devblogs.microsoft.com/semantic-kernel/integrating-model-context-protocol-tools-with-semantic-kernel-a-step-by-step-guide/)
* [什么是模型上下文协议（MCP）？它如何比传统API更简单地集成AI？](https://mp.weixin.qq.com/s/oyewbUXalcfjjKo6R6YOdA)




