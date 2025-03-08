import React from 'react';
import '@testing-library/jest-dom';
import FileUpload from './FileUpload';
import { describe, expect, it, jest } from '@jest/globals';
import { render, screen, fireEvent, act, createEvent, waitFor } from '@testing-library/react';

jest.mock('@microsoft/signalr');
jest.mock('../../utils/getEnv', () => ({
  getEnv: () => 'http://localhost:5000'
}));

describe('FileUpload', () => {
  it('renders the file upload component', async () => {
    await act(async () => { 
      render(<FileUpload onFileUpload={jest.fn()} />);
    });
    const text = screen.getByText(/Drag and drop your file here or click to select/i);
    expect(text).toBeTruthy();
  });

  it('calls onFileUpload when a file is dropped', async () => {
    const onFileUploadMock = jest.fn();
    await act(async () => {
      render(<FileUpload onFileUpload={onFileUploadMock} />);
    });
    
    await act(async () => {
      await new Promise(resolve => setTimeout(resolve, 1000));
    });

    const file = new File(['dummy content'], 'example.xlsx', { type: 'application/vnd.ms-excel' });
    const dropzone = screen.getByText(/Drag and drop your file here or click to select/i).parentElement;
    
    if (!dropzone) {
      throw new Error('Dropzone element not found');
    }

    await act(async () => {
      const dropEvent = createEvent.drop(dropzone);
      Object.defineProperty(dropEvent, 'dataTransfer', {
        value: {
          files: [file],
          types: ['Files']
        }
      });
      await act(() => fireEvent(dropzone, dropEvent)); 
    });

    await waitFor(() => {
      expect(onFileUploadMock).toHaveBeenCalledWith(file);
    });
  });

  // it('shows progress bars when progress is received', async () => {
  //   await act(async () => {
  //     render(<FileUpload onFileUpload={jest.fn()} />);
  //   });
    
  //   await act(async () => {
  //     await new Promise(resolve => setTimeout(resolve, 1000));
  //   });

  //   await waitFor(() => {
  //     const parsingText = screen.getByText(/Parsing Progress:/i);
  //     const savingText = screen.getByText(/Saving Progress:/i);
  //     expect(parsingText).toBeTruthy();
  //     expect(savingText).toBeTruthy();
  //   });
    
  //   await waitFor(() => {
  //     const progressBars = screen.getAllByRole('progressbar');
  //     expect(progressBars[0].getAttribute('aria-valuenow')).toBe('50');
  //     expect(progressBars[1].getAttribute('aria-valuenow')).toBe('75');
  //   });
  // });
});
