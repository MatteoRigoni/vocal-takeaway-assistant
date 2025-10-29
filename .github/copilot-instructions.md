# Copilot Instructions for vocal-takeaway-assistant

This document provides essential guidelines for AI coding agents working on the `vocal-takeaway-assistant` project. Follow these instructions to ensure productivity and alignment with the project's architecture and conventions.

## Project Overview

The `vocal-takeaway-assistant` is a reusable system for managing take-away orders via voice or text. It consists of three main components:

1. **Backend**: A .NET-based API (`backend/`) that handles business logic, data processing, and external integrations.
2. **Frontend**: An Angular-based user interface (`frontend/`) for interacting with the system.
3. **Reverse Proxy**: A layer for routing requests (`reverse-proxy/`), likely used for load balancing or API gateway functionality.

## Key Files and Directories

- **Backend**:
  - `Program.cs`: Entry point for the .NET API.
  - `Takeaway.Api.csproj`: Project configuration for the backend.
  - `appsettings.json` and `appsettings.Development.json`: Configuration files for different environments.
  - `Properties/launchSettings.json`: Debugging configurations.

- **Frontend**:
  - `src/app/`: Contains Angular components, services, and routing configurations.
  - `angular.json`: Angular CLI configuration.
  - `package.json`: Lists frontend dependencies and scripts.

- **Reverse Proxy**:
  - Likely contains configuration files for routing (e.g., `nginx.conf` or similar).

## Developer Workflows

### Building and Running

- **Backend**:
  - Use the .NET CLI to build and run the API:
    ```pwsh
    dotnet build backend/Takeaway.Api.csproj
    dotnet run --project backend/Takeaway.Api.csproj
    ```

- **Frontend**:
  - Install dependencies and start the development server:
    ```pwsh
    cd frontend
    npm install
    npm start
    ```

- **Docker**:
  - The project includes a `docker-compose.yml` file for containerized development. Use:
    ```pwsh
    docker-compose up
    ```

### Testing

- **Backend**:
  - Add and run tests using the .NET testing framework:
    ```pwsh
    dotnet test
    ```

- **Frontend**:
  - Run Angular unit tests:
    ```pwsh
    npm test
    ```

### Debugging

- **Backend**:
  - Use `launchSettings.json` for debugging configurations in Visual Studio or VS Code.

- **Frontend**:
  - Use browser developer tools and Angular CLI debugging options.

## Project-Specific Conventions

- **Backend**:
  - Follow .NET conventions for API design and dependency injection.
  - Use `appsettings.json` for environment-specific configurations.

- **Frontend**:
  - Use Angular's modular structure for components and services.
  - Define routes in `app.routes.ts`.

## Integration Points

- **Backend and Frontend**:
  - The frontend communicates with the backend via REST API endpoints defined in the `.http` files (e.g., `Takeaway.Api.http`).

- **Reverse Proxy**:
  - Ensure proper routing configurations to connect the frontend and backend.

## External Dependencies

- **Backend**:
  - Uses .NET libraries for API development.

- **Frontend**:
  - Uses Angular and dependencies listed in `package.json`.

- **Docker**:
  - Ensure Docker is installed and running for containerized workflows.

## Notes for AI Agents

- Always validate changes against the existing project structure and conventions.
- When adding new features, ensure they align with the modular architecture of the backend and frontend.
- Document any new workflows or configurations in this file.

For further clarification, consult the `README.md` or relevant configuration files.