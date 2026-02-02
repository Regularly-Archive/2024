using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Plugins.Abstration;
using System;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "日期时间处理插件。提供日期计算、日期比较、日期格式化等功能。", Version = "1.1")]
    public class DateTimePlugin : BasePlugin
    {
        public DateTimePlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {

        }

        [KernelFunction]
        [Description("计算两个日期之间相差的天数。输入两个日期（格式：yyyy-MM-dd），返回相差的天数。")]
        public string DateDiff(
            [Description("第一个日期，格式：yyyy-MM-dd")] string date1,
            [Description("第二个日期，格式：yyyy-MM-dd")] string date2)
        {
            var dateTime1 = DateTime.Parse(date1);
            var dateTime2 = DateTime.Parse(date2);
            return Math.Abs((dateTime1 - dateTime2).Days).ToString();
        }

        [KernelFunction]
        [Description("计算目标日期距离今天还有多少天。输入目标日期（格式：yyyy-MM-dd），返回与今天相差的天数。")]
        public string DateDiffToday([Description("目标日期，格式：yyyy-MM-dd")] string date)
        {
            var dateTime = DateTime.Parse(date);
            return Math.Abs((dateTime - DateTime.Today).Days).ToString();
        }

        [KernelFunction]
        [Description("返回当前日期向前推移指定天数后的日期。")]
        public string TodayBefore(
            [Description("天数，正整数")] int days,
            [Description("可选，日期格式化provider")] IFormatProvider? formatProvider = null)
        {
            var dateTime = DateTime.Now.AddDays(-1 * days);
            return dateTime.ToString("D", formatProvider);
        }

        [KernelFunction]
        [Description("返回当前日期向后推移指定天数后的日期。")]
        public string TodayAfter(
            [Description("天数，正整数")] int days,
            [Description("可选，日期格式化provider")] IFormatProvider? formatProvider = null)
        {
            var dateTime = DateTime.Now.AddDays(days);
            return dateTime.ToString("D", formatProvider);
        }
    }
}
