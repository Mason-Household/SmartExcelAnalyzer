"""Deterministic spreadsheet analytics.

Vector search returns only the rows most similar to a question, which is the
right tool for "find rows about X" but the wrong tool for anything that has to
see every row: counts, totals, averages, maxima. Asking a small language model
to do that arithmetic over a 10-row sample produces confident wrong answers.

This module reconstructs the full sheet from the rows already stored in Qdrant
and answers those questions with real pandas operations. Questions it cannot
answer confidently return None so the caller can fall back to retrieval + LLM.
"""
import json
import logging
import re
from dataclasses import dataclass, field
from typing import Any, Dict, List, Optional, Tuple

import pandas as pd
from qdrant_client.http import models

logger = logging.getLogger(__name__)

SCROLL_PAGE_SIZE = 512
MAX_ROWS = 200_000
MAX_RETURNED_ROWS = 50
# Guards the literal-value scan on wide categorical columns.
MAX_UNIQUE_VALUES_SCANNED = 1_000
_CACHE_LIMIT = 8

# Documents are immutable once uploaded, so caching the frame is safe.
_DF_CACHE: "dict[str, pd.DataFrame]" = {}


@dataclass
class AnalyticResult:
    answer: str
    rows: List[Dict[str, Any]] = field(default_factory=list)


# --------------------------------------------------------------------------
# Loading
# --------------------------------------------------------------------------
def load_column_order(client, summary_collection: str, document_id: str) -> List[str]:
    """Sheet column order, which the row payloads themselves do not preserve."""
    try:
        points, _ = client.scroll(
            collection_name=summary_collection,
            scroll_filter=models.Filter(
                must=[
                    models.FieldCondition(
                        key="document_id",
                        match=models.MatchValue(value=document_id),
                    )
                ]
            ),
            limit=1,
            with_payload=True,
            with_vectors=False,
        )
    except Exception:
        logger.warning("Could not read column order for %s", document_id, exc_info=True)
        return []

    if not points:
        return []
    try:
        payload = json.loads((points[0].payload or {}).get("content") or "{}")
    except (TypeError, json.JSONDecodeError):
        return []

    for key, value in payload.items():
        if key.casefold() != "summary" or not isinstance(value, dict):
            continue
        for inner_key, inner_value in value.items():
            if inner_key.casefold() == "columns" and isinstance(inner_value, list):
                return [str(column) for column in inner_value]
    return []


def load_document_dataframe(
    client,
    collection_name: str,
    document_id: str,
    summary_collection: Optional[str] = None,
) -> pd.DataFrame:
    """Page through every stored row for a document and build a DataFrame."""
    if document_id in _DF_CACHE:
        return _DF_CACHE[document_id]

    records: List[Dict[str, Any]] = []
    offset = None
    while True:
        points, offset = client.scroll(
            collection_name=collection_name,
            scroll_filter=models.Filter(
                must=[
                    models.FieldCondition(
                        key="document_id",
                        match=models.MatchValue(value=document_id),
                    )
                ]
            ),
            limit=SCROLL_PAGE_SIZE,
            offset=offset,
            with_payload=True,
            with_vectors=False,
        )
        for point in points:
            content = (point.payload or {}).get("content")
            if not content:
                continue
            try:
                row = json.loads(content)
            except (TypeError, json.JSONDecodeError):
                continue
            if isinstance(row, dict):
                row.pop("embedding", None)
                records.append(row)
        if offset is None or len(records) >= MAX_ROWS:
            break

    frame = _coerce_numeric(pd.DataFrame(records)) if records else pd.DataFrame()

    if not frame.empty and summary_collection:
        order = load_column_order(client, summary_collection, document_id)
        if order:
            ordered = [c for c in order if c in frame.columns]
            frame = frame[ordered + [c for c in frame.columns if c not in ordered]]

    if len(_DF_CACHE) >= _CACHE_LIMIT:
        _DF_CACHE.pop(next(iter(_DF_CACHE)))
    _DF_CACHE[document_id] = frame
    logger.info("Loaded %d rows for document %s", len(frame), document_id)
    return frame


def invalidate_cache(document_id: Optional[str] = None) -> None:
    if document_id is None:
        _DF_CACHE.clear()
    else:
        _DF_CACHE.pop(document_id, None)


