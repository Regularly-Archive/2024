CREATE EXTENSION IF NOT EXISTS vector;
CREATE TABLE text2vectors (
	"id" SERIAL PRIMARY KEY NOT NULL,
	"content" text NOT NULL,
	"embedding" vector(1024) NOT NULL
)