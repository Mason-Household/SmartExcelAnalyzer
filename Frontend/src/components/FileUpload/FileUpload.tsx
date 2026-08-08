import React, { useState, useEffect, useRef } from 'react';
import { Typography, Box, LinearProgress } from '@mui/material';
import { FileUpload as MuiFileUpload } from '@mui/icons-material';
import { useDropzone } from 'react-dropzone';
import { 
  HubConnectionBuilder, 
  HubConnection, 
  LogLevel,
  HttpTransportType
} from '@microsoft/signalr';
import { FileUploadProps } from './FileUploadProps';

// Use the relative URL since we're using the Vite proxy
const SIGNALR_HUB_URL = '/progressHub';

const FileUpload: React.FC<FileUploadProps> = ({ onFileUpload }): React.ReactElement => {
  const [parseProgress, setParseProgress] = useState(0);
  const [saveProgress, setSaveProgress] = useState(0);
  const [connectionStatus, setConnectionStatus] = useState<string>('Initializing...');
  const connectionRef = useRef<HubConnection | null>(null);

  useEffect(() => {
    const connectToHub = async () => {
      try {
        console.log(`Attempting to connect to ${SIGNALR_HUB_URL}`);
        setConnectionStatus('Connecting...');
        
        const newConnection = new HubConnectionBuilder()
          .withUrl(SIGNALR_HUB_URL, {
            // Try all transport types
            transport: 
              HttpTransportType.WebSockets | 
              HttpTransportType.ServerSentEvents | 
              HttpTransportType.LongPolling,
            skipNegotiation: false
          })
          .withAutomaticReconnect([0, 2000, 10000, 30000])
          .configureLogging(LogLevel.Information)
          .build();
          
        // Set up handlers first
        newConnection.on('ReceiveProgress', (progress: number, total: number) => {
          console.log(`Progress received: ${progress}/${total}`);
          setParseProgress(progress * 100);
          setSaveProgress(total * 100);
        });
        
        newConnection.on('UserConnected', (connectionId: string) => {
          console.log(`Connected to hub with ID: ${connectionId}`);
          setConnectionStatus('Connected');
        });
        
        newConnection.on('ReceiveError', (error: string) => {
          console.error('SignalR Error: ', error);
          setConnectionStatus(`Error: ${error}`);
        });
        
        // Handle connection closing
        newConnection.onclose((error?: Error) => {
          console.log('Connection closed:', error);
          setConnectionStatus('Disconnected');
          
          // Try to reconnect after a delay
          setTimeout(() => {
            if (!connectionRef.current) {
              console.log('Attempting to reconnect...');
              connectToHub();
            }
          }, 5000);
        });
        
        // Start the connection
        await newConnection.start();
        console.log('SignalR Connected successfully!');
        setConnectionStatus('Connected');
        connectionRef.current = newConnection;
      } catch (err: any) {
        console.error('SignalR Connection Error: ', err);
        setConnectionStatus(`Connection failed: ${err.message}`);
        
        // Try to reconnect after delay
        setTimeout(() => {
          connectToHub();
        }, 5000);
      }
    };

    connectToHub();

    // Clean up function
    return () => {
      const connection = connectionRef.current;
      if (connection) {
        connection.stop()
          .catch(err => console.error('Error stopping connection:', err));
        connectionRef.current = null;
      }
    };
  }, []);

  const onDrop = (acceptedFiles: File[]) => {
    if (acceptedFiles.length > 0) {
      setParseProgress(0);
      setSaveProgress(0);
      onFileUpload(acceptedFiles[0]);
    }
  };

  const {getRootProps, getInputProps, isDragActive} = useDropzone({
    onDrop,
    accept: {
      'application/vnd.ms-excel': ['.xls', '.xlsx'],
      'text/csv': ['.csv'],
    },
    maxFiles: 1,
  });

  return (
    <Box
      {...getRootProps()}
      sx={{
        border: '2px dashed',
        borderColor: isDragActive ? 'primary.main' : 'grey.300',
        borderRadius: 2,
        p: 2,
        textAlign: 'center',
        cursor: 'pointer',
      }}
    >
      <input {...getInputProps()} />
      <MuiFileUpload sx={{ fontSize: 48 }} />
      <Typography variant="body2" sx={{ mt: 2 }}>
        {isDragActive ? 
          'Drop the file here...' :
          'Drag and drop your file here or click to select'
        }
      </Typography>
      
      <Typography 
        variant="caption" 
        color={connectionStatus === 'Connected' ? 'success.main' : 'text.secondary'}
        sx={{ display: 'block', mt: 1 }}
      >
        SignalR: {connectionStatus}
      </Typography>
      
      {(parseProgress > 0 || saveProgress > 0) && (
        <Box sx={{ mt: 2 }}>
          <Typography variant="body2">
            Parsing Progress: {parseProgress.toFixed(0)}%
          </Typography>
          <LinearProgress 
            variant="determinate"
            value={parseProgress}
          />
          <Typography
            variant="body2"
            sx={{ mt: 1 }} 
          >
            Saving Progress: {saveProgress.toFixed(0)}%
          </Typography>
          <LinearProgress
            variant="determinate" 
            value={saveProgress}
          />
        </Box>
      )}
    </Box>
  );
};

export default FileUpload;