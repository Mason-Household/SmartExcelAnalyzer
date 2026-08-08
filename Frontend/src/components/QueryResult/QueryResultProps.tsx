
export interface QueryResultProps {
  result: SubmitQueryResult;
}

export interface SubmitQueryResult {
  answer: string;
  question: string;
  documentId: string;
  relevantRows: Array<{ [key: string]: any }>;
  columns?: string[];
}
