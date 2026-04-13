using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using PostgreSQL.Embedding.Plugins.Abstration;
using System;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins.Custom
{
    [KernelPlugin(Description = "日期时间处理插件。提供日期计算、日期比较、日期格式化等功能。", Version = "2.0")]
    public class DateTimePlugin : BasePlugin
    {
        public DateTimePlugin(IServiceProvider serviceProvider)
            : base(serviceProvider)
        {
        }

        /// <summary>
        /// 获取本地时区的当前日期和时间
        /// </summary>
        /// <example>
        /// Now() => 2025-01-08 21:15:30
        /// Now("yyyy-MM-dd") => 2025-01-08
        /// Now("HH:mm:ss") => 21:15:30
        /// </example>
        /// <param name="format">日期时间格式，支持：yyyy(年)、MM(月)、dd(日)、HH(24小时)、mm(分)、ss(秒)等</param>
        /// <returns>格式化后的日期时间字符串</returns>
        [KernelFunction]
        [Description("获取本地时区的当前日期和时间。format 支持：yyyy(年)、MM(月)、dd(日)、HH(24小时)、mm(分)、ss(秒)等，如 Now(\"yyyy-MM-dd HH:mm:ss\")")]
        public string Now(
            [Description("日期时间格式，默认 yyyy-MM-dd HH:mm:ss")] string format = "yyyy-MM-dd HH:mm:ss") =>
            DateTimeOffset.Now.ToString(format);

        /// <summary>
        /// 获取当前 UTC 日期和时间
        /// </summary>
        /// <example>
        /// UtcNow() => 2025-01-08 13:15:30
        /// UtcNow("yyyy-MM-dd") => 2025-01-08
        /// </example>
        /// <param name="format">日期时间格式，默认 yyyy-MM-dd HH:mm:ss</param>
        /// <returns>格式化后的 UTC 日期时间字符串</returns>
        [KernelFunction]
        [Description("获取当前 UTC 日期和时间。format 支持：yyyy(年)、MM(月)、dd(日)、HH(24小时)、mm(分)、ss(秒)等")]
        public string UtcNow(
            [Description("日期时间格式，默认 yyyy-MM-dd HH:mm:ss")] string format = "yyyy-MM-dd HH:mm:ss") =>
            DateTimeOffset.UtcNow.ToString(format);

        /// <summary>
        /// 计算两个日期之间相差的天数
        /// </summary>
        /// <example>
        /// DateDiff("2025-01-01", "2025-01-08") => 7
        /// </example>
        /// <param name="date1">第一个日期，格式：yyyy-MM-dd</param>
        /// <param name="date2">第二个日期，格式：yyyy-MM-dd</param>
        /// <returns>相差的天数</returns>
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

        /// <summary>
        /// 计算目标日期距离今天还有多少天
        /// </summary>
        /// <example>
        /// DateDiffToday("2025-01-15") => 7
        /// </example>
        /// <param name="date">目标日期，格式：yyyy-MM-dd</param>
        /// <returns>与今天相差的天数</returns>
        [KernelFunction]
        [Description("计算目标日期距离今天还有多少天。输入目标日期（格式：yyyy-MM-dd），返回与今天相差的天数。")]
        public string DateDiffToday([Description("目标日期，格式：yyyy-MM-dd")] string date)
        {
            var dateTime = DateTime.Parse(date);
            return Math.Abs((dateTime - DateTime.Today).Days).ToString();
        }

        /// <summary>
        /// 获取从今天往前推指定天数后的日期
        /// </summary>
        /// <example>
        /// DaysAgo(7) => 2025年1月1日
        /// </example>
        /// <param name="days">从今天往前推的天数</param>
        /// <returns>从今天往前推指定天数后的日期</returns>
        [KernelFunction]
        [Description("获取从今天往前推指定天数后的日期。输入天数，返回对应日期。")]
        public string DaysAgo([Description("从今天往前推的天数")] double days) =>
            DateTimeOffset.Now.AddDays(-days).ToString("D");

        /// <summary>
        /// 获取上一个指定星期几的日期
        /// </summary>
        /// <example>
        /// DateMatchingLastDayName(Tuesday) => 2025年1月7日 星期二
        /// </example>
        /// <param name="dayOfWeek">要匹配的星期几</param>
        /// <returns>上一个指定星期几的日期</returns>
        /// <exception cref="ArgumentOutOfRangeException">dayOfWeek 不是有效的星期几</exception>
        [KernelFunction]
        [Description("获取上一个指定星期几的日期。例如：上一个星期二的日期")]
        public string DateMatchingLastDayName(
            [Description("要匹配的星期几")] DayOfWeek dayOfWeek)
        {
            DateTimeOffset dateTime = DateTimeOffset.Now;

            // 从前一天开始向前最多七天查找匹配的星期几
            for (int i = 1; i <= 7; ++i)
            {
                dateTime = dateTime.AddDays(-1);
                if (dateTime.DayOfWeek == dayOfWeek)
                {
                    break;
                }
            }

            return dateTime.ToString("D");
        }

        /// <summary>
        /// 获取当前年份
        /// </summary>
        /// <example>
        /// Year() => 2025
        /// </example>
        /// <returns>当前年份</returns>
        [KernelFunction]
        [Description("获取当前年份")]
        public string Year() =>
            DateTimeOffset.Now.ToString("yyyy");

        /// <summary>
        /// 获取当前月份（两位数字）
        /// </summary>
        /// <example>
        /// Month() => 01
        /// </example>
        /// <returns>当前月份（01-12）</returns>
        [KernelFunction]
        [Description("获取当前月份（两位数字，如 01、12）")]
        public string Month() =>
            DateTimeOffset.Now.ToString("MM");

        /// <summary>
        /// 获取当前日期（月份中的第几天）
        /// </summary>
        /// <example>
        /// Day() => 08
        /// </example>
        /// <returns>当前日期（01-31）</returns>
        [KernelFunction]
        [Description("获取当前日期（月份中的第几天，如 01、08、31）")]
        public string Day() =>
            DateTimeOffset.Now.ToString("dd");

        /// <summary>
        /// 获取当前小时（24小时制）
        /// </summary>
        /// <example>
        /// Hour() => 21
        /// </example>
        /// <returns>当前小时（00-23）</returns>
        [KernelFunction]
        [Description("获取当前小时（24小时制，如 09、21）")]
        public string Hour() =>
            DateTimeOffset.Now.ToString("HH");

        /// <summary>
        /// 获取当前分钟数
        /// </summary>
        /// <example>
        /// Minute() => 15
        /// </example>
        /// <returns>当前分钟数（00-59）</returns>
        [KernelFunction]
        [Description("获取当前分钟数（00-59）")]
        public string Minute() =>
            DateTimeOffset.Now.ToString("mm");

        /// <summary>
        /// 获取当前秒数
        /// </summary>
        /// <example>
        /// Second() => 30
        /// </example>
        /// <returns>当前秒数（00-59）</returns>
        [KernelFunction]
        [Description("获取当前秒数（00-59）")]
        public string Second() =>
            DateTimeOffset.Now.ToString("ss");

        /// <summary>
        /// 获取本地时区与 UTC 的时差
        /// </summary>
        /// <example>
        /// TimeZoneOffset() => +08:00
        /// </example>
        /// <returns>本地时区与 UTC 的时差（如 +08:00、-05:00）</returns>
        [KernelFunction]
        [Description("获取本地时区与 UTC 的时差（如 +08:00、-05:00）")]
        public string TimeZoneOffset() =>
            DateTimeOffset.Now.ToString("%K");

        /// <summary>
        /// 获取本地时区名称
        /// </summary>
        /// <example>
        /// TimeZoneName() => 中国标准时间
        /// </example>
        /// <remarks>
        /// 注意：这是"当前"时区，它会随着年份变化而改变，例如从 PST 变为 PDT
        /// </remarks>
        /// <returns>本地时区名称</returns>
        [KernelFunction]
        [Description("获取本地时区名称（如 中国标准时间、PST）")]
        public string TimeZoneName() =>
            TimeZoneInfo.Local.DisplayName;
    }
}
