import os
import json
import torch
import logging
import requests
from enum import Enum
from typing import List, Optional
from pydantic import BaseModel
from transformers import pipeline
from qdrant_client.http import models
from qdrant_client import QdrantClient
from fastapi import FastAPI, HTTPException
from fastapi.responses import JSONResponse
from transformers import AutoTokenizer, AutoModel
from urllib3.exceptions import InsecureRequestWarning
from prometheus_fastapi_instrumentator import Instrumentator

from analytics import (
    answer_with_pandas,
    find_matching_rows,
    load_document_dataframe,
    rows_payload,
)

# Set cache directory for transformers models
os.environ['TRANSFORMERS_CACHE'] = '/app/model_cache'
os.environ['HF_HOME'] = '/app/model_cache'
# Explicitly set HuggingFace endpoint to avoid proxy/mirror issues
os.environ['HF_ENDPOINT'] = 'https://huggingface.co'
# Clear any proxy settings that might interfere
for proxy_var in ['HTTP_PROXY', 'HTTPS_PROXY', 'http_proxy', 'https_proxy']:
    if proxy_var in os.environ:
        del os.environ[proxy_var]

# Disable SSL warnings
requests.packages.urllib3.disable_warnings(InsecureRequestWarning)

class EnvironmentVariables(str, Enum):
    QDRANT_HOST = "QDRANT_HOST"
    QDRANT_PORT = "QDRANT_PORT"
    QDRANT_API_KEY = "QDRANT_API_KEY"
    EMBEDDING_MODEL = "EMBEDDING_MODEL"
    QDRANT_USE_HTTPS = "QDRANT_USE_HTTPS"
    TEXT_GENERATION_MODEL = "TEXT_GENERATION_MODEL"

class Query(BaseModel):
    document_id: str
    question: str

class QueryResponse(Query): 
    answer: str
    relevantRows: list
    # Row payloads are unordered maps, so the sheet's column order is sent alongside.
    columns: List[str] = []

class ComputeEmbedding(BaseModel):
    text: str

class ComputeBatchEmbeddings(BaseModel):
    texts: list[str]

DOCUMENT_COLLECTION = "documents"
SUMMARY_COLLECTION = "summaries"

default_qdrant_port = 6333
default_qdrant_host = "localhost"
# Instruction-tuned so it answers questions rather than summarizing them.
default_text_generation_model = "google/flan-t5-large"
default_embedding_model = "sentence-transformers/all-MiniLM-L6-v2"

QDRANT_HOST = os.getenv(EnvironmentVariables.QDRANT_HOST.value, default_qdrant_host)
QDRANT_PORT = int(os.getenv(EnvironmentVariables.QDRANT_PORT.value, default_qdrant_port))
QDRANT_USE_HTTPS = os.getenv(EnvironmentVariables.QDRANT_USE_HTTPS.value, "false").lower() == "true"
EMBEDDING_MODEL = os.getenv(EnvironmentVariables.EMBEDDING_MODEL.value, default_embedding_model)
TEXT_GENERATION_MODEL = os.getenv(EnvironmentVariables.TEXT_GENERATION_MODEL.value, default_text_generation_model)
QDRANT_API_KEY = os.getenv(EnvironmentVariables.QDRANT_API_KEY.value, None)

app = FastAPI()
logging.basicConfig(level=logging.INFO)
logger = logging.getLogger(__name__)
tokenizer = AutoTokenizer.from_pretrained(EMBEDDING_MODEL)
embedding_model = AutoModel.from_pretrained(EMBEDDING_MODEL)
model = pipeline("text2text-generation", model=TEXT_GENERATION_MODEL)

# Use http:// explicitly if QDRANT_USE_HTTPS is False
qdrant_url = f"{'https' if QDRANT_USE_HTTPS else 'http'}://{QDRANT_HOST}:{QDRANT_PORT}"
qdrant_client = QdrantClient(url=qdrant_url, api_key=QDRANT_API_KEY, prefer_grpc=False)

# Instrument the FastAPI app for Prometheus metrics
Instrumentator().instrument(app).expose(app)

@app.get("/health", response_model=dict)
async def health():
    # Models are loaded once at startup; re-loading them here would make every
    # health check take minutes.
    if tokenizer is None or embedding_model is None or model is None:
        raise HTTPException(status_code=500, detail="Models failed to load at startup")
    return {"status": "ok"}

