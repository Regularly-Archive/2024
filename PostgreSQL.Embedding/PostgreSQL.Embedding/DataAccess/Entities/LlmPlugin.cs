﻿using SqlSugar;

namespace PostgreSQL.Embedding.DataAccess.Entities
{
    [SugarTable("llm_plugins")]
    public class LlmPlugin : BaseEntity
    {
        [SugarColumn(ColumnName = "plugin_name", ColumnDataType = "varchar(32)")]
        public string PluginName { get; set; }

        [SugarColumn(ColumnName = "plugin_intro")]
        public string PluginIntro { get; set; }

        [SugarColumn(ColumnName = "type_name")]
        public string TypeName {  get; set; }
    }
}