def _coerce_numeric(frame: pd.DataFrame) -> pd.DataFrame:
    """Excel values arrive as JSON strings; recover numeric columns for math."""
    for column in frame.columns:
        if frame[column].dtype != object:
            continue
        populated = frame[column].notna().sum()
        if not populated:
            continue
        converted = pd.to_numeric(frame[column], errors="coerce")
        if converted.notna().sum() >= 0.8 * populated:
            frame[column] = converted
    return frame


# --------------------------------------------------------------------------
# Question parsing
# --------------------------------------------------------------------------
COUNT_PATTERN = r"\b(how many|how much|count|number of|total number)\b"
UNIQUE_PATTERN = r"\b(unique|distinct|different)\b"
GROUP_PATTERN = r"\b(?:per|by|for each|each)\s+([a-z0-9_ ]+?)\b"
NUMERIC_INTENTS: List[Tuple[str, str]] = [
    ("sum", r"\b(total|sum|combined|altogether)\b"),
    ("mean", r"\b(average|mean|avg|typical)\b"),
    ("max", r"\b(max|maximum|highest|largest|longest|greatest|biggest|most)\b"),
    ("min", r"\b(min|minimum|lowest|smallest|shortest|least|fewest)\b"),
]


def _normalize(text: Any) -> str:
    return re.sub(r"[^a-z0-9]+", " ", str(text).lower()).strip()


# Words too generic to identify a column on their own, so "what task" can match
# a "Task Name" column while "name" alone matches nothing.
GENERIC_HEADER_WORDS = {"name", "id", "no", "number", "of", "the", "type", "value"}


def _stem(word: str) -> str:
    """Crude singular form, so "sales" in a question finds a "Sale Amount" column."""
    if len(word) > 4 and word.endswith("ies"):
        return word[:-3] + "y"
    if len(word) > 4 and word.endswith(("ses", "xes", "zes", "ches", "shes")):
        return word[:-2]
    if len(word) > 3 and word.endswith("s") and not word.endswith("ss"):
        return word[:-1]
    return word


def _match_columns(question: str, frame: pd.DataFrame) -> List[str]:
    """Columns named in the question, most specific match first."""
    normalized = _normalize(question)
    asked = {_stem(word) for word in normalized.split()}
    matches: List[Tuple[int, str]] = []
    for column in frame.columns:
        name = _normalize(column)
        if not name:
            continue
        if re.search(rf"\b{re.escape(name)}\b", normalized):
            matches.append((1000 + len(name), column))
            continue
        # Fall back to distinctive words: "task" should still find "Task Name".
        words = [
            word
            for word in name.split()
            if len(word) > 3 and word not in GENERIC_HEADER_WORDS
        ]
        hits = [w for w in words if _stem(w) in asked]
        if hits:
            matches.append((len(hits) * 100 + len(name), column))
    matches.sort(reverse=True)
    return [column for _, column in matches]


def _match_filters(question: str, frame: pd.DataFrame) -> List[Tuple[str, Any]]:
    """Literal cell values named in the question, e.g. 'Alice' or 'Done'."""
    normalized = _normalize(question)
    filters: List[Tuple[str, Any]] = []
    for column in frame.columns:
        if frame[column].dtype != object:
            continue
        best: Optional[Tuple[int, Any]] = None
        for value in frame[column].dropna().unique()[:MAX_UNIQUE_VALUES_SCANNED]:
            text = _normalize(value)
            if len(text) < 3:
                continue
            if re.search(rf"\b{re.escape(text)}\b", normalized):
                if best is None or len(text) > best[0]:
                    best = (len(text), value)
        if best is not None:
            filters.append((column, best[1]))
    return filters


def _apply_filters(
    frame: pd.DataFrame, filters: List[Tuple[str, Any]]
) -> Tuple[pd.DataFrame, str]:
    if not filters:
        return frame, ""
    mask = pd.Series(True, index=frame.index)
    described = []
    for column, value in filters:
        mask &= (
            frame[column].astype(str).str.strip().str.casefold()
            == str(value).strip().casefold()
        )
        described.append(f"{column} = {value}")
    return frame[mask], " and ".join(described)


def _format_number(value: Any) -> str:
    try:
        number = float(value)
    except (TypeError, ValueError):
        return str(value)
    if number.is_integer():
        return f"{int(number):,}"
    return f"{number:,.2f}"


def rows_payload(frame: pd.DataFrame) -> List[Dict[str, Any]]:
    subset = frame.head(MAX_RETURNED_ROWS)
    return json.loads(subset.to_json(orient="records"))


_rows_payload = rows_payload