def build_prompt(question: str, rows: List[dict], columns: Optional[List[str]] = None) -> str:
    """Render retrieved rows as a readable table so the model can reason over them."""
    lines = []
    for index, row in enumerate(rows, start=1):
        keys = [c for c in columns if c in row] if columns else list(row.keys())
        fields = " | ".join(
            f"{key}: {row[key]}" for key in keys
            if key != "embedding" and str(row.get(key, "")).strip()
        )
        lines.append(f"Row {index}: {fields}")
    table = "\n".join(lines)
    return (
        "Answer the question using only the spreadsheet rows below. "
        "If the rows do not contain the answer, say you do not have enough information.\n\n"
        f"Rows:\n{table}\n\n"
        f"Question: {question}\n"
        "Answer:"
    )


@app.post("/query", response_model=QueryResponse)
async def process_query(query: Query) -> QueryResponse:
    try:
        # Aggregate questions need every row, not the ten most similar ones, so
        # try to compute an exact answer before falling back to retrieval.
        frame = load_document_dataframe(
            qdrant_client, DOCUMENT_COLLECTION, query.document_id, SUMMARY_COLLECTION
        )
        if frame.empty:
            return QueryResponse(
                answer="No rows are stored for this document. Try re-uploading the file.",
                question=query.question,
                document_id=query.document_id,
                relevantRows=[]
            )

        computed = answer_with_pandas(query.question, frame)
        if computed is not None:
            logger.info("Answered from computed statistics for %s", query.document_id)
            return QueryResponse(
                answer=computed.answer,
                question=query.question,
                document_id=query.document_id,
                relevantRows=computed.rows,
                columns=list(frame.columns)
            )

        matched = find_matching_rows(query.question, frame)
        if matched is not None:
            relevant_rows = rows_payload(matched)
            logger.info("Matched %d rows exactly for %s", len(relevant_rows), query.document_id)
        else:
            inputs = tokenizer(query.question, return_tensors="pt", truncation=True, padding=True)
            with torch.no_grad():
                question_embedding = embedding_model(**inputs).last_hidden_state.mean(dim=1).numpy().tolist()[0]
            search_result = qdrant_client.search(
                collection_name=DOCUMENT_COLLECTION,
                query_vector=question_embedding,
                query_filter=models.Filter(
                    must=[
                        models.FieldCondition(
                            key="document_id",
                            match=models.MatchValue(value=query.document_id)
                        )
                    ]
                ),
                limit=10
            )
            relevant_rows = [json.loads(hit.payload["content"]) for hit in search_result]
            logger.info(f"Retrieved {len(relevant_rows)} rows for {query.document_id}")

        if not relevant_rows:
            return QueryResponse(
                answer="No matching rows were found for this question.",
                question=query.question,
                document_id=query.document_id,
                relevantRows=[],
                columns=list(frame.columns)
            )

        prompt = build_prompt(query.question, relevant_rows, list(frame.columns))
        result = model(
            prompt,
            max_new_tokens=200,
            do_sample=False,
            truncation=True,
        )[0]["generated_text"].strip()
        return QueryResponse(
            answer=result,
            question=query.question,
            document_id=query.document_id,
            relevantRows=relevant_rows,
            columns=list(frame.columns)
        )
    except Exception as e:
        logger.error(f"Error processing query: {str(e)}", exc_info=True)
        raise HTTPException(status_code=500, detail=str(e))

@app.post("/compute_embedding", response_model=list[float])
async def compute_embedding(compute_embedding: ComputeEmbedding):
    try:
        inputs = tokenizer(compute_embedding.text, return_tensors="pt", truncation=True, padding=True)
        with torch.no_grad():
            embeddings = embedding_model(**inputs).last_hidden_state.mean(dim=1)
        return embeddings.numpy().tolist()[0]
    except Exception as e:
        print(e)
        raise HTTPException(status_code=500, detail=str(e))
    
@app.post("/compute_batch_embedding", response_model=List[List[float]])
async def compute_batch_embedding(compute_embedding: ComputeBatchEmbeddings):
    try:
        inputs = tokenizer(compute_embedding.texts, padding=True, truncation=True, return_tensors="pt")
        with torch.no_grad():
            outputs = embedding_model(**inputs)
        embeddings = outputs.last_hidden_state.mean(dim=1)
        return embeddings.tolist()
    except Exception as e:
        raise HTTPException(status_code=500, detail=str(e))
    
async def general_exception_handler(request, exc):
    logger.error(f"An error occurred: {str(exc)}", exc_info=True)
    return JSONResponse(
        status_code=500,
        content={"message": "An internal error occurred", "detail": str(exc)}
    )