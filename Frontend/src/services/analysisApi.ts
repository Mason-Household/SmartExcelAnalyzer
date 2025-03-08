import axiosInstance from './axiosInstance';
import { FinalizeResponse } from './interfaces/FinalizeResponse';
import { SubmitQueryResponse } from './interfaces/SubmitQueryResponse';
import { ChunkedUploadResponse } from './interfaces/ChunkedUploadResponse';

const CHUNK_SIZE = 5 * 1024 * 1024; // 5MB

export const uploadFileInChunks = async (file: File): Promise<ChunkedUploadResponse> => {
  const totalChunks = Math.ceil(file.size / CHUNK_SIZE);
  const documentId = await initializeUpload(file.name);

  for (let chunkIndex = 0; chunkIndex < totalChunks; chunkIndex++) {
    const start = chunkIndex * CHUNK_SIZE;
    const end = Math.min(start + CHUNK_SIZE, file.size);
    const chunk = file.slice(start, end);
    const formData = new FormData();
    formData.append('file', chunk);
    formData.append('chunkIndex', chunkIndex.toString());
    formData.append('totalChunks', totalChunks.toString());

    console.log(await axiosInstance.post('/analysis/upload-chunk', formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    }));
  }

  return finalizeUpload(documentId);
};

const initializeUpload = async (filename: string): Promise<string> => {
  const response = await axiosInstance.post('/analysis/initialize-upload', { filename });
  return response.data.documentId;
};

const finalizeUpload = async (documentId: string): Promise<FinalizeResponse> => {
  const response = await axiosInstance.post('/analysis/finalize-upload', { documentId });
  return response.data;
};

export const uploadFile = async (file: File): Promise<string> => {
  const formData = new FormData();
  formData.append('fileToUpload', file);

  try {
    const response = await axiosInstance.post('/analysis/upload', formData, {
      headers: {
        'Content-Type': 'multipart/form-data',
      },
    });

    return response.data.documentId;
  } catch (error) {
    console.error('File upload error:', error);
    throw error;
  }
}

export const submitQuery = async (
  query: string, 
  documentId: string
): Promise<SubmitQueryResponse> => {
  const response = await axiosInstance.post<SubmitQueryResponse>('/analysis/query', {
    query,
    documentId,
  });
  return response.data;
};