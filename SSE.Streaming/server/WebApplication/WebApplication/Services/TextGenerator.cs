
namespace WebApp.Services
{
    public class TextGenerator : ITextGenerator
    {
        public async IAsyncEnumerable<string> Generate(string prompt, CancellationToken cancellationToken)
        {
            var text = "天之道，损有余而补不足，是故虚胜实，不足胜有余。其意博，其理奥，其趣深，天地之象分，阴阳之候列，变化之由表，死生之兆彰，不谋而遗迹自同";

            foreach (var item in text)
            {
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                await Task.Delay(200);

                yield return item.ToString();
            }
        }
    }
}
