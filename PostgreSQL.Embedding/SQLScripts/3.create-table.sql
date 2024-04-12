-- public.app_conversations definition

-- Drop table

-- DROP TABLE public.app_conversations;

CREATE TABLE public.app_conversations (
	id int8 NOT NULL,
	app_id int8 NOT NULL,
	conversation_id varchar(255) NOT NULL,
	summary varchar(255) NULL DEFAULT NULL::character varying,
	created_at timestamp NOT NULL,
	created_by varchar(255) NOT NULL,
	updated_at timestamp NOT NULL,
	updated_by varchar(255) NOT NULL,
	CONSTRAINT app_conversations_pkey PRIMARY KEY (id)
);


-- public.chat_messages definition

-- Drop table

-- DROP TABLE public.chat_messages;

CREATE TABLE public.chat_messages (
	id int8 NOT NULL,
	app_id int8 NOT NULL,
	conversation_id varchar(255) NOT NULL,
	"content" text NOT NULL,
	is_user_message bool NOT NULL,
	created_at timestamp NOT NULL,
	created_by varchar(255) NOT NULL,
	updated_at timestamp NOT NULL,
	updated_by varchar(255) NOT NULL,
	CONSTRAINT chat_messages_pkey PRIMARY KEY (id)
);


-- public.document_import_record definition

-- Drop table

-- DROP TABLE public.document_import_record;

CREATE TABLE public.document_import_record (
	id int8 NOT NULL,
	task_id varchar(255) NOT NULL,
	file_name varchar(255) NOT NULL,
	knowledge_base_id int8 NOT NULL,
	queue_status int4 NOT NULL,
	created_at timestamp NOT NULL,
	created_by varchar(255) NOT NULL,
	updated_at timestamp NOT NULL,
	updated_by varchar(255) NOT NULL,
	process_start_time timestamp NULL,
	process_end_time timestamp NULL,
	process_duration_time float8 NULL,
	CONSTRAINT document_import_record_pkey PRIMARY KEY (id)
);


-- public.items definition

-- Drop table

-- DROP TABLE public.items;

CREATE TABLE public.items (
	id bigserial NOT NULL,
	embedding public.vector NULL,
	CONSTRAINT items_pkey PRIMARY KEY (id)
);


-- public.llm_app definition

-- Drop table

-- DROP TABLE public.llm_app;

CREATE TABLE public.llm_app (
	id int8 NOT NULL,
	avatar varchar(255) NULL,
	intro varchar(255) NULL,
	app_type int4 NOT NULL,
	prompt varchar(255) NULL,
	text_model varchar(255) NOT NULL,
	temperature numeric NOT NULL,
	created_at timestamp NOT NULL,
	created_by varchar(255) NOT NULL,
	updated_at timestamp NOT NULL,
	updated_by varchar(255) NOT NULL,
	"name" varchar(255) NOT NULL,
	welcome varchar(255) NULL DEFAULT NULL::character varying,
	CONSTRAINT llm_app_pkey PRIMARY KEY (id)
);


-- public.llm_app_knowledge definition

-- Drop table

-- DROP TABLE public.llm_app_knowledge;

CREATE TABLE public.llm_app_knowledge (
	id int8 NOT NULL,
	app_id int8 NOT NULL,
	knowledge_base_id int8 NOT NULL,
	created_at timestamp NOT NULL,
	created_by varchar(255) NOT NULL,
	updated_at timestamp NOT NULL,
	updated_by varchar(255) NOT NULL,
	CONSTRAINT llm_app_knowledge_pkey PRIMARY KEY (id)
);


-- public.llm_knowledgebase definition

-- Drop table

-- DROP TABLE public.llm_knowledgebase;

