using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Services.Training;

namespace PostgreSQL.Embedding.Controllers
{
    [Route("api/vectors")]
    [ApiController]
    public class PgVectorController : ControllerBase
    {
        private readonly PgVectorService _vectorService;
        public PgVectorController(PgVectorService vectorService)
        {
            _vectorService = vectorService;
        }

        [HttpGet("text")]
        public async Task CreateEmbeddingFromText([FromQuery] string text)
        {
            await _vectorService.AddEmbedding(text);
        }

        [HttpGet("search")]
        public async Task<JsonResult> SimilaritySearch([FromQuery] string query)
        {
            var results = await _vectorService.SimilaritySearch(query);
            return new JsonResult(results);
        }
    }
}
