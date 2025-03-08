import React, { useEffect, useState } from 'react';
import { ThemeProvider, createTheme, CssBaseline } from '@mui/material';
import { Paper, Snackbar, Alert, Box } from '@mui/material';
import { submitQuery, uploadFile } from './services/analysisApi.ts';
import DocumentList from './components/DocumentList/DocumentList';
import FileUpload from './components/FileUpload/FileUpload';
import QueryForm from './components/QueryForm/QueryForm';
import QueryResult from './components/QueryResult/QueryResult.tsx';
import { Document } from './interfaces/Document.tsx';
import ThemeSwitch from './components/ThemeSwitch/ThemeSwitch.tsx';
import { ThemeMode, ThemeModeEnum } from './components/ThemeSwitch/ThemeMode.tsx';
import { SubmitQueryResult } from './components/QueryResult/QueryResultProps.tsx';

const originalTheme = createTheme({
  palette: {
    mode: ThemeModeEnum.LIGHT,
    primary: {
      main: '#1976d2',
    },
    secondary: {
      main: '#dc004e',
    },
    background: {
      default: '#f0f4f8',
    },
    text: {
      primary: '#000000',
      secondary: '#6c6c6c',
    },
  },
}, {
  palette: {
    mode: ThemeModeEnum.DARK,
    primary: {
      main: '#bb86fc',
    },
    secondary: {
      main: '#03dac6',
    },
    background: {
      default: '#121212',
      paper: '#1e1e1e',
    },
    text: {
      primary: '#ffffff',
      secondary: '#b0b0b0',
    },
  },
});

function SmartExcelAnalyzerApp() {
  const [theme, setTheme] = useState(originalTheme);
  const [toastOpen, setToastOpen] = useState(false);
  const [toastMessage, setToastMessage] = useState('');
  const [documents, setDocuments] = useState<Document[]>([]);
  const [queryResult, setQueryResult] = useState<SubmitQueryResult | null>(null);
  const [selectedDocument, setSelectedDocument] = useState<Document | null>(null);
  const [toastSeverity, setToastSeverity] = useState<'success' | 'error'>('success');

  const handleFileUpload = async (file: File) => {
    try {
      const res = await uploadFile(file);
      setDocuments([...documents, { id: res, name: file.name }]);
      showToast('File uploaded successfully', 'success');
    } catch (error) {
      console.error('File upload failed:', error);
      showToast('File upload failed', 'error');
    }
  };

  const handleDocumentSelect = (document: Document) => {
    setSelectedDocument(document);
    setQueryResult(null);
  };

  const handleQuerySubmit = async (question: string) => {
    if (selectedDocument) {
      try {
        const result = await submitQuery(
          question,
          selectedDocument.id
        );
        setQueryResult({
          ...result,
          relevantRows: result.relevantRows || [],
        });
      } catch (error) {
        console.error('Query submission failed:', error);
        showToast('Query submission failed', 'error');
      }
    }
  };

  const showToast = (message: string, severity: 'success' | 'error') => {
    setToastMessage(message);

    setToastSeverity(severity);
    setToastOpen(true);
  };

  const handleToastClose = (_event?: React.SyntheticEvent | Event, reason?: string) => {
    if (reason === 'clickaway') return;
    setToastOpen(false);
  };

  const [themeMode, setThemeMode] = useState<ThemeMode>({ mode: ThemeModeEnum.DARK });

  useEffect(() => {
    setTheme(createTheme({
      palette: {
        mode: themeMode.mode,
      },
    }));
  }, [themeMode]);

  return (
    <ThemeProvider theme={theme}>
      <CssBaseline />
      <Box
        sx={{
          minHeight: '100vh',
          width: '100vw',
          display: 'flex',
          justifyContent: 'center',
          alignItems: 'center',
          bgcolor: 'background.default',
        }}
      >
        <Box
          sx={{
            width: '100%',
            maxWidth: '600px',
            display: 'flex',
            flexDirection: 'column',
            alignItems: 'center',
            gap: 3,
            p: 3,
          }}
        >
          <Paper
            elevation={3}
            sx={{
              p: 3,
              width: '100%',
              display: 'flex',
              flexDirection: 'column',
              alignItems: 'center',
            }}
          >
            <FileUpload onFileUpload={handleFileUpload} />
            <Box 
              sx={{ 
                mt: 2, 
                width: '100%' 
              }}
            >
              <DocumentList
                documents={documents}
                selectedDocument={selectedDocument}
                onSelectDocument={handleDocumentSelect}
              />
            </Box>
            <Box 
              sx={{ 
                mt: 2, 
                width: '100%',
                display: 'flex',
                justifyContent: 'space-between',
              }}
            >
              <QueryForm
                onQuerySubmit={handleQuerySubmit}
                isDocumentSelected={!!selectedDocument}
              />
              <ThemeSwitch 
                themeMode={themeMode} 
                setThemeMode={setThemeMode} 
              />
            </Box>
          </Paper>
          {queryResult && (
            <Paper elevation={3} sx={{ p: 3, width: '100%'}}>
              <QueryResult result={queryResult} />
            </Paper>
          )}
        </Box>
      </Box>
      <Snackbar 
        open={toastOpen} 
        autoHideDuration={6000} 
        onClose={handleToastClose}
      >
        <Alert 
          onClose={handleToastClose} 
          severity={toastSeverity} 
          sx={{ width: '100%' }}
        >
          {toastMessage}
        </Alert>
      </Snackbar>
    </ThemeProvider>
  );
}

export default SmartExcelAnalyzerApp;