CREATE TABLE public.llm_knowledgebase (
	id int8 NOT NULL,
	avatar varchar(255) NULL,
	intro varchar(255) NULL,
	embedding_model varchar(255) NOT NULL,
	max_tokens_per_paragraph int4 NOT NULL,
	max_tokens_per_line int4 NOT NULL,
	overlapping_tokens int4 NOT NULL,
	created_at timestamp NOT NULL,
	created_by varchar(255) NOT NULL,
	updated_at timestamp NOT NULL,
	updated_by varchar(255) NOT NULL,
	"name" varchar(255) NOT NULL,
	retrieval_type int4 NOT NULL DEFAULT 0,
	CONSTRAINT llm_knowledgebase_pkey PRIMARY KEY (id)
);


-- public.llm_model definition

-- Drop table

-- DROP TABLE public.llm_model;

CREATE TABLE public.llm_model (
	id int8 NOT NULL,
	model_name varchar(255) NOT NULL,
	model_type int4 NOT NULL,
	service_provider int4 NOT NULL,
	created_at timestamp NOT NULL,
	created_by varchar(255) NOT NULL,
	updated_at timestamp NOT NULL,
	updated_by varchar(255) NOT NULL,
	api_key varchar(255) NULL,
	base_url varchar(255) NULL,
	is_builtin_model bool NULL DEFAULT false,
	CONSTRAINT llm_model_pkey PRIMARY KEY (id)
);


-- public."sk-chaejg-default" definition

-- Drop table

-- DROP TABLE public."sk-chaejg-default";

CREATE TABLE public."sk-chaejg-default" (
	id text NOT NULL,
	embedding public.vector NULL,
	tags _text NOT NULL DEFAULT '{}'::text[],
	"content" text NOT NULL DEFAULT ''::text,
	payload jsonb NOT NULL DEFAULT '{}'::jsonb,
	CONSTRAINT "sk-chaejg-default_pkey" PRIMARY KEY (id)
);


-- public."sk-training-default" definition

-- Drop table

-- DROP TABLE public."sk-training-default";

CREATE TABLE public."sk-training-default" (
	id text NOT NULL,
	embedding public.vector NULL,
	tags _text NOT NULL DEFAULT '{}'::text[],
	"content" text NOT NULL DEFAULT ''::text,
	payload jsonb NOT NULL DEFAULT '{}'::jsonb,
	CONSTRAINT "sk-training-default_pkey" PRIMARY KEY (id)
);
CREATE INDEX idx_chinese_full_text_search ON public."sk-training-default" USING gin (to_tsvector('chinese'::regconfig, 'content'::text));


-- public."sk-xjxbmk-default" definition

-- Drop table

-- DROP TABLE public."sk-xjxbmk-default";

CREATE TABLE public."sk-xjxbmk-default" (
	id text NOT NULL,
	embedding public.vector NULL,
	tags _text NOT NULL DEFAULT '{}'::text[],
	"content" text NOT NULL DEFAULT ''::text,
	payload jsonb NOT NULL DEFAULT '{}'::jsonb,
	CONSTRAINT "sk-xjxbmk-default_pkey" PRIMARY KEY (id)
);
CREATE INDEX idx_tags ON public."sk-xjxbmk-default" USING gin (tags);


-- public.sk_table_prefix_mapping definition

-- Drop table

-- DROP TABLE public.sk_table_prefix_mapping;

CREATE TABLE public.sk_table_prefix_mapping (
	id int8 NOT NULL,
	full_name varchar(255) NOT NULL,
	short_name varchar(255) NOT NULL,
	created_at timestamp NOT NULL,
	created_by varchar(255) NOT NULL,
	updated_at timestamp NOT NULL,
	updated_by varchar(255) NOT NULL,
	CONSTRAINT sk_table_prefix_mapping_pkey PRIMARY KEY (id)
);


-- public.sys_user definition

-- Drop table

-- DROP TABLE public.sys_user;

CREATE TABLE public.sys_user (
	id int8 NOT NULL,
	user_name varchar(32) NOT NULL,
	"password" varchar(255) NOT NULL,
	created_at timestamp NOT NULL,
	created_by varchar(255) NOT NULL,
	updated_at timestamp NOT NULL,
	updated_by varchar(255) NOT NULL,
	avatar varchar(255) NULL DEFAULT NULL::character varying,
	CONSTRAINT sys_user_pkey PRIMARY KEY (id)
);