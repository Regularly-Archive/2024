using Microsoft.SemanticKernel;
using PostgreSQL.Embedding.Common.Attributes;
using System;
using System.ComponentModel;

namespace PostgreSQL.Embedding.Plugins
{
    [KernelPlugin(Description = "一个适用于日期和时间处理的插件")] 
    public class DateTimePlugin
    {
        [KernelFunction]
        [Description("一个计算两个日期间差值的函数, 返回两个日期之间相差的天数")]
        public string DateDiff([Description("第一个日期，格式为：yyyy-MM-dd")] string date1, [Description("第二个日期，格式为：yyyy-MM-dd")] string date2)
        {
            var dateTime1 = DateTime.Parse(date1);
            var dateTime2 = DateTime.Parse(date2);
            return Math.Abs((dateTime1 - dateTime2).Days).ToString();
        }

        [KernelFunction]
        [Description("一个计算目标日期与当前日期差值的函数, 返回目标日期距离当前日期的天数")]
        public string DateDiffToday([Description("目标日期，格式为：yyyy-MM-dd")] string date)
        {
            var dateTime = DateTime.Parse(date);
            return Math.Abs((dateTime - DateTime.Today).Days).ToString();
        }

        [KernelFunction]
        [Description("返回当前日期前指定天数所在日期")]
        public string TodayBefore([Description("天数，正整数")] int days, IFormatProvider? formatProvider = null)
        {
            var dateTime = DateTime.Now.AddDays(-1 * days);
            return dateTime.ToString("D", formatProvider);
        }

        [KernelFunction]
        [Description("返回当前日期后指定天数所在日期")]
        public string TodayAfter([Description("天数，正整数")] int days, IFormatProvider? formatProvider = null)
        {
            var dateTime = DateTime.Now.AddDays(days);
            return dateTime.ToString("D", formatProvider);
        }
    }
}
