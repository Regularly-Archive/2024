using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Domain.Models.Rerank;
using PostgreSQL.Embedding.Domain.Models.WebApi;
using PostgreSQL.Embedding.Llm.Abstractions;

namespace PostgreSQL.Embedding.Application.Controllers.Controllers;

[Route("api/[controller]")]
[AllowAnonymous]
public class RerankerController : ControllerBase
{
    private readonly IServiceProvider _serviceProvider;
    private readonly IRerankService _rerankService;
    public RerankerController(IServiceProvider serviceProvider)
    {
        _serviceProvider = serviceProvider;
        _rerankService = serviceProvider.GetKeyedService<IRerankService>(nameof(RerankerType.BM25));
    }

    [HttpPost("ComputeScores")]
    public JsonResult ComputeScores([FromBody]RerankRequest request)
    {
        
        var rerankResult = _rerankService.Sort(request.Query, request.Documents, x => x);
        var rerankResponse = new RerankResponse() { Query =  request.Query };
        rerankResponse.Scores = rerankResult.Select(x => new RerankScorePair() { Document = x.Document, Score = (float)x.Score }).ToList();

        return ApiResult.Success(rerankResponse);
    }

    [HttpPost("GetTopN")]
    public JsonResult GetTopN([FromBody] RerankTopNRequest request)
    {
        var rerankResult = _rerankService.GetTopN(request.Query, request.Documents, x => x, request.TopN);
        var rerankResponse = new RerankResponse() { Query = request.Query };
        rerankResponse.Scores = rerankResult.Select(x => new RerankScorePair() { Document = x.Document, Score = (float)x.Score }).ToList();

        return ApiResult.Success(rerankResponse);
    }
}
