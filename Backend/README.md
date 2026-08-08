# 📊 Smart Excel Analyzer Backend

- [![Smart Excel Analyzer .NET Backend CI/CID Workflow](https://github.com/cwmasonRollTide/SmartExcelAnalyzer/actions/workflows/backend-workflow.yml/badge.svg)](https://github.com/cwmasonRollTide/SmartExcelAnalyzer/actions/workflows/backend-workflow.yml)

*This C# project provides the backend API for the **Smart Excel Analyzer** application. It allows users to upload Excel files, extracts insights and summaries from the data using AI, and provides a natural language interface to query the data.*

---

## 🏗️ Architecture

The backend is built on **.NET 8** and follows **Clean Architecture / CQRS** with multiple layers:

- **API Layer** (`./API`): Defines the web API endpoints and HTTP interface. Key components:

  - `AnalysisController`: Handles Excel file upload and query submission

  - `BaseController`: Base class for API controllers

  - `ExceptionMiddleware`: Handles exceptions and generates error responses

- **Application Layer** (`./Application`): Contains the core application logic and workflows. Key components:  

  - `UploadFileCommand`: Handles processing of uploaded Excel files

  - `SubmitQuery`: Handles natural language query submission and generating responses

  - `ExcelFileService`: Service for parsing and summarizing Excel files

- **Domain Layer** (`./Domain`): Defines the core domain models and abstractions. Key components:

  - `Document`: Represents an analyzed Excel document

  - `Summary`: Represents an AI-generated summary of an Excel file

- **Persistence Layer** (`./Persistence`): Handles data storage and retrieval. Key components:

  - `DatabaseWrapper`: Abstracts database operations

  - `VectorRepository`: Stores and searches document embeddings for query answering

  - `EmbeddingCache`: Caches document embeddings to improve performance

---

## 🚀 Getting Started

### Prerequisites

- [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0)

- [Docker](https://www.docker.com/products/docker-desktop) (for containerized deployment)

- Qdrant database

### Configuration

Application settings are stored in `appsettings.json`. Key settings to configure:

- `DatabaseOptions`: Connection details for MongoDB or Qdrant database
- `LLMOptions`: Configuration for AI language model used for summarization and query answering

### 🐳 Building Docker Image

To build the docker image locally run the following command in the ./Backend directory

```powershell
docker buildx build -f backend.Dockerfile -t backend .
```

Then run the container with the command:

```powershell
docker run -p 5001:80 backend
```

This will build the docker image and start the single container instance. The API will be available at `http://localhost:5000`.

### 💻 Running Locally

#### To run the backend API locally without Docker

1. Ensure you have the .NET 8 SDK installed

2. Configure the `appsettings.json` file with your database connection details

3. From the `Backend` directory, run:

```powershell
dotnet run --project API
```

The API will be available at `http://localhost:5000` or `https://localhost:5000`.

#### With Docker Compose

1. Go to the root directory

2. Run the command

```powershell
docker compose up -d
```

3. The backend should be available at `http://localhost:5000`

### 📄 API Documentation

The backend uses Swagger to provide automatic API documentation. When the application is running, you can access the interactive Swagger UI at:

- `http://localhost:5000/swagger` (if running with Docker)
- `http://localhost:5000/swagger` or `https://localhost:5000/swagger` (if running locally)

This allows you to explore and test the available API endpoints.

---

## ✅ Testing

Unit tests are located in the `SmartExcelAnalyzer.Tests` project. To run the tests:

```powershell
dotnet test
```

Test coverage reports are generated in the `SmartExcelAnalyzer.Tests/TestResults` directory.

---

## 🛠️ CI/CD with GitHub Actions

The project uses GitHub Actions for Continuous Integration and Deployment to run the tests, build the image, and push it to docker hub. 
The workflow is defined in [a github action file](../.github/workflows/backend-workflow.yml)
