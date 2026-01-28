using AngleSharp.Text;
using Shouldly;
using System.Text.RegularExpressions;

namespace Wikit.Tests.Utils;

public class When_Call_Regex
{
    private readonly Regex _regexKeepFormat = new Regex(@"<KEEP_FORMAT>([\s\S]*?)<\/KEEP_FORMAT>", RegexOptions.Compiled);
    private readonly Regex _regexCitations = new Regex(@"<CITATIONS>([\s\S]*?)<\/CITATIONS>", RegexOptions.Compiled);


    [Fact]
    public void It_Should_Match_Content_With_Keep_Format_Tag()
    {
        var input = @"文本开始
            <KEEP_FORMAT>
            这里是需要保留合适的内容，从今天起，你的名字叫做7527!@#$%^&*()
                <CITATIONS>
                    [1]: 引用内容1
                    [2]: 引用内容2
                    [3]: 引用内容3
                </CITATIONS>
            </KEEP_FORMAT>
            文本结束";

        var match = _regexKeepFormat.Match(input);
        var ragContent = match.Groups[1].Value;

        var citationsMatch = _regexCitations.Match(ragContent);
        var citations = ragContent[citationsMatch.Index..];


        this.ShouldSatisfyAllConditions(
            () => match.Success.ShouldBeTrue()
        );
    }
}
