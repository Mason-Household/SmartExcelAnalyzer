import React from 'react';
import { 
  Table, 
  TableBody, 
  TableCell, 
  TableContainer, 
  TableHead, 
  TableRow, 
  Paper, 
  Typography, 
  Box 
} from '@mui/material';
import { QueryResultProps } from './QueryResultProps';

const QueryResult: React.FC<QueryResultProps> = ({ result }: QueryResultProps) => {
  const { answer, question, documentId, relevantRows, columns } = result;

  // Row payloads arrive as unordered maps, so headers come from the sheet's
  // column order and each cell is looked up by name rather than by position.
  const headers = React.useMemo(() => {
    if (columns && columns.length > 0) return columns;
    const seen = new Set<string>();
    (relevantRows ?? []).forEach((row) =>
      Object.keys(row).forEach((key) => seen.add(key))
    );
    return Array.from(seen);
  }, [columns, relevantRows]);

  return (
    <Box sx={{ mt: 2 }}>
      <Typography 
        variant="h6" 
        gutterBottom
      >
        Query Result
      </Typography>
      <Typography 
        variant="body1"
      >
        Question: {question}
      </Typography>
      <Typography 
        variant="body1"
      >
        Answer: {answer}
      </Typography>
      <Typography 
        variant="body2"
      >
        Document ID: {documentId}
      </Typography>

      {relevantRows && relevantRows.length > 0 ? (
        <TableContainer component={Paper}>
          <Table>
            <TableHead>
              <TableRow>
                {headers.map((key) => (
                  <TableCell key={key}>{key}</TableCell>
                ))}
              </TableRow>
            </TableHead>
            <TableBody>
              {relevantRows.map((row, index) => (
                <TableRow key={index}>
                  {headers.map((key) => (
                    <TableCell key={key}>{String(row[key] ?? '')}</TableCell>
                  ))}
                </TableRow>
              ))}
            </TableBody>
          </Table>
        </TableContainer>
      ) : (
        <Typography variant="body2">No relevant rows found.</Typography>
      )}
    </Box>
  );
};

export default QueryResult;