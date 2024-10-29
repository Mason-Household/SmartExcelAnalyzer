# Smart Excel Analyzer

## Table of Contents

- [Smart Excel Analyzer](#smart-excel-analyzer)
  - [Table of Contents](#table-of-contents)
  - [Introduction](#introduction)
  - [Features](#features)
    - [Detailed Features](#detailed-features)
  - [Tech Stack](#tech-stack)
  - [Architecture](#architecture)
  - [Setup and Installation](#setup-and-installation)
  - [Usage](#usage)
  - [API Documentation](#api-documentation)
  - [Full Workflow Example](#full-workflow-example)
    - [1. Upload Excel File](#1-upload-excel-file)
    - [2. Get the Status of an Upload](#2-get-the-status-of-an-upload)
    - [3. Ask a Question](#3-ask-a-question)
  - [Development](#development)
    - [Backend Setup](#backend-setup)
      - [Windows (PowerShell)](#windows-powershell)
      - [macOS](#macos)
      - [Linux](#linux)
    - [Frontend Setup](#frontend-setup)
      - [Windows (PowerShell)](#windows-powershell-1)
      - [macOS](#macos-1)
      - [Linux](#linux-1)
    - [LLM Services Setup](#llm-services-setup)
      - [Windows (PowerShell)](#windows-powershell-2)
      - [macOS](#macos-2)
      - [Linux](#linux-2)
    - [Run the whole project:](#run-the-whole-project)
  - [Docker](#docker)
  - [Testing](#testing)
  - [Contact](#contact)

## Introduction

Smart Excel Analyzer is an advanced tool designed to process, analyze, and query Excel files using state-of-the-art Language Model (LLM) technology and vector database capabilities.

This project aims to transform the way users interact with spreadsheet data by providing intelligent, natural language querying and deep data insights.

## Features

| Feature                           | Description                                                                |
|-----------------------------------|----------------------------------------------------------------------------|
| **Excel File Upload**             | Seamlessly upload Excel files for processing and analysis                  |
| **Natural Language Querying**     | Ask questions about your data in plain English                             |
| **Intelligent Data Analysis**     | Leverage LLM technology to extract insights from your Excel data           |
| **Local Data Analysis**           | This does not require outside internet access or third-party intervention  |
| **Vector Database Integration**   | Utilize Qdrant for efficient similarity search and data retrieval          |
| **Scalable Architecture**         | Designed to handle large datasets with distributed LLM services            |
| **Real-time Processing**          | Get quick responses to your queries with optimized data processing         |

### Detailed Features

- **Excel File Upload**:
  - Easily upload Excel files through the web interface.
  - Supports large files with efficient processing.

- **Natural Language Querying**:
  - Use natural language to ask questions about your data.
  - Example: "What are the total sales for Q1 2024?"

- **Intelligent Data Analysis**:
  - Extract insights using advanced LLM technology.
  - Provides summaries, trends, and anomalies in your data.

- **Local Data Analysis**:
  - Ensures data privacy by not requiring internet access.
  - All processing is done locally on your machine.

- **Vector Database Integration**:
  - Uses Qdrant for fast and efficient data retrieval.
  - Supports similarity search for complex queries.

- **Scalable Architecture**:
  - Handles large datasets with ease.
  - Uses distributed services for better performance.

- **Real-time Processing**:
  - Provides instant responses to queries.
  - Optimized for quick data processing.

## Tech Stack

- **Backend**:
  - ![C#](https://img.shields.io/badge/CSharp-239120?style=for-the-badge&logo=c-sharp&logoColor=white) [C# 12](https://learn.microsoft.com/en-us/dotnet/csharp/whats-new/csharp-12)
  - ![ASP.NET Core](https://img.shields.io/badge/ASP.NET_Core-512BD4?style=for-the-badge&logo=.net&logoColor=white) [ASP.NET Core 8.0](https://dotnet.microsoft.com/en-us/download/dotnet/8.0)

- **Frontend**:
  - ![React](https://img.shields.io/badge/React-20232A?style=for-the-badge&logo=react&logoColor=61DAFB) [React with TypeScript](https://react.dev/learn/typescript)
  - ![Vite](https://img.shields.io/badge/Vite-646CFF?style=for-the-badge&logo=vite&logoColor=white) [Vite for build tooling](https://vite.dev/)

- **Database**:
  - ![Qdrant](https://img.shields.io/badge/Qdrant-F24E1E?style=for-the-badge&logo=vector&logoColor=white) [Qdrant Vector Database](https://try.qdrant.tech/high-performance-vector-search?utm_source=google&utm_medium=cpc&utm_campaign=21518712216&utm_content=163351119817&utm_term=qdrant%20vector%20database&hsa_acc=6907203950&hsa_cam=21518712216&hsa_grp=163351119817&hsa_ad=707722911577&hsa_src=g&hsa_tgt=kwd-2240456171437&hsa_kw=qdrant%20vector%20database&hsa_mt=e&hsa_net=adwords&hsa_ver=3&gad_source=1&gclid=CjwKCAjw1NK4BhAwEiwAVUHPUDZxy5-yERXvEGdq-Q58x1xzDrpdB3rj1norGq_P6JDlU9vI3yXnyhoCTIoQAvD_BwE)

- **LLM Services**:
  - ![PyTorch](https://img.shields.io/badge/PyTorch-EE4C2C?style=for-the-badge&logo=pytorch&logoColor=white) [PyTorch](https://pytorch.org/)
  - ![Hugging Face](https://img.shields.io/badge/Hugging_Face-FFDA4E?style=for-the-badge&logo=huggingface&logoColor=white) [Hugging Face Transformers](https://huggingface.co/docs/transformers/en/index)

- **Containerization**:
  - ![Docker](https://img.shields.io/badge/Docker-2496ED?style=for-the-badge&logo=docker&logoColor=white) [Docker](https://app.docker.com/)
  - ![Docker Compose](https://img.shields.io/badge/Docker_Compose-2496ED?style=for-the-badge&logo=docker&logoColor=white) [Docker Compose](https://docs.docker.com/compose/)

- **API Documentation**:
  - ![Swagger](https://img.shields.io/badge/Swagger-85EA2D?style=for-the-badge&logo=swagger&logoColor=white) [Swagger / OpenAPI](https://swagger.io/docs/)

- **Testing**:
  - ![xUnit](https://img.shields.io/badge/xUnit-5C2D91?style=for-the-badge&logo=xunit&logoColor=white) [xUnit](https://xunit.net/)
  - ![Moq](https://img.shields.io/badge/Moq-FF6F00?style=for-the-badge&logo=moq&logoColor=white) [Moq](https://github.com/devlooped/moq)
  - ![FluentAssertions](https://img.shields.io/badge/Fluent_Assertions-D2B48C?style=for-the-badge&logo=fluentassertions&logoColor=white) [FluentAssertions](https://github.com/fluentassertions/fluentassertions)

- **Other Libraries**:
  - ![FluentValidation](https://img.shields.io/badge/FluentValidation-00C853?style=for-the-badge&logo=fluentvalidation&logoColor=white) [FluentValidation for request validation](https://github.com/FluentValidation/FluentValidation)
  - ![ExcelDataReader](https://img.shields.io/badge/ExcelDataReader-1D76DB?style=for-the-badge&logo=excel&logoColor=white) [ExcelDataReader for Fast Excel Data Loading](https://github.com/ExcelDataReader/ExcelDataReader)
  - ![MediatR](https://img.shields.io/badge/MediatR-FF6F00?style=for-the-badge&logo=mediatR&logoColor=white) [MediatR for CQRS pattern](https://github.com/jbogard/MediatR) **Only choice in my opinion**
  
## Architecture

The Smart Excel Analyzer follows a microservices architecture:

1. **Frontend Service**:
   - React application for user interaction.

2. **Backend Service**:
   - ASP.NET Core API handling business logic and orchestration.

3. **LLM Services**:
   - Multiple instances of Python-based services for natural language processing and embedding generation.

4. **Vector Database**:
   - Qdrant for storing and querying vector embeddings.

The application uses a CQRS (Command Query Responsibility Segregation) pattern with MediatR for efficient request handling.

## Setup and Installation

1. **Clone the repository**:

    ```bash
    git clone https://github.com/cwmasonRollTide/SmartExcelAnalyzer
    cd SmartExcelAnalyzer
    ```

2. **Ensure Docker and Docker Compose are installed on your system**:

    ```powershell
    # Download Docker Desktop Installer
    Invoke-WebRequest -Uri "https://desktop.docker.com/win/stable/Docker%20Desktop%20Installer.exe" -OutFile "DockerDesktopInstaller.exe"

    # Run the installer
    Start-Process -FilePath ".\DockerDesktopInstaller.exe" -Wait
    ```

3. **Create a `.env` file in the root directory and populate it with necessary environment variables** (refer to `.env.example`).

4. **Build and run the Docker containers**:

    ```bash
    docker compose up --build -d
    ```

5. **Access the frontend application** at [http://localhost:3000](http://localhost:3000).

6. **Access the backend API directly** at [http://localhost:5001/swagger](http://localhost:5001/swagger).

## Usage

1. **Upload an Excel file** through the web interface.

2. **Wait for the file to be processed and indexed**.

3. **Use the query interface** to ask questions about your data, life in general, the nature of the universe, or just about your day.

4. **View the results and insights** if you specify a number of rows, you will be given the option to save the results to a new excel file. Otherwise it will just try to answer your question.

## API Documentation

API documentation is available via Swagger UI. After starting the backend service, navigate to:
[http://localhost:5001/swagger](http://localhost:5001/swagger)

## Full Workflow Example

### 1. Upload Excel File

  **Request:**

  ```http
  POST /api/analysis/upload
  Content-Type: multipart/form-data
  ```

  **Response:**

  ```json
    "123456789012"
  ```

### 2. Get the Status of an Upload

**Request:**

  ```http
  GET /api/analysis/index/status/123456789012
  Content-Type: application/json
  ```

**Response:**

  ```csharp
  {
    IsIndexing: true,
    IndexingDocumentNbr: 10,
    TotalDocumentCount: 100,
    Error: string?
  }
  ```

### 3. Ask a Question

**Request:**

  ```http
  POST /api/analysis/api/query
  {
    "query": "What are the total sales for Q1 2024?",
    "documentId": "123456789012"
  }
  ```

**Response:**

  ```csharp
  {
    DocumentId: "123456789012",
    Question: "What are the total sales for Q1 2024?",
    Answer: "one... BILLION dollars, Dr. Eveel.",
    RelevantRows: ConcurrentBag<ConcurrentDictionary<string, object>>
  }
  ```

## Development

### Backend Setup

#### Windows (PowerShell)
```powershell
# Install .NET 8 SDK from https://dotnet.microsoft.com/download/dotnet/8.0
cd Backend
dotnet restore
```

#### macOS
```bash
# Install .NET 8 SDK
brew install dotnet@8

cd Backend
dotnet restore
```

#### Linux
```bash
# Install .NET 8 SDK (Ubuntu/Debian)
wget https://packages.microsoft.com/config/ubuntu/20.04/packages-microsoft-prod.deb -O packages-microsoft-prod.deb
sudo dpkg -i packages-microsoft-prod.deb
sudo apt-get update
sudo apt-get install -y dotnet-sdk-8.0

cd Backend
dotnet restore
```

### Frontend Setup

#### Windows (PowerShell)
```powershell
# Install Node.js from https://nodejs.org/
cd Frontend
npm install
npm run dev
```

#### macOS
```bash
# Install Node.js
brew install node

cd Frontend
npm install
npm run dev
```

#### Linux
```bash
# Install Node.js (Ubuntu/Debian)
curl -fsSL https://deb.nodesource.com/setup_lts.x | sudo -E bash -
sudo apt-get install -y nodejs

cd Frontend
npm install
npm run dev
```

### LLM Services Setup

#### Windows (PowerShell)
```powershell
# Install Python 3.9+ from https://www.python.org/downloads/
cd LLM
python -m venv venv
.\venv\Scripts\Activate.ps1
pip install -r requirements.txt
```

#### macOS
```bash
# Install Python 3.9+
brew install python@3.9

cd LLM
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
```

#### Linux
```bash
# Install Python 3.9+ (Ubuntu/Debian)
sudo apt-get update
sudo apt-get install -y python3.9 python3.9-venv python3-pip

cd LLM
python3 -m venv venv
source venv/bin/activate
pip install -r requirements.txt
```

### Run the whole project:

```powershell
cd SmartExcelAnalyzer
docker compose up --build -d
```

## Docker

- All of our docker images will be published to these docker hub container registries:

  - [Backend](https://hub.docker.com/repository/docker/fivemowner/smart-excel-analyzer-backend/general)

  - [Frontend](https://hub.docker.com/repository/docker/fivemowner/smart-excel-analyzer-frontend/general)

  - [LLM Service](https://hub.docker.com/repository/docker/fivemowner/smart-excel-analyzer-llm/general)

- This process is kicked off in the [GitHub Action File](.github/workflows/docker-build-push.yml)
  
## Testing

- Backend tests: Run dotnet test in the ./Backend/ directory.
  
- Frontend tests: Run npm test in the ./Frontend/ directory.
  
## Contact

- If you are trying to fork this or having trouble using the tool itself, feel free to contact me!

  - Connor Mason - <connor.mason@fivemconsulting.com>

  - [GitHub Repository](https://github.com/cwmasonRollTide/SmartExcelAnalyzer.git)
