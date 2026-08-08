export interface SubmitQueryResponse {
  answer: string;
  question?: string;
  documentId: string;
  relevantRows?: Record<string, unknown>[];
  columns?: string[];
}