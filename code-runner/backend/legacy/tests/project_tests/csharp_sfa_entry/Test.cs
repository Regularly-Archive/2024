#:package Newtonsoft.Json@13.0.3

using Newtonsoft.Json;
using System;

// 生成随机数
var random = new Random();
int a = random.Next(1, 100);
int b = random.Next(1, 100);
int sum = a + b;

// 创建数据对象
var result = new {
    A = a,
    B = b,
    Sum = sum,
    Timestamp = DateTime.Now,
    Language = "C# Script"
};

// JSON 序列化
string json = JsonConvert.SerializeObject(result, Formatting.Indented);
Console.WriteLine(json);