def find_matching_rows(question: str, frame: pd.DataFrame) -> Optional[pd.DataFrame]:
    """
    Rows that literally contain a value named in the question, e.g. "Alice".

    Exact filtering beats vector search for lookups: it finds every matching row
    instead of the ten nearest ones, so the model is not asked to answer from a
    sample that may be missing the row it needs.
    """
    if frame.empty:
        return None

    filters = _match_filters(question, frame)
    if not filters:
        return None

    filtered, _ = _apply_filters(frame, filters)
    if filtered.empty or len(filtered) == len(frame):
        return None
    return filtered


# --------------------------------------------------------------------------
# Answering
# --------------------------------------------------------------------------
def answer_with_pandas(question: str, frame: pd.DataFrame) -> Optional[AnalyticResult]:
    """Compute an exact answer, or None when the question needs the LLM."""
    if frame.empty:
        return None

    normalized = _normalize(question)
    columns = _match_columns(question, frame)
    numeric_columns = [c for c in columns if pd.api.types.is_numeric_dtype(frame[c])]
    filters = _match_filters(question, frame)
    filtered, filter_text = _apply_filters(frame, filters)
    scope = f" where {filter_text}" if filter_text else ""

    wants_count = re.search(COUNT_PATTERN, normalized) is not None
    wants_unique = re.search(UNIQUE_PATTERN, normalized) is not None
    group_column = _resolve_group_column(normalized, frame, columns)

    # "How many tasks per owner" -> a breakdown rather than a single number.
    if wants_count and group_column:
        counts = filtered[group_column].value_counts()
        if counts.empty:
            return None
        lines = "\n".join(
            f"- {index}: {_format_number(value)}" for index, value in counts.items()
        )
        return AnalyticResult(
            answer=f"Row count by {group_column}{scope}:\n{lines}",
            rows=_rows_payload(filtered),
        )

    if wants_count and wants_unique and columns:
        column = columns[0]
        distinct = filtered[column].nunique()
        return AnalyticResult(
            answer=f"There are {_format_number(distinct)} distinct values of {column}{scope}.",
            rows=_rows_payload(filtered),
        )

    if wants_count:
        total = len(filtered)
        if filter_text:
            answer = (
                f"{_format_number(total)} of {_format_number(len(frame))} rows match {filter_text}."
            )
        else:
            answer = f"The sheet has {_format_number(total)} rows."
        return AnalyticResult(answer=answer, rows=_rows_payload(filtered))

    intent = _detect_numeric_intent(normalized)

    # "Who had the most in sales?" is a ranking across groups, not the single
    # largest row: the top seller is the one whose rows add up highest.
    if intent in ("max", "min"):
        rank_column = _resolve_rank_column(normalized, frame, columns, group_column)
        if rank_column is not None:
            ranked = _answer_ranked(
                intent, rank_column, numeric_columns, columns, filtered, scope
            )
            if ranked is not None:
                return ranked

    if intent and numeric_columns:
        column = numeric_columns[0]
        series = filtered[column].dropna()
        if series.empty:
            return None
        return _answer_numeric(intent, column, series, filtered, frame, scope)

    if wants_unique and columns:
        column = columns[0]
        values = filtered[column].dropna().unique()[:MAX_RETURNED_ROWS]
        if not len(values):
            return None
        listed = ", ".join(str(value) for value in values)
        return AnalyticResult(
            answer=f"Distinct values of {column}{scope}: {listed}.",
            rows=_rows_payload(filtered),
        )

    # "What task is Alice assigned to?" - a lookup the sheet answers exactly, and
    # exactly is better than the model's guess from a handful of retrieved rows.
    # Yes/no questions are excluded: they need a judgement about a value, not the
    # value itself, so the model answers those from the same filtered rows.
    if filters and not filtered.empty and not _is_yes_no_question(normalized):
        filter_columns = {column for column, _ in filters}
        target = next((c for c in columns if c not in filter_columns), None)
        if target is not None:
            values = filtered[target].dropna().unique()[:MAX_RETURNED_ROWS]
            if len(values):
                listed = ", ".join(str(value) for value in values)
                return AnalyticResult(
                    answer=f"{target} where {filter_text}: {listed}.",
                    rows=rows_payload(filtered),
                )

    # Not an aggregate question - let retrieval and the LLM handle it.
    return None


YES_NO_OPENERS = {
    "is", "are", "was", "were", "has", "have", "had", "do", "does", "did",
    "can", "could", "should", "will", "would", "am",
}


def _is_yes_no_question(normalized: str) -> bool:
    words = normalized.split()
    return bool(words) and words[0] in YES_NO_OPENERS


