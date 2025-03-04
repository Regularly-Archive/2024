﻿using DocumentFormat.OpenXml.Vml;
using Microsoft.AspNetCore.Mvc;
using PostgreSQL.Embedding.Common;
using PostgreSQL.Embedding.Common.Models.WebApi;
using PostgreSQL.Embedding.Common.Models.WebApi.QuerableFilters;
using PostgreSQL.Embedding.DataAccess;
using PostgreSQL.Embedding.DataAccess.Entities;
using PostgreSQL.Embedding.LlmServices.Abstration;
using SqlSugar;

namespace PostgreSQL.Embedding.Controllers
{
    public class DocumentController : CrudBaseController<DocumentImportRecord, DocumentQueryableFilter>
    {
        private IKnowledgeBaseService _knowledgeBaseService;
        public DocumentController(
            CrudBaseService<DocumentImportRecord> crudBaseService,
            IKnowledgeBaseService knowledgeBaseService
            ) : base(crudBaseService)
        {
            _knowledgeBaseService = knowledgeBaseService;
        }

        public override async Task<JsonResult> SelectByIdAsync(long id)
        {
            var db = _crudBaseService.SqlSugarClient;

            var importRecord = await _crudBaseService.GetByIdAsync(id);
            var knowledgeBase = await db.Queryable<KnowledgeBase>().Where(x => x.Id == importRecord.KnowledgeBaseId).FirstAsync();
            importRecord.KnowledgeBaseName = knowledgeBase != null ? knowledgeBase.Name : string.Empty;
            return ApiResult.Success(importRecord);
        }

        [HttpGet("paginate")]
        public override async Task<JsonResult> GetByPageAsync(QueryParameter<DocumentImportRecord, DocumentQueryableFilter> queryParameter)
        {
            var queryable = _crudBaseService.SqlSugarClient.Queryable<DocumentImportRecord>()
              .LeftJoin<KnowledgeBase>((r, k) => r.KnowledgeBaseId == k.Id)
              .Select((r, k) => new DocumentImportRecord()
              {
                  Id = r.Id,
                  TaskId = r.TaskId,
                  FileName = r.FileName,
                  QueueStatus = r.QueueStatus,
                  KnowledgeBaseId = r.KnowledgeBaseId,
                  KnowledgeBaseName = k.Name,
                  CreatedAt = r.CreatedAt,
                  CreatedBy = r.CreatedBy,
                  UpdatedAt = r.UpdatedAt,
                  UpdatedBy = r.UpdatedBy,
                  ProcessStartTime = r.ProcessStartTime,
                  ProcessEndTime = r.ProcessEndTime,
                  ProcessDuartionTime = r.ProcessDuartionTime,
                  DocumentType = r.DocumentType,
                  Content = r.Content,
              })
              .OrderByDescending(r => r.CreatedAt);

            if (queryParameter != null && queryParameter.Filter != null)
                queryable = queryParameter.Filter.Apply(queryable);

            var total = await queryable.CountAsync();
            var list = queryable.Skip(queryParameter.PageSize * (queryParameter.PageIndex - 1)).Take(queryParameter.PageSize).ToList();
            var result = new PagedResult<DocumentImportRecord> { TotalCount = total, Rows = list };
            return ApiResult.Success(result);
        }


        public override async Task<JsonResult> FindListAsync([FromQuery] DocumentQueryableFilter filter = null)
        {
            var queryable = _crudBaseService.SqlSugarClient.Queryable<DocumentImportRecord>()
              .LeftJoin<KnowledgeBase>((r, k) => r.KnowledgeBaseId == k.Id)
              .Select((r, k) => new DocumentImportRecord()
              {
                  Id = r.Id,
                  TaskId = r.TaskId,
                  FileName = r.FileName,
                  QueueStatus = r.QueueStatus,
                  KnowledgeBaseId = r.KnowledgeBaseId,
                  KnowledgeBaseName = k.Name,
                  CreatedAt = r.CreatedAt,
                  CreatedBy = r.CreatedBy,
                  UpdatedAt = r.UpdatedAt,
                  UpdatedBy = r.UpdatedBy,
                  ProcessStartTime = r.ProcessStartTime,
                  ProcessEndTime = r.ProcessEndTime,
                  ProcessDuartionTime = r.ProcessDuartionTime,
                  DocumentType = r.DocumentType,
                  Content = r.Content,
              })
              .OrderByDescending(r => r.CreatedAt);

            if (filter != null)
                queryable = filter.Apply(queryable);

            var list = queryable.ToListAsync();
            return ApiResult.Success(list);
        }

        public override async Task<JsonResult> DeleteAsync(string ids)
        {
            var keys = ids.Split(',').Select(x => long.Parse(x)).ToList();
            var importRecords = await _crudBaseService.Repository.FindListAsync(x => keys.Contains(x.Id));

            if (importRecords.Any(x => x.QueueStatus == (int)QueueStatus.Processing))
            {
                var importRecord = importRecords.FirstOrDefault(x => x.QueueStatus == (int)QueueStatus.Processing);
                throw new InvalidOperationException($"文档\"{importRecord.FileName}\"正在处理中，无法删除！");
            }

            var deleteTasks = importRecords.Select(async importRecord =>
            {
                await _knowledgeBaseService.DeleteKnowledgeBaseChunksByFileName(importRecord.KnowledgeBaseId, importRecord.FileName);
            });

            await Task.WhenAll(deleteTasks);
            return ApiResult.Success(new { });
        }
    }
}
