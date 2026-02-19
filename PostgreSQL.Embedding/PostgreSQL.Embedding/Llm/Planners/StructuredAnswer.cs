namespace PostgreSQL.Embedding.Llm.Planners
{
    /// <summary>
    /// Structured final answer with confidence assessment
    /// </summary>
    public class StructuredAnswer
    {
        /// <summary>
        /// The main answer content
        /// </summary>
        public string Content { get; set; } = string.Empty;

        /// <summary>
        /// Confidence level: High, Medium, or Low
        /// </summary>
        public string Level { get; set; } = "Medium";

        /// <summary>
        /// Justification or explanation for the confidence level
        /// </summary>
        public string Reason { get; set; } = string.Empty;

        /// <summary>
        /// Create from plain text with confidence pattern: "Answer [High] - justification"
        /// </summary>
        public static StructuredAnswer FromText(string text)
        {
            var structuredAnswer = new StructuredAnswer();

            var match = System.Text.RegularExpressions.Regex.Match(text, @"\[(High|Medium|Low)\](.*)");
            if (match.Success)
            {
                structuredAnswer.Level = match.Groups[1].Value;
                structuredAnswer.Reason = match.Groups[2].Value.Trim();
                structuredAnswer.Content = System.Text.RegularExpressions.Regex.Replace(text, @"\[(High|Medium|Low)\].*", "").Trim();
            }
            else
            {
                structuredAnswer.Content = text;
            }

            return structuredAnswer;
        }

        public override string ToString()
        {
            return $"<Content>{Content}</Content>\n    <Confidence Level=\"{Level}\">{Reason}</Confidence>";
        }
    }
}