def _resolve_group_column(
    normalized: str, frame: pd.DataFrame, matched: List[str]
) -> Optional[str]:
    match = re.search(GROUP_PATTERN, normalized)
    if not match:
        return None
    phrase = match.group(1).strip()
    for column in frame.columns:
        name = _normalize(column)
        if name and (name in phrase or phrase in name):
            return column
    return None


# Words that mark a column as identifying a person, used to answer "who ..."
# when the question names no column at all.
PERSON_COLUMN_WORDS = {
    "person", "people", "employee", "owner", "rep", "representative",
    "salesperson", "seller", "agent", "customer", "client", "manager",
    "staff", "member", "author", "assigned", "assignee", "contact", "user",
}


def _resolve_rank_column(
    normalized: str,
    frame: pd.DataFrame,
    matched: List[str],
    group_column: Optional[str],
) -> Optional[str]:
    """The column to rank groups by: what "who" or "which X" refers to."""
    if group_column is not None:
        return group_column

    words = normalized.split()
    if not words or words[0] not in {"who", "which", "what", "whose"}:
        return None

    # "Who has the most tasks" ranks people, not tasks, so a person column wins
    # over any other column the question happens to name.
    if words[0] in {"who", "whose"}:
        person = _resolve_person_column(frame)
        if person is not None:
            return person

    text_columns = [
        column
        for column in matched
        if not pd.api.types.is_numeric_dtype(frame[column])
    ]
    return text_columns[0] if text_columns else None


def _resolve_person_column(frame: pd.DataFrame) -> Optional[str]:
    for column in frame.columns:
        if pd.api.types.is_numeric_dtype(frame[column]):
            continue
        if set(_normalize(column).split()) & PERSON_COLUMN_WORDS:
            return column
    for column in frame.columns:
        if _normalize(column) == "name":
            return column
    return None


def _answer_ranked(
    intent: str,
    group_column: str,
    numeric_columns: List[str],
    matched: List[str],
    filtered: pd.DataFrame,
    scope: str,
) -> Optional[AnalyticResult]:
    """Rank groups by a summed measure, or by row count when none is named."""
    measure = next((c for c in numeric_columns if c != group_column), None)
    if measure is not None:
        totals = filtered.groupby(group_column)[measure].sum()
        described = f"total {measure}"
    elif any(c != group_column for c in matched):
        totals = filtered.groupby(group_column).size()
        described = "row count"
    else:
        # Nothing to rank by; the model is a better guess than an invented metric.
        return None

    totals = totals.dropna()
    if totals.empty:
        return None

    totals = totals.sort_values(ascending=intent == "min")
    winner = totals.index[0]
    label = "highest" if intent == "max" else "lowest"
    breakdown = "\n".join(
        f"- {index}: {_format_number(value)}" for index, value in totals.head(5).items()
    )
    rows = filtered[filtered[group_column] == winner]
    return AnalyticResult(
        answer=(
            f"{winner} has the {label} {described}{scope} at "
            f"{_format_number(totals.iloc[0])}.\n\nBy {group_column}:\n{breakdown}"
        ),
        rows=rows_payload(rows),
    )


def _detect_numeric_intent(normalized: str) -> Optional[str]:
    for intent, pattern in NUMERIC_INTENTS:
        if re.search(pattern, normalized):
            return intent
    return None


def _answer_numeric(
    intent: str,
    column: str,
    series: "pd.Series",
    filtered: pd.DataFrame,
    frame: pd.DataFrame,
    scope: str,
) -> AnalyticResult:
    if intent == "sum":
        return AnalyticResult(
            answer=f"The total {column}{scope} is {_format_number(series.sum())} "
            f"across {_format_number(len(series))} rows.",
            rows=_rows_payload(filtered),
        )
    if intent == "mean":
        return AnalyticResult(
            answer=f"The average {column}{scope} is {_format_number(series.mean())} "
            f"across {_format_number(len(series))} rows.",
            rows=_rows_payload(filtered),
        )

    index = series.idxmax() if intent == "max" else series.idxmin()
    label = "highest" if intent == "max" else "lowest"
    winning_row = filtered.loc[[index]]
    details = " | ".join(
        f"{key}: {value}" for key, value in winning_row.iloc[0].items() if pd.notna(value)
    )
    return AnalyticResult(
        answer=f"The {label} {column}{scope} is {_format_number(series.loc[index])} ({details}).",
        rows=_rows_payload(winning_row),
    )
