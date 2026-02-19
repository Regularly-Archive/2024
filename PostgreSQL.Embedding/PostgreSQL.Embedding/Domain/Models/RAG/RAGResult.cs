namespace PostgreSQL.Embedding.Domain.Models.RAG
{
    public class RAGResult
    {
        public string PlainAnswer { get; set; }
        public string CitedAnswer { get; set; }
        public List<LlmCitationModel> AnswerSources { get; set; } = new List<LlmCitationModel>();

        public static RAGResult FromEmptyAnswer(string defaultAnswer)
        {
            return new RAGResult { PlainAnswer = defaultAnswer, CitedAnswer = defaultAnswer };
        }

        public static RAGResult FromCitedAnswer(string citedAnswer, string plainAnswer, List<LlmCitationModel> citationSources)
        {
            return new RAGResult { PlainAnswer = plainAnswer, CitedAnswer = citedAnswer, AnswerSources = citationSources };
        }
    }
}
